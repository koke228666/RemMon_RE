using System.Collections.Generic;
using System.Linq;

namespace RemMon;

internal sealed class StutterDetector
{
	private readonly record struct FrameSample(double EventTimeSeconds, double FrameTimeMs);

	private const double WindowSeconds = 1.0;

	private const double MinimumHistorySeconds = 0.3;

	private const int MinimumHistoryFrames = 5;

	private const double HoldSeconds = 3.0;

	private const double MicrostutterRatio = 3.0;

	private const double StutterRatio = 5.0;

	private const double FreezeRatio = 15.0;

	private const double ReducedSensitivityMultiplier = 1.5;

	private const double MinFrameTimeMs = 0.05;

	private const double MaxFrameTimeMs = 1000.0;

	private readonly Queue<FrameSample> _samples = new Queue<FrameSample>();

	private StutterState _currentState;

	private StutterState _heldState;

	private double _holdUntilSeconds;

	private double? _previousFrameTimeMs;

	private double? _lastStutterFrameTimeMs;

	private double? _currentFrameTimeMs;

	private double? _medianFrameTimeMs;

	private double? _ratio;

	private double? _deltaFrameTimeMs;

	private int _stutterCount;

	public bool ReduceSensitivity { get; set; }

	public StutterDetectorSnapshot Snapshot => new StutterDetectorSnapshot(GetVisibleState(), _stutterCount, _lastStutterFrameTimeMs, _currentFrameTimeMs, _previousFrameTimeMs, _medianFrameTimeMs, _ratio, _deltaFrameTimeMs);

	public void AddFrameTime(double frameTimeMs, double eventTimeSeconds)
	{
		if (!IsValidFrameTime(frameTimeMs) || double.IsNaN(eventTimeSeconds) || double.IsInfinity(eventTimeSeconds))
		{
			return;
		}
		Prune(eventTimeSeconds);
		_currentFrameTimeMs = frameTimeMs;
		double? previousFrameTimeMs = _previousFrameTimeMs;
		_deltaFrameTimeMs = ((previousFrameTimeMs.HasValue && previousFrameTimeMs.GetValueOrDefault() > 0.0) ? new double?(frameTimeMs - _previousFrameTimeMs.Value) : ((double?)null));
		if (HasEnoughHistory(eventTimeSeconds))
		{
			_medianFrameTimeMs = Median(_samples.Select((FrameSample sample) => sample.FrameTimeMs));
			previousFrameTimeMs = _medianFrameTimeMs;
			_ratio = ((previousFrameTimeMs.HasValue && previousFrameTimeMs.GetValueOrDefault() > 0.0) ? new double?(frameTimeMs / _medianFrameTimeMs.Value) : ((double?)null));
			_currentState = Classify(_ratio);
			StutterState currentState = _currentState;
			if ((uint)(currentState - 3) <= 1u)
			{
				_stutterCount++;
				_lastStutterFrameTimeMs = frameTimeMs;
				HoldState(_currentState, eventTimeSeconds);
			}
			else if (_currentState == StutterState.Microstutter)
			{
				HoldState(StutterState.Microstutter, eventTimeSeconds);
			}
			else if (eventTimeSeconds >= _holdUntilSeconds)
			{
				_heldState = StutterState.Smooth;
			}
		}
		else
		{
			_currentState = StutterState.NoData;
			_medianFrameTimeMs = null;
			_ratio = null;
		}
		_samples.Enqueue(new FrameSample(eventTimeSeconds, frameTimeMs));
		_previousFrameTimeMs = frameTimeMs;
	}

	public void Reset()
	{
		_samples.Clear();
		_currentState = StutterState.NoData;
		_heldState = StutterState.NoData;
		_holdUntilSeconds = 0.0;
		_previousFrameTimeMs = null;
		_lastStutterFrameTimeMs = null;
		_currentFrameTimeMs = null;
		_medianFrameTimeMs = null;
		_ratio = null;
		_deltaFrameTimeMs = null;
		_stutterCount = 0;
	}

	private void HoldState(StutterState state, double eventTimeSeconds)
	{
		if (state == StutterState.Freeze)
		{
			_heldState = state;
			_holdUntilSeconds = eventTimeSeconds + 3.0;
		}
		else if (_heldState != StutterState.Freeze || !(eventTimeSeconds < _holdUntilSeconds))
		{
			if (state == StutterState.Stutter)
			{
				_heldState = state;
				_holdUntilSeconds = eventTimeSeconds + 3.0;
			}
			else if ((_heldState != StutterState.Stutter || !(eventTimeSeconds < _holdUntilSeconds)) && state == StutterState.Microstutter)
			{
				_heldState = state;
				_holdUntilSeconds = eventTimeSeconds + 3.0;
			}
		}
	}

	private StutterState GetVisibleState()
	{
		StutterState heldState = _heldState;
		if ((uint)(heldState - 2) <= 2u)
		{
			return _heldState;
		}
		if (_currentState != StutterState.NoData)
		{
			return StutterState.Smooth;
		}
		return StutterState.NoData;
	}

	private bool HasEnoughHistory(double eventTimeSeconds)
	{
		if (_samples.Count < 5)
		{
			return false;
		}
		return eventTimeSeconds - _samples.Peek().EventTimeSeconds >= 0.3;
	}

	private void Prune(double eventTimeSeconds)
	{
		double num = eventTimeSeconds - 1.0;
		while (_samples.Count > 0 && _samples.Peek().EventTimeSeconds < num)
		{
			_samples.Dequeue();
		}
	}

	private StutterState Classify(double? ratio)
	{
		if (!ratio.HasValue || !(ratio.GetValueOrDefault() > 0.0))
		{
			return StutterState.NoData;
		}
		double num = (ReduceSensitivity ? 1.5 : 1.0);
		if (ratio >= 15.0 * num)
		{
			return StutterState.Freeze;
		}
		if (ratio >= 5.0 * num)
		{
			return StutterState.Stutter;
		}
		if (ratio >= 3.0 * num)
		{
			return StutterState.Microstutter;
		}
		return StutterState.Smooth;
	}

	private static double Median(IEnumerable<double> values)
	{
		double[] array = values.OrderBy((double value) => value).ToArray();
		if (array.Length == 0)
		{
			return 0.0;
		}
		int num = array.Length / 2;
		if (array.Length % 2 != 1)
		{
			return (array[num - 1] + array[num]) / 2.0;
		}
		return array[num];
	}

	private static bool IsValidFrameTime(double frameTimeMs)
	{
		if (!double.IsNaN(frameTimeMs) && !double.IsInfinity(frameTimeMs))
		{
			if (frameTimeMs >= 0.05)
			{
				return frameTimeMs <= 1000.0;
			}
			return false;
		}
		return false;
	}
}
