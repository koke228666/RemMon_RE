using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RemMon;

public class App : Application
{
	private const string SingleInstanceMutexName = "Local\\RemMon.SingleInstance";

	private const string RemMonProcessName = "RemMon";

	private LicenseService? _licenseService;

	private MainWindow? _mainWindow;

	private Mutex? _singleInstanceMutex;

	private bool _ownsSingleInstanceMutex;

	private bool _applicationStarted;

	protected override async void OnStartup(StartupEventArgs e)
	{
		AppLaunchOptions.Parse(e.Args);
		base.ShutdownMode = ShutdownMode.OnExplicitShutdown;
		if (!EnsureSingleInstance())
		{
			Shutdown();
			return;
		}
		AppLogger.Initialize();
		_applicationStarted = true;
		base.DispatcherUnhandledException += delegate(object _, DispatcherUnhandledExceptionEventArgs args)
		{
			AppLogger.Crash("Unhandled UI exception", args.Exception);
			if (AppLogger.IsShuttingDown)
			{
				args.Handled = true;
			}
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs args)
		{
			if (args.ExceptionObject is Exception exception)
			{
				AppLogger.Crash("Unhandled app exception", exception);
			}
			else
			{
				AppLogger.Info($"Unhandled app exception: {args.ExceptionObject}");
			}
		};
		TaskScheduler.UnobservedTaskException += delegate(object? _, UnobservedTaskExceptionEventArgs args)
		{
			AppLogger.Crash("Unobserved task exception", args.Exception);
			args.SetObserved();
		};
		base.OnStartup(e);
		_licenseService = new LicenseService();
		LicenseState licenseState = _licenseService.CheckLocalLicense();
		OpenMainWindow(_licenseService);
		if (licenseState.IsValid)
		{
			CheckLicenseOnlineAfterStartupAsync(licenseState.License);
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (_applicationStarted)
		{
			AppLogger.BeginShutdown();
		}
		if (_ownsSingleInstanceMutex)
		{
			try
			{
				_singleInstanceMutex?.ReleaseMutex();
			}
			catch (ApplicationException)
			{
			}
		}
		_singleInstanceMutex?.Dispose();
		base.OnExit(e);
	}

	private bool EnsureSingleInstance()
	{
		_singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\RemMon.SingleInstance", out var createdNew);
		if (createdNew)
		{
			_ownsSingleInstanceMutex = true;
		}
		if (createdNew && !HasOtherRemMonInstance())
		{
			return true;
		}
		if (AppDialog.ShowAlreadyRunning() != AppDialogResult.Yes)
		{
			return false;
		}
		TerminateOtherInstances();
		if (HasOtherRemMonInstance())
		{
			ShowRestartFailure();
			return false;
		}
		if (_ownsSingleInstanceMutex)
		{
			return true;
		}
		try
		{
			_ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(8L));
		}
		catch (AbandonedMutexException)
		{
			_ownsSingleInstanceMutex = true;
		}
		if (_ownsSingleInstanceMutex)
		{
			return true;
		}
		ShowRestartFailure();
		return false;
	}

	private static bool HasOtherRemMonInstance()
	{
		int processId = Environment.ProcessId;
		Process[] processesByName = Process.GetProcessesByName("RemMon");
		bool result = false;
		Process[] array = processesByName;
		foreach (Process process in array)
		{
			using (process)
			{
				try
				{
					if (process.Id != processId && !process.HasExited)
					{
						result = true;
					}
				}
				catch (Exception)
				{
					result = true;
				}
			}
		}
		return result;
	}

	private static void TerminateOtherInstances()
	{
		using Process process = Process.GetCurrentProcess();
		Process[] processesByName = Process.GetProcessesByName("RemMon");
		foreach (Process process2 in processesByName)
		{
			using (process2)
			{
				if (process2.Id != process.Id)
				{
					try
					{
						process2.Kill(entireProcessTree: true);
						process2.WaitForExit(5000);
					}
					catch (Exception)
					{
					}
				}
			}
		}
	}

	private static void ShowRestartFailure()
	{
		AppDialog.Show(null, "Ошибка перезапуска", "Не удалось завершить работающую копию RemMon.", AppDialogKind.Error);
	}

	private void OpenMainWindow(LicenseService licenseService)
	{
		_mainWindow = new MainWindow(licenseService);
		base.MainWindow = _mainWindow;
		base.ShutdownMode = ShutdownMode.OnMainWindowClose;
		_mainWindow.Show();
	}

	private async Task CheckLicenseOnlineAfterStartupAsync(LicenseFile? startupLicense)
	{
		if (_licenseService == null)
		{
			return;
		}
		LicenseOperationResult licenseOperationResult = await _licenseService.CheckOnlineAsync();
		if (licenseOperationResult.Success)
		{
			return;
		}
		if (licenseOperationResult.Status == "offline" && startupLicense != null && _licenseService.IsOfflineGraceValid(startupLicense))
		{
			AppLogger.Info("online license check unavailable; local license accepted within grace period");
		}
		else
		{
			if (!licenseOperationResult.ShouldBlockApplication && licenseOperationResult.Status != "offline")
			{
				return;
			}
			if (licenseOperationResult.Status == "offline" && startupLicense != null && !_licenseService.IsOfflineGraceValid(startupLicense))
			{
				_licenseService.RemoveLicense();
			}
			else
			{
				if (!(licenseOperationResult.Status != "offline"))
				{
					return;
				}
				_licenseService.RemoveLicense();
			}
			AppDialog.Show(_mainWindow, "Активация", "Активация недействительна. Включена бесплатная версия программы.", AppDialogKind.Warning);
			_mainWindow?.RefreshLicenseState();
		}
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.8.0")]
	public static void Main()
	{
		new App().Run();
	}
}
