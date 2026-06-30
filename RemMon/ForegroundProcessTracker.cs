using System;
using System.Diagnostics;
using System.Linq;

namespace RemMon;

internal sealed class ForegroundProcessTracker
{
	private static readonly string[] IgnoredProcessNames = new string[27]
	{
		"RemMon", "ApplicationFrameHost", "chrome", "Codex", "Code", "devenv", "Discord", "dwm", "explorer", "firefox",
		"Lightshot", "msedge", "mstsc", "OpenConsole", "powershell", "pwsh", "SearchApp", "Taskmgr", "Telegram", "WindowsTerminal",
		"LockApp", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "SystemSettings", "TextInputHost", "WinStore.App"
	};

	private readonly int _ownProcessId = Environment.ProcessId;

	private ActiveProcessInfo? _lastProcess;

	public ActiveProcessInfo? LastProcess => _lastProcess;

	public ActiveProcessInfo? GetActiveProcess()
	{
		nint foregroundWindow = NativeMethods.GetForegroundWindow();
		if (foregroundWindow == IntPtr.Zero)
		{
			return GetLastLiveProcess();
		}
		NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var processId);
		if (processId == 0 || processId == _ownProcessId)
		{
			return GetLastLiveProcess();
		}
		try
		{
			using Process process = Process.GetProcessById((int)processId);
			string processName = process.ProcessName;
			if (IgnoredProcessNames.Any((string name) => processName.Equals(name, StringComparison.OrdinalIgnoreCase)))
			{
				return GetLastLiveProcess();
			}
			ActiveProcessInfo value = new ActiveProcessInfo(processId, processName);
			_lastProcess = value;
			return value;
		}
		catch
		{
			return GetLastLiveProcess();
		}
	}

	private ActiveProcessInfo? GetLastLiveProcess()
	{
		if (!_lastProcess.HasValue)
		{
			return null;
		}
		try
		{
			using Process process = Process.GetProcessById((int)_lastProcess.Value.ProcessId);
			if (process.HasExited)
			{
				_lastProcess = null;
				return null;
			}
			return _lastProcess;
		}
		catch
		{
			_lastProcess = null;
			return null;
		}
	}
}
