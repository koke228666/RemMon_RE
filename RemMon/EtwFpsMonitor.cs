using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace RemMon;

internal sealed class EtwFpsMonitor : IDisposable
{
	private static readonly TimeSpan GraphWindow = TimeSpan.FromSeconds(10L);

	private readonly object _sync = new object();

	private readonly ForegroundProcessTracker _foregroundProcessTracker = new ForegroundProcessTracker();

	private readonly FrameTimeBuffer _frameTimeBuffer = new FrameTimeBuffer();

	private readonly StutterDetector _stutterDetector = new StutterDetector();

	private readonly RenderInfoService _renderInfoService = new RenderInfoService();

	private readonly Dictionary<uint, double> _lastPresentSecondsByPid = new Dictionary<uint, double>();

	private readonly Dictionary<uint, RenderApiHint> _renderApiHintsByPid = new Dictionary<uint, RenderApiHint>();

	private readonly string _sessionName = $"RemMonEtwFps-{Environment.ProcessId}";

	private TraceEventSession? _session;

	private Task? _processingTask;

	private Timer? _flushTimer;

	private ActiveProcessInfo? _targetProcess;

	private RenderInfo _latestRenderInfo = RenderInfo.Empty;

	private DateTime _lastRenderInfoUpdateUtc = DateTime.MinValue;

	private DateTime _lastFastRenderInfoUpdateUtc = DateTime.MinValue;

	private int _acceptedPresentCount;

	private bool _stutterDetectionEnabled;

	private bool _renderInfoUpdateInProgress;

	private bool _disposed;

	public EtwFpsMonitor()
	{
		StartEtwSession();
	}

	public FpsStats GetStats(bool includeRenderInfo = true)
	{
		if (_disposed)
		{
			return FpsStats.Empty;
		}
		ActiveProcessInfo? activeProcess = _foregroundProcessTracker.GetActiveProcess();
		lock (_sync)
		{
			if (!activeProcess.HasValue)
			{
				_targetProcess = null;
				_lastPresentSecondsByPid.Clear();
				_renderApiHintsByPid.Clear();
				_frameTimeBuffer.Clear();
				_stutterDetector.Reset();
				return FpsStats.Empty;
			}
			if (_targetProcess?.ProcessId != activeProcess.Value.ProcessId)
			{
				_targetProcess = activeProcess.Value;
				_lastPresentSecondsByPid.Remove(activeProcess.Value.ProcessId);
				RemoveOldRenderApiHints(activeProcess.Value.ProcessId);
				_acceptedPresentCount = 0;
				_frameTimeBuffer.Clear();
				_stutterDetector.Reset();
				_latestRenderInfo = RenderInfo.Empty;
				_lastRenderInfoUpdateUtc = DateTime.MinValue;
				_lastFastRenderInfoUpdateUtc = DateTime.MinValue;
				_frameTimeBuffer.SetStatus("Waiting ETW presents");
				Log($"ETW FPS target: {activeProcess.Value.ProcessName}.exe PID {activeProcess.Value.ProcessId}");
			}
			if (_session == null)
			{
				_frameTimeBuffer.SetStatus("ETW session not started");
			}
			DateTime utcNow = DateTime.UtcNow;
			FpsStats stats = _frameTimeBuffer.GetStats(utcNow, activeProcess);
			if (!includeRenderInfo)
			{
				_latestRenderInfo = RenderInfo.Empty;
			}
			else
			{
				RenderApiHint? renderApiHint = GetRenderApiHint(activeProcess.Value.ProcessId, utcNow);
				ApplyFastRenderInfoFallback(activeProcess.Value, renderApiHint, utcNow);
				if (utcNow - _lastRenderInfoUpdateUtc >= TimeSpan.FromSeconds(1L))
				{
					QueueRenderInfoUpdate(activeProcess.Value, renderApiHint, utcNow);
				}
			}
			return new FpsStats
			{
				HasValues = stats.HasValues,
				ProcessName = stats.ProcessName,
				ProcessId = stats.ProcessId,
				Status = stats.Status,
				CurrentFps = stats.CurrentFps,
				AverageFps = stats.AverageFps,
				OnePercentLowFps = stats.OnePercentLowFps,
				PointOnePercentLowFps = stats.PointOnePercentLowFps,
				FrameTimeMs = stats.FrameTimeMs,
				RenderInfoText = _latestRenderInfo.Text
			};
		}
	}

	public FrameTimeGraphSnapshot GetFrameTimeGraphSnapshot()
	{
		if (_disposed)
		{
			return new FrameTimeGraphSnapshot(0.0, Array.Empty<FrameTimeGraphSample>());
		}
		return _frameTimeBuffer.GetGraphSnapshot(GraphWindow);
	}

	public double? GetLatestFrameTimeMs()
	{
		if (_disposed)
		{
			return null;
		}
		return _frameTimeBuffer.GetLatestFrameTimeMs(DateTime.UtcNow);
	}

	public void ConfigureStutterDetection(bool enabled, bool reduceSensitivity)
	{
		lock (_sync)
		{
			bool num = _stutterDetectionEnabled != enabled || _stutterDetector.ReduceSensitivity != reduceSensitivity;
			_stutterDetectionEnabled = enabled;
			_stutterDetector.ReduceSensitivity = reduceSensitivity;
			if (num)
			{
				_stutterDetector.Reset();
			}
		}
	}

	public StutterDetectorSnapshot GetStutterSnapshot()
	{
		lock (_sync)
		{
			return _stutterDetector.Snapshot;
		}
	}

	public void ResetStutterStats()
	{
		lock (_sync)
		{
			_stutterDetector.Reset();
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		lock (_sync)
		{
			_targetProcess = null;
			_frameTimeBuffer.Clear();
			_stutterDetector.Reset();
		}
		try
		{
			_flushTimer?.Dispose();
			_session?.Stop();
		}
		catch (Exception ex)
		{
			Log("ETW session stop failed: " + ex.Message);
		}
		try
		{
			_session?.Dispose();
		}
		catch
		{
		}
	}

	private void StartEtwSession()
	{
		try
		{
			StopStaleRemMonSessions();
			_session = new TraceEventSession(_sessionName, TraceEventSessionOptions.NoPerProcessorBuffering)
			{
				StopOnDispose = true,
				BufferQuantumKB = 4,
				BufferSizeMB = 16
			};
			EnableProvider("Microsoft-Windows-DXGI");
			EnableProvider("Microsoft-Windows-D3D9");
			EnableProvider("Microsoft-Windows-Direct3D11");
			EnableProvider("Microsoft-Windows-D3D11");
			EnableProvider("Microsoft-Windows-D3D12");
			EnableProvider("Microsoft-Windows-Vulkan-Loader");
			_session.Source.Dynamic.All += OnEtwEvent;
			_processingTask = Task.Run(delegate
			{
				try
				{
					_session.Source.Process();
				}
				catch (ObjectDisposedException)
				{
				}
				catch (Exception ex3)
				{
					if (_disposed || AppLogger.IsShuttingDown)
					{
						Log("ETW source processing stopped during shutdown: " + ex3.Message);
						return;
					}
					Log($"ETW source processing stopped: {ex3}");
					AppLogger.Crash("ETW source processing stopped", ex3);
					lock (_sync)
					{
						_frameTimeBuffer.SetStatus("ETW stopped");
					}
				}
			});
			Log("ETW FPS session started.");
			_flushTimer = new Timer(delegate
			{
				FlushEtwSession();
			}, null, TimeSpan.FromMilliseconds(250L), TimeSpan.FromMilliseconds(250L));
		}
		catch (Exception ex)
		{
			Log($"ETW FPS session failed to start: {ex}");
			AppLogger.Crash("ETW FPS session failed to start", ex);
			lock (_sync)
			{
				_frameTimeBuffer.SetStatus("ETW start failed");
			}
		}
	}

	private void EnableProvider(string providerName)
	{
		try
		{
			_session?.EnableProvider(providerName, TraceEventLevel.Verbose, ulong.MaxValue);
			Log("ETW provider enabled: " + providerName);
		}
		catch (Exception ex)
		{
			Log("ETW provider enable failed for " + providerName + ": " + ex.Message);
		}
	}

	private void FlushEtwSession()
	{
		if (_disposed)
		{
			return;
		}
		try
		{
			_session?.Flush();
		}
		catch
		{
		}
	}

	private void OnEtwEvent(TraceEvent data)
	{
		if (_disposed)
		{
			return;
		}
		ActiveProcessInfo? targetProcess;
		lock (_sync)
		{
			targetProcess = _targetProcess;
		}
		if (!targetProcess.HasValue)
		{
			return;
		}
		if (data.ProcessID == (int)targetProcess.Value.ProcessId)
		{
			UpdateRenderApiHint(data, targetProcess.Value.ProcessId);
		}
		if (!LooksLikePresentEvent(data) || data.ProcessID != (int)targetProcess.Value.ProcessId)
		{
			return;
		}
		double num = data.TimeStampRelativeMSec / 1000.0;
		lock (_sync)
		{
			if (_disposed || _targetProcess?.ProcessId != targetProcess.Value.ProcessId || !_lastPresentSecondsByPid.TryGetValue(targetProcess.Value.ProcessId, out var value))
			{
				_lastPresentSecondsByPid[targetProcess.Value.ProcessId] = num;
				return;
			}
			double num2 = (num - value) * 1000.0;
			_lastPresentSecondsByPid[targetProcess.Value.ProcessId] = num;
			if ((!(num2 < 1.0) && !(num2 > 1000.0)) || 1 == 0)
			{
				_frameTimeBuffer.AddFrameTime(num2, num, DateTime.UtcNow);
				if (_stutterDetectionEnabled)
				{
					_stutterDetector.AddFrameTime(num2, num);
				}
				if (string.IsNullOrWhiteSpace(_latestRenderInfo.ApiText))
				{
					_latestRenderInfo = MergeRenderInfo(_latestRenderInfo, new RenderInfo
					{
						ApiText = "DirectX"
					});
				}
				_acceptedPresentCount++;
			}
		}
	}

	private void ApplyFastRenderInfoFallback(ActiveProcessInfo processInfo, RenderApiHint? etwHint, DateTime nowUtc)
	{
		if (!(nowUtc - _lastFastRenderInfoUpdateUtc >= TimeSpan.FromSeconds(1L)) && !etwHint.HasValue)
		{
			int? width = _latestRenderInfo.Width;
			if (width.HasValue && width.GetValueOrDefault() > 0)
			{
				width = _latestRenderInfo.Height;
				if (width.HasValue && width.GetValueOrDefault() > 0)
				{
					return;
				}
			}
		}
		RenderInfo fastRenderInfo = _renderInfoService.GetFastRenderInfo(processInfo, etwHint);
		_latestRenderInfo = MergeRenderInfo(_latestRenderInfo, fastRenderInfo);
		_lastFastRenderInfoUpdateUtc = nowUtc;
	}

	private static RenderInfo MergeRenderInfo(RenderInfo current, RenderInfo fallback)
	{
		RenderInfo obj = new RenderInfo
		{
			ApiText = ChooseBetterApiText(current.ApiText, fallback.ApiText)
		};
		int? width = current.Width;
		obj.Width = ((width.HasValue && width.GetValueOrDefault() > 0) ? current.Width : fallback.Width);
		width = current.Height;
		obj.Height = ((width.HasValue && width.GetValueOrDefault() > 0) ? current.Height : fallback.Height);
		return obj;
	}

	private static string? ChooseBetterApiText(string? current, string? fallback)
	{
		if (string.IsNullOrWhiteSpace(current))
		{
			return fallback;
		}
		if (string.IsNullOrWhiteSpace(fallback))
		{
			return current;
		}
		if (GetApiSpecificity(fallback) <= GetApiSpecificity(current))
		{
			return current;
		}
		return fallback;
	}

	private static int GetApiSpecificity(string api)
	{
		if (api.Contains("DirectX 12", StringComparison.OrdinalIgnoreCase) || api.Contains("DirectX 11", StringComparison.OrdinalIgnoreCase) || api.Contains("DirectX 10", StringComparison.OrdinalIgnoreCase) || api.Contains("DirectX 9", StringComparison.OrdinalIgnoreCase) || api.Contains("Vulkan", StringComparison.OrdinalIgnoreCase) || api.Contains("OpenGL", StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		return api.Contains("DirectX", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
	}

	private void QueueRenderInfoUpdate(ActiveProcessInfo processInfo, RenderApiHint? etwHint, DateTime nowUtc)
	{
		if (_renderInfoUpdateInProgress)
		{
			return;
		}
		_renderInfoUpdateInProgress = true;
		_lastRenderInfoUpdateUtc = nowUtc;
		Task.Run(delegate
		{
			RenderInfo current = RenderInfo.Empty;
			try
			{
				current = _renderInfoService.GetRenderInfo(processInfo, etwHint);
			}
			catch (Exception ex)
			{
				Log($"Render API detection failed: Process={processInfo.ProcessName}.exe PID={processInfo.ProcessId}; Error={ex.GetType().Name}: {ex.Message}");
			}
			lock (_sync)
			{
				_renderInfoUpdateInProgress = false;
				if (!_disposed && _targetProcess?.ProcessId == processInfo.ProcessId)
				{
					_latestRenderInfo = MergeRenderInfo(current, _latestRenderInfo);
				}
			}
		});
	}

	private static bool LooksLikePresentEvent(TraceEvent data)
	{
		string obj = data.ProviderName ?? string.Empty;
		string text = data.EventName ?? string.Empty;
		if (obj.Equals("Microsoft-Windows-DXGI", StringComparison.OrdinalIgnoreCase))
		{
			return text.Equals("IDXGISwapChain_Present/Start", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private void UpdateRenderApiHint(TraceEvent data, uint processId)
	{
		string renderApiFromEtwProvider = GetRenderApiFromEtwProvider(data.ProviderName);
		if (renderApiFromEtwProvider == null)
		{
			return;
		}
		lock (_sync)
		{
			if (!_renderApiHintsByPid.TryGetValue(processId, out var value) || GetApiSpecificity(value.Api) <= GetApiSpecificity(renderApiFromEtwProvider))
			{
				_renderApiHintsByPid[processId] = new RenderApiHint(renderApiFromEtwProvider, DateTime.UtcNow);
			}
		}
	}

	private RenderApiHint? GetRenderApiHint(uint processId, DateTime nowUtc)
	{
		if (!_renderApiHintsByPid.TryGetValue(processId, out var value))
		{
			return null;
		}
		if (nowUtc - value.UpdatedUtc > TimeSpan.FromSeconds(15L))
		{
			_renderApiHintsByPid.Remove(processId);
			return null;
		}
		return value;
	}

	private void RemoveOldRenderApiHints(uint currentProcessId)
	{
		uint[] array = _renderApiHintsByPid.Keys.Where((uint processId) => processId != currentProcessId).ToArray();
		foreach (uint key in array)
		{
			_renderApiHintsByPid.Remove(key);
		}
	}

	private static string? GetRenderApiFromEtwProvider(string? providerName)
	{
		if (string.IsNullOrWhiteSpace(providerName))
		{
			return null;
		}
		if (providerName.Equals("Microsoft-Windows-DXGI", StringComparison.OrdinalIgnoreCase))
		{
			return "DirectX";
		}
		if (providerName.Equals("Microsoft-Windows-D3D9", StringComparison.OrdinalIgnoreCase))
		{
			return "DirectX 9";
		}
		if (providerName.Equals("Microsoft-Windows-Direct3D11", StringComparison.OrdinalIgnoreCase) || providerName.Equals("Microsoft-Windows-D3D11", StringComparison.OrdinalIgnoreCase))
		{
			return "DirectX 11";
		}
		if (providerName.Equals("Microsoft-Windows-D3D12", StringComparison.OrdinalIgnoreCase))
		{
			return "DirectX 12";
		}
		if (providerName.Equals("Microsoft-Windows-Vulkan-Loader", StringComparison.OrdinalIgnoreCase))
		{
			return "Vulkan";
		}
		return null;
	}

	private void StopStaleRemMonSessions()
	{
		foreach (string item in from name in TraceEventSession.GetActiveSessionNames()
			where name.StartsWith("RemMonEtwFps-", StringComparison.OrdinalIgnoreCase)
			select name)
		{
			try
			{
				TraceEventSession.GetActiveSession(item)?.Stop();
				Log("Stopped stale ETW session: " + item);
			}
			catch (Exception ex)
			{
				Log("Could not stop stale ETW session " + item + ": " + ex.Message);
			}
		}
	}

	private void Log(string message)
	{
		AppLogger.Info(message);
	}
}
