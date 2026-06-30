using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RemMon;

internal sealed class FrameTimeBuffer
{
	private readonly record struct FrameSample(double EventTimeSeconds, double FrameTimeMs);

	private readonly record struct GraphFrameSample(double TimeSeconds, double FrameTimeMs);

	private const double MinFrameTimeMs = 0.05;

	private const double MaxFrameTimeMs = 1000.0;

	private const int GraphSampleCapacity = 20000;

	private static readonly TimeSpan CurrentWindow = TimeSpan.FromMilliseconds(500L);

	private static readonly TimeSpan AverageWindow = TimeSpan.FromSeconds(60L);

	private static readonly TimeSpan LowWindow = TimeSpan.FromSeconds(10L);

	private static readonly TimeSpan RetentionWindow = TimeSpan.FromSeconds(70L);

	private static readonly TimeSpan GraphRetentionWindow = TimeSpan.FromSeconds(12L);

	private static readonly TimeSpan StaleTimeout = TimeSpan.FromSeconds(3L);

	private readonly object _sync = new object();

	private readonly Queue<FrameSample> _samples = new Queue<FrameSample>();

	private readonly GraphFrameSample[] _graphSamples = new GraphFrameSample[20000];

	private DateTime _lastReceivedUtc = DateTime.MinValue;

	private int _graphStart;

	private int _graphCount;

	private double _latestGraphEventTimeSeconds;

	private double _latestGraphReceiveTimeSeconds;

	private double? _latestFrameTimeMs;

	private string? _status;

	public void AddFrameTime(double frameTimeMs)
	{
		double monotonicSeconds = GetMonotonicSeconds();
		AddFrameTime(frameTimeMs, monotonicSeconds, DateTime.UtcNow);
	}

	public void AddFrameTime(double frameTimeMs, double eventTimeSeconds, DateTime receivedUtc)
	{
		bool flag = double.IsNaN(frameTimeMs) || double.IsInfinity(frameTimeMs);
		if (!flag)
		{
			bool flag2 = ((frameTimeMs < 0.05 || frameTimeMs > 1000.0) ? true : false);
			flag = flag2;
		}
		if (flag || double.IsNaN(eventTimeSeconds) || double.IsInfinity(eventTimeSeconds))
		{
			return;
		}
		lock (_sync)
		{
			_samples.Enqueue(new FrameSample(eventTimeSeconds, frameTimeMs));
			AddGraphSample(eventTimeSeconds, frameTimeMs);
			_latestGraphEventTimeSeconds = eventTimeSeconds;
			_latestGraphReceiveTimeSeconds = GetMonotonicSeconds();
			_latestFrameTimeMs = frameTimeMs;
			_lastReceivedUtc = receivedUtc;
			_status = null;
			Prune(eventTimeSeconds);
		}
	}

	public void SetStatus(string status)
	{
		lock (_sync)
		{
			_status = status;
		}
	}

	public void Clear()
	{
		lock (_sync)
		{
			_samples.Clear();
			_graphStart = 0;
			_graphCount = 0;
			_latestGraphEventTimeSeconds = 0.0;
			_latestGraphReceiveTimeSeconds = 0.0;
			_latestFrameTimeMs = null;
			_lastReceivedUtc = DateTime.MinValue;
			_status = null;
		}
	}

	public double? GetLatestFrameTimeMs(DateTime nowUtc)
	{
		double? num;
		lock (_sync)
		{
			num = _latestFrameTimeMs;
			if (!num.HasValue || !(num.GetValueOrDefault() > 0.0) || nowUtc - _lastReceivedUtc > StaleTimeout)
			{
				num = null;
				num = num;
			}
			else
			{
				num = _latestFrameTimeMs;
			}
		}
		return num;
	}

	public FrameTimeGraphSnapshot GetGraphSnapshot(TimeSpan window)
	{
		lock (_sync)
		{
			if (_graphCount == 0)
			{
				return new FrameTimeGraphSnapshot(0.0, Array.Empty<FrameTimeGraphSample>());
			}
			double graphNowSeconds = GetGraphNowSeconds();
			double num = graphNowSeconds - window.TotalSeconds;
			List<FrameTimeGraphSample> list = new List<FrameTimeGraphSample>(_graphCount);
			for (int i = 0; i < _graphCount; i++)
			{
				GraphFrameSample graphFrameSample = _graphSamples[(_graphStart + i) % 20000];
				if (graphFrameSample.TimeSeconds >= num)
				{
					list.Add(new FrameTimeGraphSample(graphFrameSample.TimeSeconds, graphFrameSample.FrameTimeMs));
				}
			}
			return new FrameTimeGraphSnapshot(graphNowSeconds, list.ToArray());
		}
	}

	private double GetGraphNowSeconds()
	{
		if (_latestGraphReceiveTimeSeconds <= 0.0)
		{
			return _latestGraphEventTimeSeconds;
		}
		double num = Math.Max(0.0, GetMonotonicSeconds() - _latestGraphReceiveTimeSeconds);
		return _latestGraphEventTimeSeconds + num;
	}

	public FpsStats GetStats(DateTime nowUtc, ActiveProcessInfo? process)
	{
		lock (_sync)
		{
			if (!process.HasValue)
			{
				return FpsStats.Empty;
			}
			if (_samples.Count == 0 || nowUtc - _lastReceivedUtc > StaleTimeout)
			{
				return new FpsStats
				{
					ProcessId = process.Value.ProcessId,
					ProcessName = process.Value.ProcessName,
					Status = (_status ?? "No frame data")
				};
			}
			double eventTimeSeconds = _samples.Last().EventTimeSeconds;
			Prune(eventTimeSeconds);
			FrameSample[] samples = GetSamples(eventTimeSeconds, CurrentWindow);
			FrameSample[] samples2 = GetSamples(eventTimeSeconds, AverageWindow);
			FrameSample[] samples3 = GetSamples(eventTimeSeconds, LowWindow);
			if (samples.Length == 0 || samples2.Length == 0 || samples3.Length == 0)
			{
				return new FpsStats
				{
					ProcessId = process.Value.ProcessId,
					ProcessName = process.Value.ProcessName,
					Status = "Waiting for samples"
				};
			}
			double windowFps = GetWindowFps(samples, eventTimeSeconds, CurrentWindow);
			double windowFps2 = GetWindowFps(samples2, eventTimeSeconds, AverageWindow);
			double value = ((windowFps > 0.0) ? (1000.0 / windowFps) : samples.Average((FrameSample sample) => sample.FrameTimeMs));
			return new FpsStats
			{
				HasValues = true,
				ProcessId = process.Value.ProcessId,
				ProcessName = process.Value.ProcessName,
				CurrentFps = windowFps,
				AverageFps = windowFps2,
				OnePercentLowFps = GetAverageWorstFrameFps(samples3, 0.01),
				PointOnePercentLowFps = GetAverageWorstFrameFps(samples3, 0.001),
				FrameTimeMs = value
			};
		}
	}

	private void AddGraphSample(double timeSeconds, double frameTimeMs)
	{
		if (_graphCount < 20000)
		{
			int num = (_graphStart + _graphCount) % 20000;
			_graphSamples[num] = new GraphFrameSample(timeSeconds, frameTimeMs);
			_graphCount++;
			PruneGraphSamples(timeSeconds);
		}
		else
		{
			_graphSamples[_graphStart] = new GraphFrameSample(timeSeconds, frameTimeMs);
			_graphStart = (_graphStart + 1) % 20000;
			PruneGraphSamples(timeSeconds);
		}
	}

	private void PruneGraphSamples(double latestEventTimeSeconds)
	{
		double num = latestEventTimeSeconds - GraphRetentionWindow.TotalSeconds;
		while (_graphCount > 0 && _graphSamples[_graphStart].TimeSeconds < num)
		{
			_graphStart = (_graphStart + 1) % 20000;
			_graphCount--;
		}
	}

	private FrameSample[] GetSamples(double latestEventTimeSeconds, TimeSpan window)
	{
		double minTime = latestEventTimeSeconds - window.TotalSeconds;
		return _samples.Where((FrameSample sample) => sample.EventTimeSeconds >= minTime).ToArray();
	}

	private void Prune(double latestEventTimeSeconds)
	{
		double num = latestEventTimeSeconds - RetentionWindow.TotalSeconds;
		while (_samples.Count > 0 && _samples.Peek().EventTimeSeconds < num)
		{
			_samples.Dequeue();
		}
	}

	private static double GetAverageWorstFrameFps(FrameSample[] samples, double fraction)
	{
		if (samples.Length == 0)
		{
			return 0.0;
		}
		int count = Math.Max(1, (int)Math.Ceiling((double)samples.Length * fraction));
		return (from sample in samples
			select ToFps(sample.FrameTimeMs) into value
			orderby value
			select value).Take(count).Average();
	}

	private static double GetWindowFps(FrameSample[] samples, double latestEventTimeSeconds, TimeSpan window)
	{
		if (samples.Length <= 1)
		{
			if (samples.Length != 1)
			{
				return 0.0;
			}
			return ToFps(samples[0].FrameTimeMs);
		}
		double eventTimeSeconds = samples[0].EventTimeSeconds;
		double num = latestEventTimeSeconds - eventTimeSeconds;
		if (num <= 0.0)
		{
			return samples.Average((FrameSample sample) => ToFps(sample.FrameTimeMs));
		}
		return Math.Max(0.0, (double)(samples.Length - 1) / num);
	}

	private static double ToFps(double frameTimeMs)
	{
		if (!(frameTimeMs > 0.0))
		{
			return 0.0;
		}
		return 1000.0 / frameTimeMs;
	}

	private static double GetMonotonicSeconds()
	{
		return (double)Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
	}
}
