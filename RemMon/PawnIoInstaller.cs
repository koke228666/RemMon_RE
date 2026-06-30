using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using Microsoft.Win32;

namespace RemMon;

internal static class PawnIoInstaller
{
	private readonly record struct PawnIoFiles(bool LibExists, bool SysExists, string LibPath, string SysPath);

	public const string OfficialUrl = "https://pawnio.eu/";

	private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "RemMon.log");

	public static string GetStatus()
	{
		string obj = (IsInstalled() ? "INSTALLED" : "NOT INSTALLED");
		string serviceStateText = GetServiceStateText();
		return obj + ", Service: " + serviceStateText;
	}

	public static PawnIoInstallResult EnsureInstalled()
	{
		Log("PawnIO check started. Current status: " + GetStatus());
		Log(GetDetailedStatus());
		if (IsInstalled())
		{
			Log("PawnIO is already installed. Skipping install.");
			TryStartServiceIfRegistered();
			Log("PawnIO check finished. Current status: " + GetStatus());
			Log(GetDetailedStatus());
			return PawnIoInstallResult.Ok;
		}
		string bundledSetupPath = GetBundledSetupPath();
		if (bundledSetupPath != null)
		{
			Log("PawnIO is not installed. Installing bundled setup: " + bundledSetupPath);
			RunProcess(bundledSetupPath, "-install -silent", TimeSpan.FromMinutes(5L));
			TryStartServiceIfRegistered();
			Log("PawnIO bundled install finished. Current status: " + GetStatus());
			Log(GetDetailedStatus());
			if (!IsInstalled())
			{
				return PawnIoInstallResult.ManualInstallRequired("Встроенный установщик PawnIO был запущен, но PawnIO всё ещё не найден.");
			}
			return PawnIoInstallResult.Ok;
		}
		if (FindExecutableOnPath("winget.exe") == null)
		{
			Log("PawnIO не установлен, встроенный установщик не найден, а winget недоступен. Это часто встречается на Windows LTSC." + " Manual install is required: https://pawnio.eu/");
			return PawnIoInstallResult.ManualInstallRequired("PawnIO не установлен, встроенный установщик не найден, а winget недоступен. Это часто встречается на Windows LTSC.");
		}
		Log("PawnIO is not installed. Installing through winget.");
		bool num = RunProcess("winget.exe", "install --id namazso.PawnIO --exact --accept-package-agreements --accept-source-agreements --silent --disable-interactivity", TimeSpan.FromMinutes(5L));
		TryStartServiceIfRegistered();
		Log("PawnIO winget install finished. Current status: " + GetStatus());
		Log(GetDetailedStatus());
		if (num && IsInstalled())
		{
			return PawnIoInstallResult.Ok;
		}
		return PawnIoInstallResult.ManualInstallRequired("winget найден, но не смог установить PawnIO автоматически.");
	}

	public static bool IsInstalled()
	{
		if (HasUninstallRegistryKey())
		{
			return true;
		}
		if (TryGetServiceState(out string _))
		{
			return true;
		}
		PawnIoFiles pawnIoFiles = GetPawnIoFiles();
		if (!pawnIoFiles.LibExists)
		{
			return pawnIoFiles.SysExists;
		}
		return true;
	}

	private static bool HasUninstallRegistryKey()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\PawnIO");
			if (registryKey != null)
			{
				return true;
			}
		}
		catch
		{
		}
		try
		{
			using RegistryKey registryKey2 = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\PawnIO");
			return registryKey2 != null;
		}
		catch
		{
			return false;
		}
	}

	private static string GetServiceStateText()
	{
		if (!TryGetServiceState(out string state))
		{
			return "NOT REGISTERED";
		}
		return state ?? "UNKNOWN";
	}

	private static bool TryGetServiceState(out string? state)
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT State FROM Win32_Service WHERE Name = 'PawnIO'");
			using (ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectSearcher.Get().GetEnumerator())
			{
				if (managementObjectEnumerator.MoveNext())
				{
					ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
					state = managementObject["State"]?.ToString();
					state = (string.IsNullOrWhiteSpace(state) ? "UNKNOWN" : state.ToUpperInvariant());
					return true;
				}
			}
			state = null;
			return false;
		}
		catch
		{
			state = "UNKNOWN";
			return true;
		}
	}

	private static void TryStartServiceIfRegistered()
	{
		if (!TryGetServiceState(out string state))
		{
			Log("PawnIO service is not registered. Skipping service start; PawnIO may be loaded on demand by LibreHardwareMonitor.");
		}
		else if (!(state == "RUNNING"))
		{
			RunProcess("sc.exe", "start PawnIO", TimeSpan.FromSeconds(20L));
		}
	}

	private static string? GetBundledSetupPath()
	{
		string text = Path.Combine(AppContext.BaseDirectory, "tools", "PawnIO_setup.exe");
		if (!File.Exists(text))
		{
			return null;
		}
		return text;
	}

	private static string GetDetailedStatus()
	{
		bool flag = HasUninstallRegistryKey();
		PawnIoFiles pawnIoFiles = GetPawnIoFiles();
		string serviceStateText = GetServiceStateText();
		return $"PawnIO details: Registry={(flag ? "FOUND" : "NOT FOUND")}, PawnIOLib.dll={(pawnIoFiles.LibExists ? pawnIoFiles.LibPath : "NOT FOUND")}, PawnIO.sys={(pawnIoFiles.SysExists ? pawnIoFiles.SysPath : "NOT FOUND")}, Service={serviceStateText}";
	}

	private static PawnIoFiles GetPawnIoFiles()
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO");
			string text = Path.Combine(path, "PawnIOLib.dll");
			string text2 = Path.Combine(path, "PawnIO.sys");
			return new PawnIoFiles(File.Exists(text), File.Exists(text2), text, text2);
		}
		catch
		{
			return new PawnIoFiles(LibExists: false, SysExists: false, string.Empty, string.Empty);
		}
	}

	private static bool RunProcess(string fileName, string arguments, TimeSpan timeout)
	{
		try
		{
			Log("Starting process: " + fileName + " " + arguments);
			using Process process = Process.Start(new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden
			});
			if (process == null)
			{
				Log("Process did not start: " + fileName);
				return false;
			}
			bool num = process.WaitForExit(timeout);
			if (num)
			{
				Log($"Process exited: {fileName}, ExitCode={process.ExitCode}");
			}
			else
			{
				Log("Process timeout: " + fileName);
			}
			return num && process.ExitCode == 0;
		}
		catch (Exception ex)
		{
			Log("Process failed: " + fileName + ", Error=" + ex.Message);
			return false;
		}
	}

	private static string? FindExecutableOnPath(string fileName)
	{
		try
		{
			string environmentVariable = Environment.GetEnvironmentVariable("PATH");
			if (string.IsNullOrWhiteSpace(environmentVariable))
			{
				return null;
			}
			return (from directory in environmentVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				select Path.Combine(directory, fileName)).FirstOrDefault(File.Exists);
		}
		catch
		{
			return null;
		}
	}

	private static void Log(string message)
	{
		string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
		try
		{
			File.AppendAllText(LogPath, text + Environment.NewLine, Encoding.UTF8);
		}
		catch
		{
		}
	}
}
