using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace RemMon;

internal static class AppLogger
{
	private static readonly object Sync = new object();

	private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "RemMon.log");

	private static readonly string SensorLogPath = Path.Combine(AppContext.BaseDirectory, "RemMon-sensors.log");

	private static bool _initialized;

	private static bool _isShuttingDown;

	private static bool _sensorDiagnosticsEnabled;

	public static bool SensorDiagnosticsEnabled
	{
		get
		{
			lock (Sync)
			{
				return _sensorDiagnosticsEnabled;
			}
		}
	}

	public static bool IsShuttingDown
	{
		get
		{
			lock (Sync)
			{
				return _isShuttingDown;
			}
		}
	}

	public static void Initialize()
	{
		lock (Sync)
		{
			if (_initialized)
			{
				return;
			}
			_initialized = true;
			try
			{
				File.WriteAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] RemMon log started.{Environment.NewLine}", Encoding.UTF8);
			}
			catch
			{
			}
		}
	}

	public static void Info(string message)
	{
		if (!message.Contains("ETW target event", StringComparison.OrdinalIgnoreCase))
		{
			Write(message);
		}
	}

	public static void SetSensorDiagnosticsEnabled(bool enabled)
	{
		lock (Sync)
		{
			if (_sensorDiagnosticsEnabled == enabled)
			{
				return;
			}
			_sensorDiagnosticsEnabled = enabled;
			try
			{
				if (enabled)
				{
					File.WriteAllText(SensorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] RemMon extended sensor diagnostics started.{Environment.NewLine}", Encoding.UTF8);
				}
			}
			catch
			{
			}
		}
		Info(enabled ? "Extended sensor diagnostics enabled." : "Extended sensor diagnostics disabled.");
	}

	public static void SensorDiagnostics(string message)
	{
		lock (Sync)
		{
			if (!_sensorDiagnosticsEnabled)
			{
				return;
			}
		}
		string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
		Task.Run(delegate
		{
			lock (Sync)
			{
				if (!_sensorDiagnosticsEnabled)
				{
					return;
				}
				try
				{
					File.AppendAllText(SensorLogPath, text + Environment.NewLine, Encoding.UTF8);
				}
				catch
				{
				}
			}
		});
	}

	public static void BeginShutdown()
	{
		bool isShuttingDown;
		lock (Sync)
		{
			isShuttingDown = _isShuttingDown;
			_isShuttingDown = true;
		}
		if (!isShuttingDown)
		{
			Info("RemMon shutdown started.");
		}
	}

	public static void Crash(string context, Exception exception)
	{
		if (IsShuttingDown)
		{
			Info(context + " ignored during shutdown: " + exception.Message);
			return;
		}
		Write($"{context}: {exception}");
		SaveCrashReport();
	}

	private static void Write(string message)
	{
		Initialize();
		string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
		lock (Sync)
		{
			try
			{
				File.AppendAllText(LogPath, text + Environment.NewLine, Encoding.UTF8);
			}
			catch
			{
			}
		}
	}

	private static void SaveCrashReport()
	{
		lock (Sync)
		{
			try
			{
				string text = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
				string path = Path.Combine(AppContext.BaseDirectory, "crash-report-" + text + ".log");
				string contents = (File.Exists(LogPath) ? File.ReadAllText(LogPath, Encoding.UTF8) : string.Empty);
				File.WriteAllText(path, contents, Encoding.UTF8);
			}
			catch
			{
			}
		}
	}
}
