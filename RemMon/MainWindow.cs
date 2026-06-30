using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RemMon;

public partial class MainWindow : Window, IComponentConnector
{
	private sealed record CpuCoreClockRow(Grid Container, TextBlock Header, TextBlock LeftLabel, TextBlock LeftValue, TextBlock Separator, TextBlock RightLabel, TextBlock RightValue);

	private sealed record CpuCoreClockDisplayRow(string? Header, CpuCoreClockReading? Left, CpuCoreClockReading? Right);

	private sealed record CpuCoreLoadGraphItem(System.Windows.Shapes.Rectangle Bar, TextBlock Label);

	private sealed class SessionStat
	{
		private double _sum;

		public double Min { get; private set; }

		public double Max { get; private set; }

		public double Average
		{
			get
			{
				if (Count <= 0)
				{
					return 0.0;
				}
				return _sum / (double)Count;
			}
		}

		public int Count { get; private set; }

		public bool HasValue => Count > 0;

		public void Update(double? value)
		{
			if (value.HasValue && value.GetValueOrDefault() > 0.0 && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
			{
				if (Count == 0)
				{
					Min = value.Value;
					Max = value.Value;
				}
				else
				{
					Min = Math.Min(Min, value.Value);
					Max = Math.Max(Max, value.Value);
				}
				_sum += value.Value;
				Count++;
			}
		}

		public void Reset()
		{
			Min = 0.0;
			Max = 0.0;
			_sum = 0.0;
			Count = 0;
		}
	}

	private const double BaseOverlayWidth = 330.0;

	private const double LineOverlayHorizontalPadding = 24.0;

	private const double BaseLineOverlayWindowHeight = 68.0;

	private const double CpuCoreClockLabelWidth = 68.0;

	private const double CpuCoreClockValueWidth = 72.0;

	private const double CpuCoreClockColumnWidth = 140.0;

	private const double CpuCoreClockSeparatorWidth = 18.0;

	private const double CpuCoreClockRequiredWidth = 298.0;

	private const double CpuCoreLoadGraphBarAreaHeight = 40.0;

	private const double CpuCoreLoadGraphLabelTop = 41.0;

	private const double CpuCoreLoadGraphLabelHeight = 11.0;

	private const double CpuCoreLoadGraphLabelFontSize = 7.0;

	private const double FrameTimeGraphWindowSeconds = 10.0;

	private const double FrameTimeGraphSampleIntervalMs = 33.333333;

	private const int FrameTimeGraphPointCount = 300;

	private const double FrameTimeGraphMinValidFrameTimeMs = 1.0;

	private const double FrameTimeGraphInProgressFreezeMs = 50.0;

	private const double FrameTimeGraphHoldMs = 300.0;

	private const double FrameTimeGraphNullAfterMs = 1500.0;

	private const double FreeVersionFontSize = 24.0;

	private const double LineFreeVersionFontSize = 13.0;

	private const double MinFreeVersionFontSize = 9.0;

	private const int ToggleOverlayHotKeyId = 1;

	private const int ResetStatsHotKeyId = 2;

	private const int OpenSettingsHotKeyId = 3;

	private const int ToggleOverlayModeHotKeyId = 4;

	private const int CurrentStartupWelcomeVersion = 3;

	private const uint VkF10 = 121u;

	private static readonly System.Windows.Media.Brush FreeVersionTextBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 242, 242));

	private static readonly System.Windows.Media.Brush WarmTemperatureBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 166, 77));

	private static readonly System.Windows.Media.Brush HotTemperatureBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 76, 76));

	private static readonly string[] FreeVersionPhrases = new string[20]
	{
		"Ахаха, бесплатная версия", "СЛЫШ, КУПИ", "Бесплатно, но с характером", "Купи — и это исчезнет", "Тут могла быть тишина", "Реклама? Нет, мотивация", "Версия для сильных духом", "Я бесплатный, не осуждай", "Поддержи разраба, брат", "Пока бесплатно — терпим",
		"Купи лицензию, не жмоться", "Ремонтяш хочет кушать", "Премиум рядом, он ждёт", "Это не баг, это free edition", "Лицензия плачет где-то рядом", "Бесплатная версия смотрит на тебя", "Ну купи, ну пожалуйста", "Разраб тоже любит дошик", "Пока ты не купил - я тут", "Free mode: боль и унижение"
	};

	private readonly EtwFpsMonitor _fpsMonitor = new EtwFpsMonitor();

	private readonly SettingsService _settingsService = new SettingsService();

	private readonly UpdateService _updateService = new UpdateService();

	private readonly LicenseService _licenseService;

	private readonly DispatcherTimer _fpsTextTimer;

	private readonly DispatcherTimer _hardwareTimer;

	private readonly DispatcherTimer _hardwareTextTimer;

	private readonly DispatcherTimer _positionTimer;

	private readonly DispatcherTimer _topmostTimer;

	private readonly NotifyIcon _trayIcon;

	private readonly List<CpuCoreLoadGraphItem> _cpuCoreLoadBars = new List<CpuCoreLoadGraphItem>();

	private readonly List<CpuCoreClockRow> _cpuCoreClockRows = new List<CpuCoreClockRow>();

	private HardwareSnapshot _latestHardware = new HardwareSnapshot();

	private FpsStats _latestFps = FpsStats.Empty;

	private readonly SessionStat _gpuTemperatureStats = new SessionStat();

	private readonly SessionStat _cpuTemperatureStats = new SessionStat();

	private readonly SessionStat _vramTemperatureStats = new SessionStat();

	private readonly SessionStat _hotspotTemperatureStats = new SessionStat();

	private readonly SessionStat _gpuVoltageStats = new SessionStat();

	private readonly SessionStat _gpuPowerStats = new SessionStat();

	private readonly SessionStat _cpuPowerStats = new SessionStat();

	private readonly Dictionary<string, SessionStat> _ramTemperatureStats = new Dictionary<string, SessionStat>(StringComparer.OrdinalIgnoreCase);

	private readonly List<TextBlock> _ramTemperatureLabelRows = new List<TextBlock>();

	private readonly List<TextBlock> _ramTemperatureValueRows = new List<TextBlock>();

	private readonly List<TextBlock> _ramTemperatureStatsLabelRows = new List<TextBlock>();

	private readonly List<TextBlock> _ramTemperatureStatsValueRows = new List<TextBlock>();

	private string _ramTemperatureRowsSignature = string.Empty;

	private string _ramTemperatureStatsRowsSignature = string.Empty;

	private uint? _lastStatsProcessId;

	private HardwareMonitorService? _hardwareMonitor;

	private OverlaySettings _settings;

	private LicenseState _licenseState;

	private SettingsWindow? _settingsWindow;

	private bool _hardwareUpdateInProgress;

	private bool _hotKeysRegistered;

	private bool _positionUpdateInProgress;

	private bool _isPositionInitialized;

	private bool _pawnIoWarningShown;

	private double _lineOverlayContentWidth;

	private long _lastLineOverlayUiTickTimestamp;

	private int _lineOverlayUiTickCount;

	private int _hardwareSnapshotVersion;

	private int _lastDisplayedHardwareSnapshotVersion;

	private int _hardwareUiDiagnosticsTick;

	private long _lastHardwareUiDiagnosticsTimestamp;

	private double _requiredCpuCoreGraphWidth;

	private double _requiredCpuCoreClockWidth;

	private FontRenderInfo _overlayFont = new FontRenderInfo(new System.Windows.Media.FontFamily("Segoe UI"), 1.0);

	private string? _lastCpuCoreGraphLogSignature;

	private string? _lastCpuOverlayVisibilityLogSignature;

	private string? _lastFrameTimeGraphLogSignature;

	private readonly string _freeVersionPhrase = FreeVersionPhrases[Random.Shared.Next(FreeVersionPhrases.Length)];

	private TimeSpan _lastFrameTimeGraphRenderTime;

	private readonly FrameTimeGraphPoint[] _frameTimeGraphPoints = new FrameTimeGraphPoint[300];

	private int _frameTimeGraphWriteIndex;

	private int _frameTimeGraphCount;

	private double _frameTimeGraphLastSampleEndSeconds;

	private double _frameTimeGraphLastValidValueTimeSeconds;

	private double? _frameTimeGraphLastValidValue;

	private Rect _lastAnchorArea = Rect.Empty;

	public string? LastUpdateStatusText { get; private set; }

	public OverlaySettings CurrentSettings => _settings.Clone();

	public bool IsPremium => _licenseState.IsPremium;

	public bool IsFreeVersion => _licenseState.IsFree;

	public string FreeVersionPhrase => _freeVersionPhrase;

	internal LicenseService LicenseService => _licenseService;

	internal MainWindow(LicenseService licenseService)
	{
		InitializeComponent();
		FreeVersionText.Text = _freeVersionPhrase;
		LineFreeVersionText.Text = _freeVersionPhrase;
		_licenseService = licenseService;
		_licenseState = _licenseService.CheckLocalLicense();
		_settings = _settingsService.Load();
		FrameTimeGraphCanvas.GraphBackground = System.Windows.Media.Brushes.Black;
		base.ShowInTaskbar = false;
		_trayIcon = CreateTrayIcon();
		_fpsTextTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(250L)
		};
		_fpsTextTimer.Tick += delegate
		{
			UpdateFpsText();
		};
		_fpsTextTimer.Start();
		_hardwareTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(250L)
		};
		_hardwareTimer.Tick += delegate
		{
			QueueHardwareUpdate();
		};
		_hardwareTextTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(250L)
		};
		_hardwareTextTimer.Tick += delegate
		{
			UpdateHardwareText();
		};
		CompositionTarget.Rendering += OnFrameTimeGraphRendering;
		_positionTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(250L)
		};
		_positionTimer.Tick += delegate
		{
			UpdateOverlayWindowSizeAndPosition(updatePosition: true);
		};
		_positionTimer.Start();
		_topmostTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(1000L)
		};
		_topmostTimer.Tick += delegate
		{
			ForceTopmost();
		};
		_topmostTimer.Start();
		base.Loaded += async delegate
		{
			ApplySettings(_settings);
			UpdateLicenseUiState();
			MakeWindowClickThrough();
			ForceTopmost();
			UpdateFpsText();
			UpdateHardwareText();
			DrawHwInfoGraph();
			ShowPawnIoManualInstallWarningIfNeeded(await Task.Run((Func<PawnIoInstallResult>)PawnIoInstaller.EnsureInstalled));
			_hardwareMonitor = new HardwareMonitorService
			{
				PollIntervalMs = _settings.Fps.UpdateIntervalMs
			};
			_hardwareTimer.Start();
			if (!_hardwareTextTimer.IsEnabled)
			{
				_hardwareTextTimer.Start();
			}
			QueueHardwareUpdate();
			ShowStartupWelcomeIfNeeded();
			ScheduleAutoExitForDiagnosticsIfRequested();
			CheckUpdatesOnStartupAsync();
		};
	}

	private void ScheduleAutoExitForDiagnosticsIfRequested()
	{
		int autoExitSeconds = AppLaunchOptions.AutoExitSeconds;
		if (autoExitSeconds > 0)
		{
			DispatcherTimer timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(autoExitSeconds)
			};
			timer.Tick += delegate
			{
				timer.Stop();
				Close();
			};
			timer.Start();
			AppLogger.Info($"Diagnostic auto-exit scheduled in {autoExitSeconds} seconds.");
		}
	}

	private void ShowStartupWelcomeIfNeeded()
	{
		if (!_settings.StartupWelcomeShown || _settings.StartupWelcomeVersion < 3)
		{
			AppDialog.ShowStartupWelcome(this);
			_settings.StartupWelcomeShown = true;
			_settings.StartupWelcomeVersion = 3;
			_settingsService.Save(_settings);
		}
	}

	private async Task CheckUpdatesOnStartupAsync()
	{
		if (!_settings.RemindUpdatesOnStartup)
		{
			AppLogger.Info("Auto update check skipped because reminders disabled.");
			return;
		}
		AppLogger.Info("Auto update check started.");
		UpdateCheckResult result = await _updateService.CheckAsync();
		if (result.Status == UpdateCheckStatus.Unavailable)
		{
			AppLogger.Info("Update server unavailable during auto check.");
			LastUpdateStatusText = result.Message;
			return;
		}
		if (result.Status != UpdateCheckStatus.Available)
		{
			LastUpdateStatusText = result.Message;
			return;
		}
		AppLogger.Info("Update available. CurrentVersion=" + result.CurrentVersion + ", LatestVersion=" + result.ServerVersion);
		LastUpdateStatusText = "Доступна новая версия: " + result.ServerVersion + ".";
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			new UpdateAvailableDialog(this, _updateService, result, SetUpdateRemindersEnabled).Show();
		});
	}

	private void ShowPawnIoManualInstallWarningIfNeeded(PawnIoInstallResult result)
	{
		if (result.NeedsManualInstall && !_pawnIoWarningShown)
		{
			_pawnIoWarningShown = true;
			base.Dispatcher.BeginInvoke((Func<AppDialogResult>)(() => AppDialog.Show(this, "Нужна ручная установка PawnIO", result.Message + "\n\nИз-за этого температура CPU и потребление CPU могут отображаться как N/A.\n\nЧто сделать вручную:\n1. Откройте официальный сайт PawnIO: https://pawnio.eu/\n2. Скачайте PawnIO_setup.exe.\n3. Запустите установщик от имени администратора.\n4. После установки перезапустите RemMon.", AppDialogKind.Warning)));
		}
	}

	public void RefreshLicenseState()
	{
		_licenseState = _licenseService.CheckLocalLicense();
		UpdateLicenseUiState();
		ApplyAppearanceSettings();
		ApplyHotKeySettings();
		UpdateFpsText();
		UpdateHardwareText();
		_settingsWindow?.RefreshLicenseState();
	}

	private void UpdateLicenseUiState()
	{
		FreeVersionText.Visibility = ToVisibility(IsFreeVersion && !IsLineOverlayMode());
		UpdateLineFooterVisibility();
		AdjustFreeVersionTextSizing();
	}

	private void UpdateLineFooterVisibility()
	{
		bool flag = IsLineOverlayMode();
		LineBrandText.Visibility = ToVisibility(flag && _settings.Appearance.ShowBranding);
		LineFreeVersionText.Visibility = ToVisibility(flag && IsFreeVersion);
		LineOverlayFooterPanel.Visibility = ToVisibility(LineBrandText.Visibility == Visibility.Visible || LineFreeVersionText.Visibility == Visibility.Visible);
	}

	private void UpdateLineOverlayAlignment()
	{
		if (_settings.Position.Contains("Right", StringComparison.OrdinalIgnoreCase))
		{
			LineOverlayRootPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
			LineOverlayRootPanel.Margin = new Thickness(0.0, 0.0, _settings.OffsetX, 0.0);
		}
		else if (_settings.Position.Contains("Center", StringComparison.OrdinalIgnoreCase))
		{
			LineOverlayRootPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
			LineOverlayRootPanel.Margin = new Thickness(0.0);
		}
		else
		{
			LineOverlayRootPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
			LineOverlayRootPanel.Margin = new Thickness(_settings.OffsetX, 0.0, 0.0, 0.0);
		}
	}

	public string GetCurrentHardwareName(bool isGpu)
	{
		if (!isGpu)
		{
			return _latestHardware.CpuName;
		}
		return _latestHardware.GpuName;
	}

	public void ApplySettings(OverlaySettings settings)
	{
		_settings = settings.Clone();
		_settingsService.ApplyStartupRegistration(_settings.StartWithWindows);
		AppLogger.SetSensorDiagnosticsEnabled(_settings.Statistics.DiagnosticLoggingEnabled);
		AppLogger.Info($"Applied CPU settings: ShowBlock={_settings.Cpu.ShowBlock}, ShowClock={_settings.Cpu.ShowClock}, ShowEAvgClock={_settings.Cpu.ShowEfficiencyAverageClock}, ShowCoreGraph={_settings.Cpu.ShowCoreLoadGraph}, ShowCoreGraphLabels={_settings.Cpu.ShowCoreLoadGraphLabels}, ShowCoreClocks={_settings.Cpu.ShowCoreClocks}, ShowPClocks={_settings.Cpu.ShowPerformanceCoreClocks}, ShowEClocks={_settings.Cpu.ShowEfficiencyCoreClocks}");
		TimeSpan interval = TimeSpan.FromMilliseconds(_settings.Fps.UpdateIntervalMs);
		_fpsTextTimer.Interval = interval;
		_hardwareTimer.Interval = interval;
		_hardwareTextTimer.Interval = interval;
		_positionTimer.Interval = TimeSpan.FromMilliseconds(250L);
		if (_hardwareMonitor != null)
		{
			_hardwareMonitor.PollIntervalMs = _settings.Fps.UpdateIntervalMs;
		}
		bool flag = IsLineOverlayMode();
		if (flag)
		{
			_isPositionInitialized = false;
		}
		_lineOverlayContentWidth = 0.0;
		_lastLineOverlayUiTickTimestamp = 0L;
		_lineOverlayUiTickCount = 0;
		_lastDisplayedHardwareSnapshotVersion = _hardwareSnapshotVersion;
		if (_hardwareMonitor != null && !_hardwareTextTimer.IsEnabled)
		{
			_hardwareTextTimer.Start();
		}
		_fpsMonitor.ConfigureStutterDetection(_settings.Statistics.ShowStutterDetector && !flag, _settings.Statistics.ReduceStutterDetectorSensitivity);
		ApplyAppearanceSettings();
		ApplyBlockOrder();
		ApplyVisibilitySettings();
		ApplyPositionSettings();
		ApplyHotKeySettings();
		UpdateFpsText();
		UpdateHardwareText();
		UpdateOverlayWindowSizeAndPosition(updatePosition: true);
		DrawHwInfoGraph();
	}

	public void SaveAndApplySettings(OverlaySettings settings)
	{
		OverlaySettings settings2 = _settingsService.Save(settings);
		ApplySettings(settings2);
	}

	public void SaveSettingsAndRestart(OverlaySettings settings)
	{
		_settings = _settingsService.Save(settings);
		AppLogger.Info("Application restart requested by settings change.");
		string processPath = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(processPath))
		{
			throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу.");
		}
		Process.Start(new ProcessStartInfo
		{
			FileName = processPath,
			WorkingDirectory = AppContext.BaseDirectory,
			UseShellExecute = true
		});
		System.Windows.Application.Current.Shutdown();
	}

	public void SaveSettingsWindowGeometry(double width, double height, double left, double top)
	{
		_settings.SettingsWindowWidth = width;
		_settings.SettingsWindowHeight = height;
		_settings.SettingsWindowLeft = left;
		_settings.SettingsWindowTop = top;
		_settingsService.Save(_settings);
	}

	public void SetUpdateRemindersEnabled(bool enabled)
	{
		_settings.RemindUpdatesOnStartup = enabled;
		_settingsService.Save(_settings);
		if (_settingsWindow != null)
		{
			_settingsWindow.RefreshFromSettings(_settings);
		}
	}

	public void OpenSettings()
	{
		if (_settingsWindow != null)
		{
			if (!_settingsWindow.IsVisible)
			{
				_settingsWindow.Show();
			}
			if (_settingsWindow.WindowState == WindowState.Minimized)
			{
				_settingsWindow.WindowState = WindowState.Normal;
			}
			ApplyOverlayVisibility();
			_settingsWindow.Activate();
			return;
		}
		_settingsWindow = new SettingsWindow(this, _settingsService, _settings, _licenseService);
		_settingsWindow.IsVisibleChanged += delegate
		{
			ApplyOverlayVisibility();
		};
		_settingsWindow.Closed += delegate
		{
			_settingsWindow = null;
			ApplyOverlayVisibility();
		};
		_settingsWindow.Show();
		ApplyOverlayVisibility();
		_settingsWindow.Activate();
	}

	private NotifyIcon CreateTrayIcon()
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			Renderer = new TrayMenuRenderer(),
			BackColor = System.Drawing.Color.FromArgb(7, 17, 27),
			ForeColor = System.Drawing.Color.FromArgb(232, 240, 250),
			ShowImageMargin = false,
			Padding = new Padding(4, 6, 4, 6)
		};
		contextMenuStrip.Items.Add("Показать / скрыть оверлей", null, delegate
		{
			base.Dispatcher.Invoke(ToggleOverlayVisibility);
		});
		contextMenuStrip.Items.Add("Сбросить статистику", null, delegate
		{
			base.Dispatcher.Invoke(ResetSessionStats);
		});
		contextMenuStrip.Items.Add("Открыть настройки", null, delegate
		{
			base.Dispatcher.Invoke(OpenSettings);
		});
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add("Закрыть", null, delegate
		{
			base.Dispatcher.Invoke(CloseFromTray);
		});
		foreach (ToolStripItem item in contextMenuStrip.Items)
		{
			item.ForeColor = System.Drawing.Color.FromArgb(232, 240, 250);
			item.BackColor = System.Drawing.Color.FromArgb(7, 17, 27);
			item.Font = new Font("Segoe UI", 9.5f);
			item.Padding = new Padding(10, 5, 12, 5);
		}
		NotifyIcon notifyIcon = new NotifyIcon();
		notifyIcon.Icon = LoadTrayIcon();
		notifyIcon.Text = "RemMon";
		notifyIcon.ContextMenuStrip = contextMenuStrip;
		notifyIcon.Visible = true;
		notifyIcon.DoubleClick += delegate
		{
			base.Dispatcher.Invoke(OpenSettings);
		};
		return notifyIcon;
	}

	private void CloseFromTray()
	{
		AppLogger.Info("Close requested from tray.");
		CloseSettingsWindowForShutdown();
		Close();
	}

	private void CloseSettingsWindowForShutdown()
	{
		SettingsWindow settingsWindow = _settingsWindow;
		if (settingsWindow == null)
		{
			return;
		}
		try
		{
			if (settingsWindow.WindowState == WindowState.Minimized)
			{
				settingsWindow.WindowState = WindowState.Normal;
			}
			settingsWindow.Close();
		}
		catch (Exception ex)
		{
			AppLogger.Info("Settings window close during shutdown failed: " + ex.Message);
		}
		finally
		{
			_settingsWindow = null;
		}
	}

	private static Icon LoadTrayIcon()
	{
		string text = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
		if (File.Exists(text))
		{
			return new Icon(text);
		}
		try
		{
			using Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty);
			if (icon != null)
			{
				return (Icon)icon.Clone();
			}
		}
		catch
		{
		}
		return SystemIcons.Application;
	}

	private void ApplyAppearanceSettings()
	{
		System.Windows.Media.Color color = ParseColor(_settings.Appearance.BackgroundColor, System.Windows.Media.Color.FromRgb(8, 8, 8));
		byte a = (byte)Math.Clamp(_settings.Appearance.BackgroundOpacity * 255 / 100, 0, 255);
		OverlayRoot.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, color.R, color.G, color.B));
		OverlayRoot.CornerRadius = new CornerRadius(_settings.Appearance.CornerRadius);
		System.Windows.Media.Brush foreground = (IsPremium ? BrushFromHex(_settings.Appearance.TextColor, "#FFF2F2F2") : FreeVersionTextBrush);
		System.Windows.Media.Brush foreground2 = (IsPremium ? BrushFromHex(_settings.Fps.TextColor, "#FFE7E7E7") : FreeVersionTextBrush);
		System.Windows.Media.Brush foreground3 = (IsPremium ? BrushFromHex(_settings.Fps.ValueColor, "#FFFF6A00") : FreeVersionTextBrush);
		System.Windows.Media.Brush foreground4 = (IsPremium ? BrushFromHex(_settings.Gpu.LabelColor, "#FF35D463") : FreeVersionTextBrush);
		System.Windows.Media.Brush foreground5 = (IsPremium ? BrushFromHex(_settings.Cpu.LabelColor, "#FF58A6FF") : FreeVersionTextBrush);
		System.Windows.Media.Brush foreground6 = (IsPremium ? BrushFromHex(_settings.Ram.LabelColor, "#FFB58CFF") : FreeVersionTextBrush);
		System.Windows.Media.Brush foreground7 = (IsPremium ? BrushFromHex(_settings.Statistics.LabelColor, "#FFFFA64D") : FreeVersionTextBrush);
		FpsDetailsText.Foreground = foreground2;
		FpsLabelText.Foreground = foreground2;
		FpsValueText.Foreground = foreground3;
		FpsDetailsText.FontSize = 12.0;
		GameText.FontSize = 11.0;
		RenderInfoText.FontSize = 12.0;
		TimeText.Foreground = foreground2;
		BrandPanel.Visibility = ToVisibility(_settings.Appearance.ShowBranding);
		GameText.Foreground = foreground;
		RenderInfoText.Foreground = foreground;
		FreeVersionText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(204, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		LineFreeVersionText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(204, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		System.Windows.Shapes.Rectangle[] array = new System.Windows.Shapes.Rectangle[6] { TopSeparator, GpuSeparator, CpuSeparator, RamSeparator, StatsSeparator, GraphSeparator };
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Visibility = ToVisibility(_settings.Appearance.ShowSeparators);
		}
		GpuTitleText.Foreground = (IsPremium ? BrushFromHex(_settings.Gpu.TitleColor, "#FF35D463") : FreeVersionTextBrush);
		CpuTitleText.Foreground = (IsPremium ? BrushFromHex(_settings.Cpu.TitleColor, "#FF58A6FF") : FreeVersionTextBrush);
		RamTitleText.Foreground = (IsPremium ? BrushFromHex(_settings.Ram.TitleColor, "#FFB58CFF") : FreeVersionTextBrush);
		StatsTitleText.Foreground = (IsPremium ? BrushFromHex(_settings.Statistics.TitleColor, "#FFFFA64D") : FreeVersionTextBrush);
		TextBlock[] array2 = new TextBlock[11]
		{
			GpuLoadLabel, GpuTempLabel, GpuHotspotTempLabel, GpuVramTempLabel, GpuClockLabel, GpuMemoryClockLabel, GpuVoltageLabel, GpuPowerLabel, GpuFanRpmLabel, GpuFanPercentLabel,
			VramLabel
		};
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].Foreground = foreground4;
		}
		array2 = new TextBlock[5] { CpuLoadLabel, CpuTempLabel, CpuClockLabel, CpuEfficiencyClockLabel, CpuPowerLabel };
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].Foreground = foreground5;
		}
		array2 = new TextBlock[3] { RamUsedLabel, RamLoadLabel, RamSpeedLabel };
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].Foreground = foreground6;
		}
		array2 = new TextBlock[10] { StatsGpuMinMaxLabel, StatsCpuMinMaxLabel, StatsVramMinMaxLabel, StatsHotspotMinMaxLabel, StatsGpuVoltageMinMaxLabel, StatsGpuPowerMinMaxLabel, StatsCpuPowerMinMaxLabel, StatsStutterStateLabel, StatsStutterCountLabel, StatsLastStutterLabel };
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].Foreground = foreground7;
		}
		array2 = new TextBlock[8] { GpuLoadText, GpuClockText, GpuMemoryClockText, GpuVoltageText, GpuPowerText, GpuFanRpmText, GpuFanPercentText, VramText };
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].Foreground = (IsPremium ? BrushFromHex(_settings.Gpu.ValueColor, "#FFF2F2F2") : FreeVersionTextBrush);
		}
		array2 = new TextBlock[4] { CpuLoadText, CpuClockText, CpuEfficiencyClockText, CpuPowerText };
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].Foreground = (IsPremium ? BrushFromHex(_settings.Cpu.ValueColor, "#FFF2F2F2") : FreeVersionTextBrush);
		}
		array2 = new TextBlock[3] { RamUsedText, RamLoadText, RamSpeedText };
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].Foreground = (IsPremium ? BrushFromHex(_settings.Ram.ValueColor, "#FFF2F2F2") : FreeVersionTextBrush);
		}
		array2 = new TextBlock[10] { StatsGpuMinMaxText, StatsCpuMinMaxText, StatsVramMinMaxText, StatsHotspotMinMaxText, StatsGpuVoltageMinMaxText, StatsGpuPowerMinMaxText, StatsCpuPowerMinMaxText, StatsStutterStateText, StatsStutterCountText, StatsLastStutterText };
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].Foreground = (IsPremium ? BrushFromHex(_settings.Statistics.ValueColor, "#FFF2F2F2") : FreeVersionTextBrush);
		}
		FrameTimeGraphHost.Height = _settings.FrameTimeGraph.Height;
		FrameTimeGraphCanvas.GraphBackground = BrushFromHex(_settings.FrameTimeGraph.BackgroundColor, "#FF000000");
		FrameTimeGraphCanvas.LineBrush = BrushFromHex(_settings.FrameTimeGraph.Color, "#FFFF0000");
		System.Windows.Media.Color color2 = ParseColor(_settings.FrameTimeGraph.Color, Colors.Red);
		byte a2 = (byte)Math.Clamp(_settings.FrameTimeGraph.FillOpacity * 255 / 100, 0, 255);
		FrameTimeGraphCanvas.FillBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a2, color2.R, color2.G, color2.B));
		FrameTimeGraphCanvas.MaxFrameTimeMs = Math.Max(1.0, _settings.FrameTimeGraph.MaxMs);
		FrameTimeGraphLabel.Foreground = (IsPremium ? BrushFromHex(_settings.FrameTimeGraph.Color, "#FFFF0000") : FreeVersionTextBrush);
		foreach (CpuCoreLoadGraphItem cpuCoreLoadBar in _cpuCoreLoadBars)
		{
			cpuCoreLoadBar.Label.Foreground = BrushFromHex(_settings.Cpu.LabelColor, "#FF58A6FF");
		}
		_overlayFont = FontLibraryService.ResolveFontInfo(GetEffectiveOverlayFontId());
		OverlayFontApplicator.Apply(OverlayRoot, _overlayFont, _settings.Appearance.TextScale);
		AdjustFreeVersionTextSizing();
		OverlayRoot.LayoutTransform = Transform.Identity;
	}

	private string GetEffectiveOverlayFontId()
	{
		if (!IsPremium)
		{
			return "system:Segoe UI";
		}
		return _settings.Appearance.FontId;
	}

	private void ApplyVisibilitySettings()
	{
		bool flag = IsLineOverlayMode();
		OverlayStack.Visibility = ToVisibility(!flag);
		LineOverlayRootPanel.Visibility = ToVisibility(flag);
		UpdateLineFooterVisibility();
		UpdateLicenseUiState();
		if (flag)
		{
			FrameTimeGraphLabel.Visibility = Visibility.Collapsed;
			return;
		}
		FpsBlockPanel.Visibility = ToVisibility(_settings.Sections.Fps);
		GpuBlockPanel.Visibility = ToVisibility(_settings.Sections.Gpu && _settings.Gpu.ShowBlock);
		CpuBlockPanel.Visibility = ToVisibility(_settings.Sections.Cpu && _settings.Cpu.ShowBlock);
		RamBlockPanel.Visibility = ToVisibility(_settings.Sections.Ram && _settings.Ram.ShowBlock);
		StatsBlockPanel.Visibility = ToVisibility(_settings.Sections.Statistics && _settings.Statistics.ShowBlock);
		FrameTimeGraphPanel.Visibility = ToVisibility(_settings.Sections.FrameTimeGraph && _settings.FrameTimeGraph.ShowGraph);
		bool flag2 = ShouldShowFpsMetrics(_latestFps);
		FrameTimeGraphLabel.Visibility = ToVisibility(_settings.FrameTimeGraph.ShowMsLabel && flag2);
		SetRowVisibility(GpuLoadLabel, GpuLoadText, _settings.Gpu.ShowLoad);
		SetRowVisibility(GpuTempLabel, GpuTempText, _settings.Gpu.ShowTemperature);
		SetRowVisibility(GpuHotspotTempLabel, GpuHotspotTempText, _settings.Gpu.ShowHotspotTemperature);
		SetRowVisibility(GpuVramTempLabel, GpuVramTempText, _settings.Gpu.ShowVramTemperature);
		SetRowVisibility(GpuClockLabel, GpuClockText, _settings.Gpu.ShowClock);
		SetRowVisibility(GpuMemoryClockLabel, GpuMemoryClockText, _settings.Gpu.ShowMemoryClock);
		SetRowVisibility(GpuVoltageLabel, GpuVoltageText, _settings.Gpu.ShowVoltage);
		SetRowVisibility(GpuPowerLabel, GpuPowerText, _settings.Gpu.ShowPower);
		SetRowVisibility(GpuFanRpmLabel, GpuFanRpmText, _settings.Gpu.ShowFanRpm);
		SetRowVisibility(GpuFanPercentLabel, GpuFanPercentText, _settings.Gpu.ShowFanPercent);
		SetRowVisibility(VramLabel, VramText, _settings.Gpu.ShowVram);
		SetRowVisibility(CpuLoadLabel, CpuLoadText, _settings.Cpu.ShowLoad);
		SetRowVisibility(CpuTempLabel, CpuTempText, _settings.Cpu.ShowTemperature);
		SetRowVisibility(CpuClockLabel, CpuClockText, _settings.Cpu.ShowClock);
		SetRowVisibility(CpuEfficiencyClockLabel, CpuEfficiencyClockText, _settings.Cpu.ShowClock && _settings.Cpu.ShowEfficiencyAverageClock && IsHybridCpu(_latestHardware));
		SetRowVisibility(CpuPowerLabel, CpuPowerText, _settings.Cpu.ShowPower);
		CpuCoreLoadGraphHost.Visibility = ToVisibility(_settings.Cpu.ShowCoreLoadGraph);
		bool flag3 = HasVisibleCpuCoreClocks(_latestHardware.CpuCoreClocks);
		CpuCoreClockRowsPanel.Visibility = ToVisibility(_settings.Cpu.ShowCoreClocks && flag3);
		CpuRowsBottomSpacer.Visibility = ToVisibility(!_settings.Cpu.ShowCoreLoadGraph && !(_settings.Cpu.ShowCoreClocks && flag3));
		SetRowVisibility(RamUsedLabel, RamUsedText, _settings.Ram.ShowUsed);
		SetRowVisibility(RamLoadLabel, RamLoadText, _settings.Ram.ShowLoad);
		SetRowVisibility(RamSpeedLabel, RamSpeedText, _settings.Ram.ShowSpeed);
		RamTemperatureLabelsPanel.Visibility = ToVisibility(_settings.Ram.ShowTemperatures && _latestHardware.RamModuleTemperatures.Count > 0);
		RamTemperatureValuesPanel.Visibility = RamTemperatureLabelsPanel.Visibility;
		SetRowVisibility(StatsGpuMinMaxLabel, StatsGpuMinMaxText, _settings.Statistics.ShowGpuMinMax);
		SetRowVisibility(StatsCpuMinMaxLabel, StatsCpuMinMaxText, _settings.Statistics.ShowCpuMinMax);
		SetRowVisibility(StatsVramMinMaxLabel, StatsVramMinMaxText, _settings.Statistics.ShowVramMinMax);
		SetRowVisibility(StatsHotspotMinMaxLabel, StatsHotspotMinMaxText, _settings.Statistics.ShowHotspotMinMax);
		SetRowVisibility(StatsGpuVoltageMinMaxLabel, StatsGpuVoltageMinMaxText, _settings.Statistics.ShowGpuVoltageMinMax);
		SetRowVisibility(StatsGpuPowerMinMaxLabel, StatsGpuPowerMinMaxText, _settings.Statistics.ShowGpuPowerMinMax);
		SetRowVisibility(StatsCpuPowerMinMaxLabel, StatsCpuPowerMinMaxText, _settings.Statistics.ShowCpuPowerMinMax);
		StatsRamTemperatureLabelsPanel.Visibility = ToVisibility(_settings.Statistics.ShowRamTemperatureStats && _latestHardware.RamModuleTemperatures.Count > 0);
		StatsRamTemperatureValuesPanel.Visibility = StatsRamTemperatureLabelsPanel.Visibility;
		SetRowVisibility(StatsStutterStateLabel, StatsStutterStateText, _settings.Statistics.ShowStutterDetector);
		SetRowVisibility(StatsStutterCountLabel, StatsStutterCountText, _settings.Statistics.ShowStutterDetector);
		SetRowVisibility(StatsLastStutterLabel, StatsLastStutterText, _settings.Statistics.ShowStutterDetector);
		UpdateBlockSeparators();
	}

	private void UpdateBlockSeparators()
	{
		Dictionary<UIElement, System.Windows.Shapes.Rectangle> obj = new Dictionary<UIElement, System.Windows.Shapes.Rectangle>
		{
			[FpsBlockPanel] = TopSeparator,
			[GpuBlockPanel] = GpuSeparator,
			[CpuBlockPanel] = CpuSeparator,
			[RamBlockPanel] = RamSeparator,
			[StatsBlockPanel] = StatsSeparator
		};
		UIElement key = FrameTimeGraphPanel;
		obj[key] = GraphSeparator;
		UIElement uIElement = OverlayStack.Children.OfType<UIElement>().LastOrDefault((UIElement block) => block.Visibility == Visibility.Visible);
		foreach (KeyValuePair<UIElement, System.Windows.Shapes.Rectangle> item in obj)
		{
			item.Deconstruct(out key, out var value);
			UIElement uIElement2 = key;
			value.Visibility = ToVisibility(_settings.Appearance.ShowSeparators && uIElement2.Visibility == Visibility.Visible && uIElement2 != uIElement);
		}
	}

	private void ApplyBlockOrder()
	{
		Dictionary<string, UIElement> dictionary = new Dictionary<string, UIElement>(StringComparer.OrdinalIgnoreCase)
		{
			["Fps"] = FpsBlockPanel,
			["Gpu"] = GpuBlockPanel,
			["Cpu"] = CpuBlockPanel,
			["Ram"] = RamBlockPanel,
			["Statistics"] = StatsBlockPanel,
			["FrameTimeGraph"] = FrameTimeGraphPanel
		};
		foreach (UIElement value2 in dictionary.Values)
		{
			OverlayStack.Children.Remove(value2);
		}
		if (OverlayStack.Children.Contains(BrandPanel))
		{
			OverlayStack.Children.Remove(BrandPanel);
		}
		OverlayStack.Children.Insert(0, BrandPanel);
		foreach (string item in _settings.BlockOrder)
		{
			if (dictionary.TryGetValue(item, out var value) && !OverlayStack.Children.Contains(value))
			{
				OverlayStack.Children.Add(value);
			}
		}
		foreach (UIElement value3 in dictionary.Values)
		{
			if (!OverlayStack.Children.Contains(value3))
			{
				OverlayStack.Children.Add(value3);
			}
		}
	}

	private void ApplyPositionSettings()
	{
		UpdateOverlayWindowSizeAndPosition(updatePosition: true);
		ApplyOverlayVisibility();
	}

	private void ApplyOverlayVisibility()
	{
		base.Visibility = ((!ShouldOverlayBeVisible()) ? Visibility.Hidden : Visibility.Visible);
	}

	private bool ShouldOverlayBeVisible()
	{
		if (!_settings.OverlayEnabled)
		{
			return false;
		}
		if (_settings.GameOnlyMode)
		{
			return HasActiveGameFps(_latestFps);
		}
		return true;
	}

	private static bool HasActiveGameFps(FpsStats fps)
	{
		if (fps.ProcessId.HasValue)
		{
			double? currentFps = fps.CurrentFps;
			if (currentFps.HasValue)
			{
				return currentFps.GetValueOrDefault() > 0.0;
			}
			return false;
		}
		return false;
	}

	private void UpdateOverlayWindowSizeAndPosition(bool updatePosition = false, bool allowAnchorRefresh = true)
	{
		if (_positionUpdateInProgress)
		{
			return;
		}
		_positionUpdateInProgress = true;
		try
		{
			if (IsLineOverlayMode())
			{
				OverlayRoot.Width = double.NaN;
				UpdateLineOverlayWindowSizeAndPosition(updatePosition);
				return;
			}
			double overlayContentWidth = GetOverlayContentWidth();
			OverlayRoot.Width = overlayContentWidth;
			AdjustFreeVersionTextSizing(overlayContentWidth - OverlayRoot.Padding.Left - OverlayRoot.Padding.Right);
			OverlayRoot.Measure(new System.Windows.Size(overlayContentWidth, double.PositiveInfinity));
			System.Windows.Size desiredSize = OverlayRoot.DesiredSize;
			double num = Math.Ceiling(Math.Max(overlayContentWidth, desiredSize.Width));
			double num2 = Math.Ceiling(Math.Max(1.0, desiredSize.Height));
			bool flag = Math.Abs(base.Width - num) > 0.5 || Math.Abs(base.Height - num2) > 0.5;
			if (Math.Abs(base.Width - num) > 0.5)
			{
				base.Width = num;
			}
			if (Math.Abs(base.Height - num2) > 0.5)
			{
				base.Height = num2;
			}
			if (updatePosition || !_isPositionInitialized || (allowAnchorRefresh && flag && NeedsSizeAwareAnchor()))
			{
				Rect anchorArea = GetAnchorArea();
				bool flag2 = !_lastAnchorArea.Equals(anchorArea);
				if (updatePosition || !_isPositionInitialized || flag2 || (flag && NeedsSizeAwareAnchor()))
				{
					ApplyWindowAnchor(anchorArea);
				}
			}
		}
		finally
		{
			_positionUpdateInProgress = false;
		}
	}

	private double GetOverlayContentWidth()
	{
		double num = Math.Clamp(_settings.OverlayWidthPercent, 50.0, 100.0) / 100.0;
		double val = ((_requiredCpuCoreGraphWidth > 0.0) ? (_requiredCpuCoreGraphWidth + 24.0) : 0.0);
		double val2 = ((_requiredCpuCoreClockWidth > 0.0) ? (_requiredCpuCoreClockWidth + 24.0) : 0.0);
		return Math.Max(330.0 * num, Math.Max(val, val2));
	}

	private bool IsLineOverlayMode()
	{
		return _settings.OverlayDisplayMode.Equals("Line", StringComparison.OrdinalIgnoreCase);
	}

	private void UpdateLineOverlayText()
	{
		if (IsLineOverlayMode())
		{
			IReadOnlyList<OverlayLineGroup> groups = OverlayLineFormatter.FormatGroups(_settings, _latestFps, _latestHardware);
			_lineOverlayContentWidth = CalculateLineOverlayMinimumWidth(groups);
			RebuildLineOverlayPanel(groups);
			UpdateLineFooterVisibility();
			LogLineOverlayTick();
		}
	}

	private void LogLineOverlayTick()
	{
		if (AppLogger.SensorDiagnosticsEnabled)
		{
			long timestamp = Stopwatch.GetTimestamp();
			double value = ((_lastLineOverlayUiTickTimestamp == 0L) ? 0.0 : ((double)(timestamp - _lastLineOverlayUiTickTimestamp) * 1000.0 / (double)Stopwatch.Frequency));
			_lastLineOverlayUiTickTimestamp = timestamp;
			_lineOverlayUiTickCount++;
			bool value2 = _lastDisplayedHardwareSnapshotVersion != _hardwareSnapshotVersion;
			_lastDisplayedHardwareSnapshotVersion = _hardwareSnapshotVersion;
			AppLogger.SensorDiagnostics($"Line overlay UI tick: Tick={_lineOverlayUiTickCount}; Interval={value:0.0} ms; HardwareVersion={_hardwareSnapshotVersion}; NewHardware={value2}; Width={_lineOverlayContentWidth:0.0}; Window={base.Width:0.0}x{base.Height:0.0}");
		}
	}

	private void RebuildLineOverlayPanel(IReadOnlyList<OverlayLineGroup> groups)
	{
		LineOverlayPanel.Children.Clear();
		for (int i = 0; i < groups.Count; i++)
		{
			if (i > 0)
			{
				LineOverlayPanel.Children.Add(CreateLineSeparator());
			}
			LineOverlayPanel.Children.Add(CreateLineGroup(groups[i]));
		}
		OverlayFontApplicator.Apply(LineOverlayPanel, _overlayFont, _settings.Appearance.TextScale);
	}

	private TextBlock CreateLineSeparator()
	{
		return new TextBlock
		{
			Text = "|",
			Foreground = (IsPremium ? BrushFromHex(_settings.Appearance.TextColor, "#FFF2F2F2") : FreeVersionTextBrush),
			FontFamily = _overlayFont.Family,
			FontWeight = FontWeights.SemiBold,
			FontSize = 13.0,
			Margin = new Thickness(8.0, 0.0, 8.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
	}

	private StackPanel CreateLineGroup(OverlayLineGroup group)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (!string.IsNullOrWhiteSpace(group.Title))
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = group.Title,
				Foreground = (IsPremium ? BrushFromHex(group.TitleColor, _settings.Appearance.TextColor) : FreeVersionTextBrush),
				FontFamily = _overlayFont.Family,
				FontWeight = FontWeights.Bold,
				FontSize = 13.0,
				Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		for (int i = 0; i < group.Items.Count; i++)
		{
			stackPanel.Children.Add(CreateLineItem(group.Items[i]));
		}
		return stackPanel;
	}

	private StackPanel CreateLineItem(OverlayLineItem item)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (!string.IsNullOrWhiteSpace(item.Label))
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = item.Label,
				Foreground = (IsPremium ? BrushFromHex(item.LabelColor, _settings.Appearance.TextColor) : FreeVersionTextBrush),
				FontFamily = _overlayFont.Family,
				FontWeight = FontWeights.SemiBold,
				FontSize = 13.0,
				Margin = new Thickness(0.0, 0.0, 4.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		TextBlock textBlock = new TextBlock
		{
			Text = item.Value,
			Width = item.ValueWidth,
			MinWidth = item.ValueWidth,
			Foreground = (IsPremium ? BrushFromHex(item.ValueColor, _settings.Appearance.TextColor) : FreeVersionTextBrush),
			FontFamily = _overlayFont.Family,
			FontWeight = FontWeights.SemiBold,
			FontSize = 13.0,
			TextAlignment = ((item.Alignment != OverlayLineValueAlignment.Left) ? TextAlignment.Right : TextAlignment.Left),
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(item.LeftMargin, 0.0, item.RightMargin, 0.0)
		};
		textBlock.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
		textBlock.SetValue(Typography.NumeralStyleProperty, FontNumeralStyle.Lining);
		stackPanel.Children.Add(textBlock);
		return stackPanel;
	}

	private static double CalculateLineOverlayMinimumWidth(IReadOnlyList<OverlayLineGroup> groups)
	{
		double num = 0.0;
		for (int i = 0; i < groups.Count; i++)
		{
			if (i > 0)
			{
				num += 17.0;
			}
			OverlayLineGroup overlayLineGroup = groups[i];
			if (!string.IsNullOrWhiteSpace(overlayLineGroup.Title))
			{
				num += 32.0;
			}
			for (int j = 0; j < overlayLineGroup.Items.Count; j++)
			{
				OverlayLineItem overlayLineItem = overlayLineGroup.Items[j];
				if (!string.IsNullOrWhiteSpace(overlayLineItem.Label))
				{
					num += Math.Max(18.0, (double)overlayLineItem.Label.Length * 7.6) + 4.0;
				}
				num += overlayLineItem.ValueWidth;
			}
		}
		return Math.Ceiling(num);
	}

	private void UpdateLineOverlayWindowSizeAndPosition(bool updatePosition)
	{
		Rect rect = GetAnchorArea();
		if (!IsUsableArea(rect))
		{
			rect = GetDefaultFallbackArea();
		}
		UpdateLineOverlayAlignment();
		double num = Math.Max(120.0, rect.Width);
		double lineOverlayWindowHeight = GetLineOverlayWindowHeight();
		bool flag = Math.Abs(base.Width - num) > 0.5 || Math.Abs(base.Height - lineOverlayWindowHeight) > 0.5;
		if (Math.Abs(base.Width - num) > 0.5)
		{
			base.Width = num;
		}
		if (Math.Abs(base.Height - lineOverlayWindowHeight) > 0.5)
		{
			base.Height = lineOverlayWindowHeight;
		}
		double num2 = Math.Max(120.0, rect.Width - 20.0);
		LineOverlayPanel.MaxWidth = Math.Max(80.0, num2 - 24.0);
		double val = ((_lineOverlayContentWidth > 0.0) ? _lineOverlayContentWidth : CalculateLineOverlayMinimumWidth(OverlayLineFormatter.FormatGroups(_settings, _latestFps, _latestHardware)));
		LineOverlayPanel.Width = Math.Min(LineOverlayPanel.MaxWidth, Math.Max(1.0, val));
		AdjustFreeVersionTextSizing(num2);
		bool flag2 = !_lastAnchorArea.Equals(rect);
		if (!_isPositionInitialized || flag2 || flag)
		{
			ApplyWindowAnchor(rect);
		}
	}

	private double GetLineOverlayWindowHeight()
	{
		double num = Math.Clamp(_settings.Appearance.TextScale, 0.5, 3.0);
		return Math.Ceiling(68.0 * num);
	}

	private void AdjustFreeVersionTextSizing(double availableWidth = 0.0)
	{
		double availableWidth2 = ((availableWidth > 0.0) ? availableWidth : Math.Max(80.0, ((OverlayRoot.ActualWidth > 0.0) ? OverlayRoot.ActualWidth : GetOverlayContentWidth()) - OverlayRoot.Padding.Left - OverlayRoot.Padding.Right));
		ApplyFittedTextSize(FreeVersionText, 24.0, availableWidth2);
		double num = Math.Max(80.0, availableWidth);
		if (LineBrandText.Visibility == Visibility.Visible)
		{
			num = Math.Max(80.0, num - LineBrandText.ActualWidth - LineFreeVersionText.Margin.Left - 24.0);
		}
		ApplyFittedTextSize(LineFreeVersionText, 13.0, num);
	}

	private void ApplyFittedTextSize(TextBlock textBlock, double baseFontSize, double availableWidth)
	{
		if (!string.IsNullOrWhiteSpace(textBlock.Text))
		{
			double num = Math.Clamp(_settings.Appearance.TextScale, 0.5, 3.0);
			double num2 = Math.Round(baseFontSize * _overlayFont.VisualScale * num, 2);
			double num3 = Math.Max(40.0, availableWidth);
			textBlock.MinWidth = 0.0;
			textBlock.Width = num3;
			textBlock.MaxWidth = num3;
			textBlock.FontSize = num2;
			double num4 = MeasureTextWidth(textBlock, num2);
			if (!(num4 <= num3))
			{
				double fontSize = Math.Max(9.0, Math.Floor(num2 * num3 / Math.Max(1.0, num4)));
				textBlock.FontSize = fontSize;
			}
		}
	}

	private double MeasureTextWidth(TextBlock textBlock, double fontSize)
	{
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		return new FormattedText(textBlock.Text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch), fontSize, System.Windows.Media.Brushes.White, dpi.PixelsPerDip).WidthIncludingTrailingWhitespace;
	}

	private bool NeedsSizeAwareAnchor()
	{
		if (!_settings.Position.Contains("Right", StringComparison.OrdinalIgnoreCase) && !_settings.Position.Contains("Bottom", StringComparison.OrdinalIgnoreCase) && !_settings.Position.Contains("Center", StringComparison.OrdinalIgnoreCase))
		{
			return _settings.Position.StartsWith("Middle", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private void ApplyWindowAnchor(Rect anchorArea)
	{
		if (!IsUsableArea(anchorArea))
		{
			anchorArea = GetDefaultFallbackArea();
		}
		double num = anchorArea.Left + (double)_settings.OffsetX;
		double num2 = anchorArea.Top + (double)_settings.OffsetY;
		if (IsLineOverlayMode())
		{
			num = anchorArea.Left;
		}
		else
		{
			if (_settings.Position.Contains("Right", StringComparison.OrdinalIgnoreCase))
			{
				num = Math.Max(anchorArea.Left, anchorArea.Right - base.Width - (double)_settings.OffsetX);
			}
			if (_settings.Position.Contains("Center", StringComparison.OrdinalIgnoreCase))
			{
				num = anchorArea.Left + (anchorArea.Width - base.Width) / 2.0;
			}
		}
		if (_settings.Position.Contains("Bottom", StringComparison.OrdinalIgnoreCase))
		{
			num2 = Math.Max(anchorArea.Top, anchorArea.Bottom - base.Height - (double)_settings.OffsetY);
		}
		if (_settings.Position == "MiddleLeft" || _settings.Position == "MiddleCenter" || _settings.Position == "MiddleRight")
		{
			num2 = anchorArea.Top + (anchorArea.Height - base.Height) / 2.0;
		}
		if (Math.Abs(base.Left - num) > 0.5)
		{
			base.Left = num;
		}
		if (Math.Abs(base.Top - num2) > 0.5)
		{
			base.Top = num2;
		}
		_lastAnchorArea = anchorArea;
		_isPositionInitialized = true;
	}

	private Rect GetAnchorArea()
	{
		string anchorTarget = _settings.AnchorTarget;
		if (!(anchorTarget == "ActiveWindow"))
		{
			if (anchorTarget == "ActiveMonitor")
			{
				return GetActiveMonitorArea() ?? SystemParameters.WorkArea;
			}
			return SystemParameters.WorkArea;
		}
		return GetActiveWindowArea() ?? GetDefaultFallbackArea();
	}

	private Rect? GetActiveMonitorArea()
	{
		nint foregroundWindow = NativeMethods.GetForegroundWindow();
		if (foregroundWindow == IntPtr.Zero)
		{
			return null;
		}
		nint num = NativeMethods.MonitorFromWindow(foregroundWindow, 2u);
		if (num == IntPtr.Zero)
		{
			return null;
		}
		NativeMethods.MonitorInfo lpmi = new NativeMethods.MonitorInfo
		{
			cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>()
		};
		if (!NativeMethods.GetMonitorInfo(num, ref lpmi))
		{
			return null;
		}
		Rect rect = ToWpfRect(lpmi.rcWork);
		if (!IsUsableArea(rect))
		{
			return null;
		}
		return rect;
	}

	private Rect? GetActiveWindowArea()
	{
		nint foregroundWindow = NativeMethods.GetForegroundWindow();
		if (!IsValidActiveWindow(foregroundWindow))
		{
			return null;
		}
		if (NativeMethods.DwmGetWindowAttribute(foregroundWindow, 9, out var pvAttribute, Marshal.SizeOf<NativeMethods.Rect>()) != 0 && !NativeMethods.GetWindowRect(foregroundWindow, out pvAttribute))
		{
			return null;
		}
		Rect rect = ToWpfRect(pvAttribute);
		if (!IsUsableArea(rect) || !IntersectsAnyWorkingArea(rect))
		{
			return null;
		}
		return rect;
	}

	private bool IsValidActiveWindow(nint hwnd)
	{
		if (hwnd == IntPtr.Zero)
		{
			return false;
		}
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle != IntPtr.Zero && hwnd == handle)
		{
			return false;
		}
		if (_settingsWindow != null)
		{
			nint handle2 = new WindowInteropHelper(_settingsWindow).Handle;
			if (handle2 != IntPtr.Zero && hwnd == handle2)
			{
				return false;
			}
		}
		if (hwnd == NativeMethods.GetDesktopWindow() || hwnd == NativeMethods.GetShellWindow())
		{
			return false;
		}
		if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
		{
			return false;
		}
		bool flag;
		switch (GetWindowClassName(hwnd))
		{
		case "Progman":
		case "WorkerW":
		case "Shell_TrayWnd":
		case "Shell_SecondaryTrayWnd":
		case "DV2ControlHost":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return false;
		}
		return true;
	}

	private static string GetWindowClassName(nint hwnd)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		if (NativeMethods.GetClassName(hwnd, stringBuilder, stringBuilder.Capacity) <= 0)
		{
			return string.Empty;
		}
		return stringBuilder.ToString();
	}

	private static Rect ToWpfRect(NativeMethods.Rect rect)
	{
		return new Rect(rect.Left, rect.Top, Math.Max(0, rect.Width), Math.Max(0, rect.Height));
	}

	private static bool IsUsableArea(Rect area)
	{
		if (area.Width < 40.0 || area.Height < 40.0)
		{
			return false;
		}
		if (double.IsNaN(area.Left) || double.IsNaN(area.Top) || double.IsNaN(area.Width) || double.IsNaN(area.Height))
		{
			return false;
		}
		if (double.IsInfinity(area.Left) || double.IsInfinity(area.Top) || double.IsInfinity(area.Width) || double.IsInfinity(area.Height))
		{
			return false;
		}
		if (area.Left <= -30000.0 || area.Top <= -30000.0 || area.Right <= -30000.0 || area.Bottom <= -30000.0)
		{
			return false;
		}
		if (area.Right > SystemParameters.VirtualScreenLeft && area.Bottom > SystemParameters.VirtualScreenTop && area.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth)
		{
			return area.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
		}
		return false;
	}

	private static Rect GetDefaultFallbackArea()
	{
		if (!IsUsableArea(SystemParameters.WorkArea))
		{
			return new Rect(0.0, 0.0, Math.Max(1.0, SystemParameters.PrimaryScreenWidth), Math.Max(1.0, SystemParameters.PrimaryScreenHeight));
		}
		return SystemParameters.WorkArea;
	}

	private static bool IntersectsAnyWorkingArea(Rect area)
	{
		NativeMethods.Rect lprc = new NativeMethods.Rect
		{
			Left = (int)Math.Floor(area.Left),
			Top = (int)Math.Floor(area.Top),
			Right = (int)Math.Ceiling(area.Right),
			Bottom = (int)Math.Ceiling(area.Bottom)
		};
		nint num = NativeMethods.MonitorFromRect(ref lprc, 0u);
		if (num == IntPtr.Zero)
		{
			return false;
		}
		NativeMethods.MonitorInfo lpmi = new NativeMethods.MonitorInfo
		{
			cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>()
		};
		if (!NativeMethods.GetMonitorInfo(num, ref lpmi))
		{
			return false;
		}
		Rect rect = ToWpfRect(lpmi.rcWork);
		if (IsUsableArea(rect))
		{
			return area.IntersectsWith(rect);
		}
		return false;
	}

	private void ApplyHotKeySettings()
	{
		if (base.IsLoaded && base.IsInitialized)
		{
			UnregisterHotKeys();
			RegisterHotKeys();
		}
	}

	private void UpdateFpsText()
	{
		bool includeRenderInfo = !IsLineOverlayMode() && _settings.Sections.Fps && _settings.Fps.ShowApi;
		FpsStats stats = _fpsMonitor.GetStats(includeRenderInfo);
		ResetStatsIfGameChanged(stats);
		_latestFps = stats;
		if (IsLineOverlayMode())
		{
			ApplyOverlayVisibility();
			return;
		}
		TimeText.Text = DateTime.Now.ToString("HH:mm:ss");
		bool flag = ShouldShowFpsMetrics(stats);
		FpsValueText.Text = ((_settings.Fps.ShowFps && flag) ? stats.CurrentFpsText : string.Empty);
		Visibility visibility = ToVisibility(_settings.Sections.Fps && _settings.Fps.ShowFps && flag);
		FpsLabelText.Visibility = visibility;
		FpsValueText.Visibility = visibility;
		List<string> list = new List<string>();
		if (flag && _settings.Fps.ShowAverage)
		{
			list.Add("AVG: " + stats.AverageFpsText);
		}
		if (flag && _settings.Fps.ShowOnePercentLow)
		{
			list.Add("1% Low: " + stats.OnePercentLowText);
		}
		if (flag && _settings.Fps.ShowPointOnePercentLow)
		{
			list.Add("0.1% Low: " + stats.PointOnePercentLowText);
		}
		FpsDetailsText.Text = string.Join("   ", list);
		FrameTimeGraphLabel.Text = stats.FrameTimeText;
		GameText.Text = stats.WindowText;
		RenderInfoText.Text = stats.RenderText;
		FpsHeaderPanel.Visibility = ToVisibility(_settings.Sections.Fps && _settings.Fps.ShowFps);
		TimeText.Visibility = ToVisibility(_settings.ShowTime);
		FpsDetailsText.Visibility = ToVisibility(_settings.Sections.Fps && flag && list.Count > 0);
		FrameTimeGraphLabel.Visibility = ToVisibility(_settings.FrameTimeGraph.ShowMsLabel && flag);
		GameText.Visibility = ToVisibility(_settings.Sections.Fps && _settings.Fps.ShowGame);
		RenderInfoText.Visibility = ToVisibility(_settings.Sections.Fps && _settings.Fps.ShowApi);
		ApplyOverlayVisibility();
		UpdateOverlayWindowSizeAndPosition(updatePosition: false, allowAnchorRefresh: false);
	}

	private void UpdateRamTemperatureRows(HardwareSnapshot hardware)
	{
		if (!_settings.Ram.ShowTemperatures)
		{
			ClearRamTemperatureRows();
			return;
		}
		System.Windows.Media.Brush foreground = (IsPremium ? BrushFromHex(_settings.Ram.LabelColor, "#FFB58CFF") : FreeVersionTextBrush);
		System.Windows.Media.Brush foreground2 = (IsPremium ? BrushFromHex(_settings.Ram.ValueColor, "#FFF2F2F2") : FreeVersionTextBrush);
		string text = string.Join("|", hardware.RamModuleTemperatures.Select((RamModuleTemperature module) => module.Name));
		if (!text.Equals(_ramTemperatureRowsSignature, StringComparison.Ordinal))
		{
			ClearRamTemperatureRows();
			foreach (RamModuleTemperature ramModuleTemperature2 in hardware.RamModuleTemperatures)
			{
				TextBlock textBlock = CreateOverlayMetricText(ramModuleTemperature2.Name, foreground, bold: false, alignRight: false);
				TextBlock textBlock2 = CreateOverlayMetricText(string.Empty, foreground2, bold: true, alignRight: true);
				_ramTemperatureLabelRows.Add(textBlock);
				_ramTemperatureValueRows.Add(textBlock2);
				RamTemperatureLabelsPanel.Children.Add(textBlock);
				RamTemperatureValuesPanel.Children.Add(textBlock2);
			}
			_ramTemperatureRowsSignature = text;
		}
		for (int num = 0; num < hardware.RamModuleTemperatures.Count && num < _ramTemperatureValueRows.Count; num++)
		{
			RamModuleTemperature ramModuleTemperature = hardware.RamModuleTemperatures[num];
			UpdateOverlayMetricText(_ramTemperatureLabelRows[num], ramModuleTemperature.Name, foreground, bold: false, alignRight: false);
			UpdateOverlayMetricText(_ramTemperatureValueRows[num], FormatTemperature(ramModuleTemperature.TemperatureC), foreground2, bold: true, alignRight: true);
		}
	}

	private void UpdateRamTemperatureStatsRows(HardwareSnapshot hardware)
	{
		if (!_settings.Statistics.ShowRamTemperatureStats)
		{
			ClearRamTemperatureStatsRows();
			return;
		}
		System.Windows.Media.Brush foreground = (IsPremium ? BrushFromHex(_settings.Statistics.LabelColor, "#FFFFA64D") : FreeVersionTextBrush);
		System.Windows.Media.Brush foreground2 = (IsPremium ? BrushFromHex(_settings.Statistics.ValueColor, "#FFF2F2F2") : FreeVersionTextBrush);
		string text = string.Join("|", hardware.RamModuleTemperatures.Select((RamModuleTemperature module) => module.Name));
		if (!text.Equals(_ramTemperatureStatsRowsSignature, StringComparison.Ordinal))
		{
			ClearRamTemperatureStatsRows();
			foreach (RamModuleTemperature ramModuleTemperature2 in hardware.RamModuleTemperatures)
			{
				_ = ramModuleTemperature2;
				TextBlock textBlock = CreateOverlayMetricText(string.Empty, foreground, bold: false, alignRight: false);
				TextBlock textBlock2 = CreateOverlayMetricText(string.Empty, foreground2, bold: true, alignRight: true);
				_ramTemperatureStatsLabelRows.Add(textBlock);
				_ramTemperatureStatsValueRows.Add(textBlock2);
				StatsRamTemperatureLabelsPanel.Children.Add(textBlock);
				StatsRamTemperatureValuesPanel.Children.Add(textBlock2);
			}
			_ramTemperatureStatsRowsSignature = text;
		}
		for (int num = 0; num < hardware.RamModuleTemperatures.Count && num < _ramTemperatureStatsValueRows.Count; num++)
		{
			RamModuleTemperature ramModuleTemperature = hardware.RamModuleTemperatures[num];
			if (_ramTemperatureStats.TryGetValue(ramModuleTemperature.Name, out SessionStat value))
			{
				string text2 = FormatStatsLabel(ShortenRamModuleName(ramModuleTemperature.Name), _settings.Statistics.RamTemperatureStatsMode);
				string text3 = FormatTemperatureStats(value, _settings.Statistics.RamTemperatureStatsMode);
				UpdateOverlayMetricText(_ramTemperatureStatsLabelRows[num], text2, foreground, bold: false, alignRight: false);
				UpdateOverlayMetricText(_ramTemperatureStatsValueRows[num], text3, foreground2, bold: true, alignRight: true);
			}
		}
	}

	private void ClearRamTemperatureRows()
	{
		RamTemperatureLabelsPanel.Children.Clear();
		RamTemperatureValuesPanel.Children.Clear();
		_ramTemperatureLabelRows.Clear();
		_ramTemperatureValueRows.Clear();
		_ramTemperatureRowsSignature = string.Empty;
	}

	private void ClearRamTemperatureStatsRows()
	{
		StatsRamTemperatureLabelsPanel.Children.Clear();
		StatsRamTemperatureValuesPanel.Children.Clear();
		_ramTemperatureStatsLabelRows.Clear();
		_ramTemperatureStatsValueRows.Clear();
		_ramTemperatureStatsRowsSignature = string.Empty;
	}

	private TextBlock CreateOverlayMetricText(string text, System.Windows.Media.Brush foreground, bool bold, bool alignRight)
	{
		TextBlock textBlock = new TextBlock
		{
			Text = text,
			Foreground = foreground,
			FontFamily = _overlayFont.Family,
			FontWeight = (bold ? FontWeights.Bold : FontWeights.Normal),
			FontSize = 13.0,
			TextAlignment = (alignRight ? TextAlignment.Right : TextAlignment.Left)
		};
		if (alignRight)
		{
			textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
			textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
		}
		return textBlock;
	}

	private void UpdateOverlayMetricText(TextBlock block, string text, System.Windows.Media.Brush foreground, bool bold, bool alignRight)
	{
		block.Text = text;
		block.Foreground = foreground;
		block.FontFamily = _overlayFont.Family;
		block.FontWeight = (bold ? FontWeights.Bold : FontWeights.Normal);
		block.FontSize = 13.0;
		block.TextAlignment = (alignRight ? TextAlignment.Right : TextAlignment.Left);
	}

	private bool ShouldShowFpsMetrics(FpsStats fps)
	{
		if (_settings.Fps.HideUnavailableFpsMetrics)
		{
			double? currentFps = fps.CurrentFps;
			if (currentFps.HasValue)
			{
				return currentFps.GetValueOrDefault() > 0.0;
			}
			return false;
		}
		return true;
	}

	private void ResetStatsIfGameChanged(FpsStats fps)
	{
		if (!_settings.Statistics.ResetOnGameChange)
		{
			return;
		}
		uint? processId = fps.ProcessId;
		if (processId.HasValue)
		{
			uint valueOrDefault = processId.GetValueOrDefault();
			if (!_lastStatsProcessId.HasValue)
			{
				_lastStatsProcessId = valueOrDefault;
			}
			else if (_lastStatsProcessId != valueOrDefault)
			{
				_lastStatsProcessId = valueOrDefault;
				ResetSessionStats();
			}
		}
	}

	private void UpdateHardwareText()
	{
		long timestamp = Stopwatch.GetTimestamp();
		HardwareSnapshot latestHardware = _latestHardware;
		if (IsLineOverlayMode())
		{
			UpdateLineOverlayText();
			ApplyVisibilitySettings();
			UpdateOverlayWindowSizeAndPosition(updatePosition: false, allowAnchorRefresh: false);
			LogHardwareUiDiagnostics(timestamp, lineMode: true);
			return;
		}
		GpuTitleText.Text = GetHardwareDisplayName(_settings.Gpu, latestHardware.GpuName);
		GpuLoadText.Text = FormatPercent(latestHardware.GpuLoadPercent);
		GpuLoadText.Foreground = GetLoadBrush(latestHardware.GpuLoadPercent, _settings.Gpu, isGpu: true);
		GpuTempText.Text = FormatTemperature(latestHardware.GpuTemperatureC);
		GpuTempText.Foreground = GetTemperatureBrush(latestHardware.GpuTemperatureC, _settings.Gpu);
		GpuHotspotTempText.Text = FormatTemperature(latestHardware.GpuHotspotTemperatureC);
		GpuHotspotTempText.Foreground = GetTemperatureBrush(latestHardware.GpuHotspotTemperatureC, _settings.Gpu);
		GpuVramTempText.Text = FormatTemperature(latestHardware.GpuVramTemperatureC);
		GpuVramTempText.Foreground = GetTemperatureBrush(latestHardware.GpuVramTemperatureC, _settings.Gpu);
		GpuClockText.Text = FormatClock(latestHardware.GpuClockMhz);
		GpuMemoryClockText.Text = FormatMemoryClock(latestHardware.GpuMemoryClockMhz);
		GpuVoltageText.Text = FormatVoltage(latestHardware.GpuVoltageV);
		GpuPowerText.Text = FormatPower(latestHardware.GpuPowerW);
		GpuFanRpmText.Text = FormatRpm(latestHardware.GpuFanRpm);
		GpuFanPercentText.Text = FormatPercent(latestHardware.GpuFanPercent);
		VramText.Text = FormatVram(latestHardware.VramUsedGb, latestHardware.VramTotalGb, latestHardware.VramLoadPercent, _settings.MemoryUnit);
		CpuTitleText.Text = GetHardwareDisplayName(_settings.Cpu, latestHardware.CpuName);
		CpuLoadText.Text = FormatPercent(latestHardware.CpuLoadPercent);
		CpuLoadText.Foreground = GetLoadBrush(latestHardware.CpuLoadPercent, _settings.Cpu, isGpu: false);
		CpuTempText.Text = FormatTemperature(latestHardware.CpuTemperatureC);
		CpuTempText.Foreground = GetTemperatureBrush(latestHardware.CpuTemperatureC, _settings.Cpu);
		UpdateCpuClockText(latestHardware);
		CpuPowerText.Text = FormatPower(latestHardware.CpuPowerW);
		if (IsLineOverlayMode())
		{
			ClearCpuCoreLoadGraph();
			ClearCpuCoreClockRows();
		}
		else
		{
			UpdateCpuCoreLoadGraph(latestHardware);
			UpdateCpuCoreClockRows(latestHardware);
		}
		RamUsedText.Text = FormatRamUsed(latestHardware.RamUsedGb, latestHardware.RamTotalGb, latestHardware.RamLoadPercent, _settings.Ram, _settings.MemoryUnit);
		RamLoadText.Text = $"{latestHardware.RamLoadPercent:0} %";
		RamSpeedText.Text = latestHardware.RamSpeedText;
		UpdateRamTemperatureRows(latestHardware);
		UpdateStatisticsLabels();
		StatsGpuMinMaxText.Text = FormatTemperatureStats(_gpuTemperatureStats, _settings.Statistics.GpuTemperatureStatsMode);
		StatsCpuMinMaxText.Text = FormatTemperatureStats(_cpuTemperatureStats, _settings.Statistics.CpuTemperatureStatsMode);
		StatsVramMinMaxText.Text = FormatTemperatureStats(_vramTemperatureStats, _settings.Statistics.VramTemperatureStatsMode);
		StatsHotspotMinMaxText.Text = FormatTemperatureStats(_hotspotTemperatureStats, _settings.Statistics.HotspotTemperatureStatsMode);
		StatsGpuVoltageMinMaxText.Text = FormatValueStats(_gpuVoltageStats, _settings.Statistics.GpuVoltageStatsMode, (double value) => $"{value:0.000}", "В");
		StatsGpuPowerMinMaxText.Text = FormatValueStats(_gpuPowerStats, _settings.Statistics.GpuPowerStatsMode, (double value) => $"{value:0.0}", "Вт");
		StatsCpuPowerMinMaxText.Text = FormatValueStats(_cpuPowerStats, _settings.Statistics.CpuPowerStatsMode, (double value) => $"{value:0.0}", "Вт");
		UpdateRamTemperatureStatsRows(latestHardware);
		UpdateStutterDetectorText();
		ApplyVisibilitySettings();
		UpdateOverlayWindowSizeAndPosition(updatePosition: false, allowAnchorRefresh: false);
		LogCpuOverlayVisibilityDiagnostics(latestHardware);
		LogHardwareUiDiagnostics(timestamp, lineMode: false);
	}

	private void LogCpuOverlayVisibilityDiagnostics(HardwareSnapshot hardware)
	{
		InlineArray10<object> buffer = default(InlineArray10<object>);
		buffer[0] = _settings.Cpu.ShowCoreLoadGraph;
		buffer[1] = CpuCoreLoadGraphHost.Visibility;
		buffer[2] = _settings.Cpu.ShowCoreClocks;
		buffer[3] = hardware.CpuCoreClocks.Count;
		buffer[4] = GetVisibleCpuCoreClocks(hardware.CpuCoreClocks).Count;
		buffer[5] = CpuCoreClockRowsPanel.Visibility;
		buffer[6] = IsHybridCpu(hardware);
		buffer[7] = Math.Round(GetCpuPerformanceAverageClockMhz(hardware).GetValueOrDefault());
		buffer[8] = Math.Round(GetCpuEfficiencyAverageClockMhz(hardware).GetValueOrDefault());
		buffer[9] = CpuEfficiencyClockLabel.Visibility;
		string text = string.Join("|", (ReadOnlySpan<object?>)buffer);
		if (!(text == _lastCpuOverlayVisibilityLogSignature))
		{
			_lastCpuOverlayVisibilityLogSignature = text;
			AppLogger.Info($"CPU overlay visibility: ShowGraph={_settings.Cpu.ShowCoreLoadGraph}, GraphVisibility={CpuCoreLoadGraphHost.Visibility}, ShowCoreClocks={_settings.Cpu.ShowCoreClocks}, CoreClockRows={hardware.CpuCoreClocks.Count}, VisibleCoreClockRows={GetVisibleCpuCoreClocks(hardware.CpuCoreClocks).Count}, CoreClockPanel={CpuCoreClockRowsPanel.Visibility}, Hybrid={IsHybridCpu(hardware)}, PAvg={FormatDiagnosticDouble(GetCpuPerformanceAverageClockMhz(hardware))}, EAvg={FormatDiagnosticDouble(GetCpuEfficiencyAverageClockMhz(hardware))}, EAvgRow={CpuEfficiencyClockLabel.Visibility}");
		}
	}

	private static string FormatDiagnosticDouble(double? value)
	{
		if (!value.HasValue)
		{
			return "N/A";
		}
		return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
	}

	private void LogHardwareUiDiagnostics(long updateStartTimestamp, bool lineMode)
	{
		if (AppLogger.SensorDiagnosticsEnabled)
		{
			int num = ++_hardwareUiDiagnosticsTick;
			if (num % 10 == 1)
			{
				long timestamp = Stopwatch.GetTimestamp();
				double value = (double)(timestamp - updateStartTimestamp) * 1000.0 / (double)Stopwatch.Frequency;
				double value2 = ((_lastHardwareUiDiagnosticsTimestamp == 0L) ? 0.0 : ((double)(timestamp - _lastHardwareUiDiagnosticsTimestamp) * 1000.0 / (double)Stopwatch.Frequency));
				_lastHardwareUiDiagnosticsTimestamp = timestamp;
				AppLogger.SensorDiagnostics($"Hardware UI update: Tick={num}; Interval={value2:0.0} ms; Duration={value:0.000} ms; LineMode={lineMode}; StatsBlock={_settings.Sections.Statistics && _settings.Statistics.ShowBlock}; StatsRows={GetEnabledStatisticsRowCount()}; RamTempRows={_latestHardware.RamModuleTemperatures.Count}; Graph={_settings.Sections.FrameTimeGraph && _settings.FrameTimeGraph.ShowGraph}; CpuCoreGraph={_settings.Cpu.ShowCoreLoadGraph}");
			}
		}
	}

	private void UpdateCpuClockText(HardwareSnapshot hardware)
	{
		if (IsHybridCpu(hardware))
		{
			CpuClockLabel.Text = "Частота P ядер";
			CpuClockText.Text = FormatClock(GetCpuPerformanceAverageClockMhz(hardware) ?? hardware.CpuClockMhz);
			CpuEfficiencyClockLabel.Text = "Частота E ядер";
			CpuEfficiencyClockText.Text = FormatClock(GetCpuEfficiencyAverageClockMhz(hardware));
		}
		else
		{
			CpuClockLabel.Text = "Частота";
			CpuClockText.Text = FormatClock(hardware.CpuClockMhz);
			CpuEfficiencyClockLabel.Text = "Частота E ядер";
			CpuEfficiencyClockText.Text = "N/A";
		}
	}

	private static bool IsHybridCpu(HardwareSnapshot hardware)
	{
		if (!HasSeparateCpuClockAverages(hardware) && !HasTypedCpuCoreClocks(hardware))
		{
			if (GetEffectivePerformanceCoreCount(hardware) > 0)
			{
				return GetEffectiveEfficiencyCoreCount(hardware) > 0;
			}
			return false;
		}
		return true;
	}

	private static bool HasSeparateCpuClockAverages(HardwareSnapshot hardware)
	{
		double? cpuPerformanceAverageClockMhz = GetCpuPerformanceAverageClockMhz(hardware);
		if (cpuPerformanceAverageClockMhz.HasValue && cpuPerformanceAverageClockMhz.GetValueOrDefault() > 0.0)
		{
			cpuPerformanceAverageClockMhz = GetCpuEfficiencyAverageClockMhz(hardware);
			if (cpuPerformanceAverageClockMhz.HasValue)
			{
				return cpuPerformanceAverageClockMhz.GetValueOrDefault() > 0.0;
			}
			return false;
		}
		return false;
	}

	private static bool HasTypedCpuCoreClocks(HardwareSnapshot hardware)
	{
		if (hardware.CpuCoreClocks.Any((CpuCoreClockReading clock) => clock.CoreType == CpuCoreType.Performance))
		{
			return hardware.CpuCoreClocks.Any((CpuCoreClockReading clock) => clock.CoreType == CpuCoreType.Efficiency);
		}
		return false;
	}

	private static double? GetCpuPerformanceAverageClockMhz(HardwareSnapshot hardware)
	{
		double? cpuPerformanceClockMhz = hardware.CpuPerformanceClockMhz;
		if (cpuPerformanceClockMhz.HasValue && cpuPerformanceClockMhz.GetValueOrDefault() > 0.0)
		{
			return hardware.CpuPerformanceClockMhz;
		}
		return AverageCpuCoreClockMhz(hardware.CpuCoreClocks, CpuCoreType.Performance);
	}

	private static double? GetCpuEfficiencyAverageClockMhz(HardwareSnapshot hardware)
	{
		double? cpuEfficiencyClockMhz = hardware.CpuEfficiencyClockMhz;
		if (cpuEfficiencyClockMhz.HasValue && cpuEfficiencyClockMhz.GetValueOrDefault() > 0.0)
		{
			return hardware.CpuEfficiencyClockMhz;
		}
		return AverageCpuCoreClockMhz(hardware.CpuCoreClocks, CpuCoreType.Efficiency);
	}

	private static double? AverageCpuCoreClockMhz(IReadOnlyList<CpuCoreClockReading> clocks, CpuCoreType coreType)
	{
		double[] array = (from clock in clocks
			where clock.CoreType == coreType && clock.ClockMhz > 0.0
			select clock.ClockMhz).ToArray();
		if (array.Length != 0)
		{
			return array.Average();
		}
		return null;
	}

	private static int GetEffectivePerformanceCoreCount(HardwareSnapshot hardware)
	{
		if (hardware.CpuPerformanceCoreCount > 0)
		{
			return hardware.CpuPerformanceCoreCount;
		}
		if (hardware.CpuPhysicalCoreCount > 0 && hardware.CpuLogicalThreadCount > hardware.CpuPhysicalCoreCount && hardware.CpuLogicalThreadCount < hardware.CpuPhysicalCoreCount * 2)
		{
			return hardware.CpuLogicalThreadCount - hardware.CpuPhysicalCoreCount;
		}
		return 0;
	}

	private static int GetEffectiveEfficiencyCoreCount(HardwareSnapshot hardware)
	{
		if (hardware.CpuEfficiencyCoreCount > 0)
		{
			return hardware.CpuEfficiencyCoreCount;
		}
		int effectivePerformanceCoreCount = GetEffectivePerformanceCoreCount(hardware);
		if (effectivePerformanceCoreCount > 0 && hardware.CpuPhysicalCoreCount > effectivePerformanceCoreCount)
		{
			return hardware.CpuPhysicalCoreCount - effectivePerformanceCoreCount;
		}
		return 0;
	}

	private int GetEnabledStatisticsRowCount()
	{
		if (!_settings.Sections.Statistics || !_settings.Statistics.ShowBlock || IsLineOverlayMode())
		{
			return 0;
		}
		int num = 0;
		if (_settings.Statistics.ShowGpuMinMax)
		{
			num++;
		}
		if (_settings.Statistics.ShowCpuMinMax)
		{
			num++;
		}
		if (_settings.Statistics.ShowVramMinMax)
		{
			num++;
		}
		if (_settings.Statistics.ShowHotspotMinMax)
		{
			num++;
		}
		if (_settings.Statistics.ShowGpuVoltageMinMax)
		{
			num++;
		}
		if (_settings.Statistics.ShowGpuPowerMinMax)
		{
			num++;
		}
		if (_settings.Statistics.ShowCpuPowerMinMax)
		{
			num++;
		}
		if (_settings.Statistics.ShowRamTemperatureStats)
		{
			num += _latestHardware.RamModuleTemperatures.Count;
		}
		if (_settings.Statistics.ShowStutterDetector)
		{
			num += 3;
		}
		return num;
	}

	private void UpdateStutterDetectorText()
	{
		if (_settings.Statistics.ShowStutterDetector)
		{
			StutterDetectorSnapshot stutterSnapshot = _fpsMonitor.GetStutterSnapshot();
			StatsStutterStateText.Text = FormatStutterState(stutterSnapshot.State);
			StatsStutterStateText.Foreground = GetStutterStateBrush(stutterSnapshot.State);
			StatsStutterCountText.Text = stutterSnapshot.StutterCount.ToString();
			TextBlock statsLastStutterText = StatsLastStutterText;
			double? lastStutterFrameTimeMs = stutterSnapshot.LastStutterFrameTimeMs;
			statsLastStutterText.Text = ((lastStutterFrameTimeMs.HasValue && lastStutterFrameTimeMs.GetValueOrDefault() > 0.0) ? $"{stutterSnapshot.LastStutterFrameTimeMs.Value:0.0} ms" : "N/A");
		}
	}

	private static string FormatStutterState(StutterState state)
	{
		return state switch
		{
			StutterState.Microstutter => "Микростаттеры", 
			StutterState.Stutter => "Статтеры", 
			StutterState.Freeze => "Фризы", 
			StutterState.NoData => "Плавно", 
			_ => "Плавно", 
		};
	}

	private System.Windows.Media.Brush GetStutterStateBrush(StutterState state)
	{
		if (IsFreeVersion)
		{
			return FreeVersionTextBrush;
		}
		return state switch
		{
			StutterState.Microstutter => new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 220, 70)), 
			StutterState.Stutter => WarmTemperatureBrush, 
			StutterState.Freeze => HotTemperatureBrush, 
			_ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(53, 212, 99)), 
		};
	}

	private void UpdateCpuCoreLoadGraph(HardwareSnapshot hardware)
	{
		if (!_settings.Cpu.ShowCoreLoadGraph)
		{
			ClearCpuCoreLoadGraph();
			return;
		}
		IReadOnlyList<CpuCoreLoadGraphBar> readOnlyList2;
		if (hardware.CpuCoreLoadGraphBars.Count <= 0)
		{
			IReadOnlyList<CpuCoreLoadGraphBar> readOnlyList = hardware.CpuLogicalThreadLoads.Select((double load) => new CpuCoreLoadGraphBar(load, CpuCoreType.Unknown)).ToArray();
			readOnlyList2 = readOnlyList;
		}
		else
		{
			readOnlyList2 = hardware.CpuCoreLoadGraphBars;
		}
		IReadOnlyList<CpuCoreLoadGraphBar> readOnlyList3 = readOnlyList2;
		if (readOnlyList3.Count == 0)
		{
			CpuCoreLoadGraphHost.Visibility = Visibility.Collapsed;
			_requiredCpuCoreGraphWidth = 0.0;
			CpuCoreLoadGraphHost.MinWidth = 0.0;
			CpuCoreLoadGraphCanvas.MinWidth = 0.0;
			return;
		}
		CpuCoreLoadGraphHost.Visibility = Visibility.Visible;
		EnsureCpuCoreLoadBars(readOnlyList3.Count);
		double num = ((readOnlyList3.Count > 32) ? 1 : 2);
		double num2 = (_requiredCpuCoreGraphWidth = CalculateCpuCoreGraphWidth(readOnlyList3, num));
		CpuCoreLoadGraphHost.Height = (_settings.Cpu.ShowCoreLoadGraphLabels ? 52.0 : 40.0);
		CpuCoreLoadGraphCanvas.Height = CpuCoreLoadGraphHost.Height;
		CpuCoreLoadGraphHost.MinWidth = num2;
		CpuCoreLoadGraphCanvas.MinWidth = num2;
		double num3 = 40.0;
		double num4 = 0.0;
		IReadOnlyList<string> readOnlyList4 = BuildCpuCoreGraphLabels(readOnlyList3, hardware);
		System.Windows.Media.Brush foreground = BrushFromHex(_settings.Cpu.LabelColor, "#FF58A6FF");
		for (int num5 = 0; num5 < _cpuCoreLoadBars.Count; num5++)
		{
			CpuCoreLoadGraphItem cpuCoreLoadGraphItem = _cpuCoreLoadBars[num5];
			if (num5 >= readOnlyList3.Count)
			{
				cpuCoreLoadGraphItem.Bar.Visibility = Visibility.Collapsed;
				cpuCoreLoadGraphItem.Label.Visibility = Visibility.Collapsed;
				continue;
			}
			CpuCoreLoadGraphBar cpuCoreLoadGraphBar = readOnlyList3[num5];
			double cpuCoreGraphBarWidth = GetCpuCoreGraphBarWidth(cpuCoreLoadGraphBar.CoreType);
			double num6 = Math.Clamp(cpuCoreLoadGraphBar.LoadPercent, 0.0, 100.0);
			double num7 = Math.Max(2.0, Math.Round(num3 * num6 / 100.0));
			cpuCoreLoadGraphItem.Bar.Visibility = Visibility.Visible;
			cpuCoreLoadGraphItem.Bar.Width = cpuCoreGraphBarWidth;
			cpuCoreLoadGraphItem.Bar.Height = num7;
			cpuCoreLoadGraphItem.Bar.RadiusX = 1.0;
			cpuCoreLoadGraphItem.Bar.RadiusY = 1.0;
			cpuCoreLoadGraphItem.Bar.Fill = GetCpuCoreLoadBrush(num6);
			Canvas.SetLeft(cpuCoreLoadGraphItem.Bar, num4);
			Canvas.SetTop(cpuCoreLoadGraphItem.Bar, num3 - num7);
			cpuCoreLoadGraphItem.Label.Visibility = ((!_settings.Cpu.ShowCoreLoadGraphLabels) ? Visibility.Collapsed : Visibility.Visible);
			cpuCoreLoadGraphItem.Label.Text = ((num5 < readOnlyList4.Count) ? readOnlyList4[num5] : FormatCpuCoreGraphFallbackLabel(cpuCoreLoadGraphBar));
			cpuCoreLoadGraphItem.Label.Width = cpuCoreGraphBarWidth;
			cpuCoreLoadGraphItem.Label.Height = 11.0;
			cpuCoreLoadGraphItem.Label.Foreground = foreground;
			Canvas.SetLeft(cpuCoreLoadGraphItem.Label, num4);
			Canvas.SetTop(cpuCoreLoadGraphItem.Label, 41.0);
			num4 += cpuCoreGraphBarWidth + num;
		}
		LogCpuCoreGraphDiagnostics(hardware, readOnlyList3.Count, num2, Math.Max(base.Width, Math.Max(330.0, num2 + 24.0)));
	}

	private void ClearCpuCoreLoadGraph()
	{
		_requiredCpuCoreGraphWidth = 0.0;
		CpuCoreLoadGraphHost.Visibility = Visibility.Collapsed;
		CpuCoreLoadGraphHost.MinWidth = 0.0;
		CpuCoreLoadGraphCanvas.MinWidth = 0.0;
	}

	private void UpdateCpuCoreClockRows(HardwareSnapshot hardware)
	{
		IReadOnlyList<CpuCoreClockReading> visibleCpuCoreClocks = GetVisibleCpuCoreClocks(hardware.CpuCoreClocks);
		if (!_settings.Cpu.ShowCoreClocks || visibleCpuCoreClocks.Count == 0)
		{
			ClearCpuCoreClockRows();
			return;
		}
		IReadOnlyList<CpuCoreClockDisplayRow> readOnlyList = BuildCpuCoreClockDisplayRows(visibleCpuCoreClocks);
		if (readOnlyList.Count == 0)
		{
			ClearCpuCoreClockRows();
			return;
		}
		EnsureCpuCoreClockRows(readOnlyList.Count);
		System.Windows.Media.Brush foreground = BrushFromHex(_settings.Cpu.LabelColor, "#FF58A6FF");
		System.Windows.Media.Brush foreground2 = BrushFromHex("#FFF2F2F2", "#FFF2F2F2");
		CpuCoreClockRowsPanel.Visibility = Visibility.Visible;
		CpuCoreClockRowsPanel.MinWidth = 298.0;
		_requiredCpuCoreClockWidth = 298.0;
		for (int i = 0; i < _cpuCoreClockRows.Count; i++)
		{
			CpuCoreClockRow cpuCoreClockRow = _cpuCoreClockRows[i];
			if (i >= readOnlyList.Count)
			{
				cpuCoreClockRow.Container.Visibility = Visibility.Collapsed;
				continue;
			}
			CpuCoreClockDisplayRow cpuCoreClockDisplayRow = readOnlyList[i];
			cpuCoreClockRow.Container.Visibility = Visibility.Visible;
			if (!string.IsNullOrWhiteSpace(cpuCoreClockDisplayRow.Header))
			{
				cpuCoreClockRow.Header.Visibility = Visibility.Visible;
				cpuCoreClockRow.Header.Text = cpuCoreClockDisplayRow.Header;
				cpuCoreClockRow.Header.Foreground = foreground2;
				SetCpuCoreClockCellsVisibility(cpuCoreClockRow, Visibility.Collapsed);
				continue;
			}
			cpuCoreClockRow.Header.Visibility = Visibility.Collapsed;
			SetCpuCoreClockCellsVisibility(cpuCoreClockRow, Visibility.Visible);
			CpuCoreClockReading left = cpuCoreClockDisplayRow.Left;
			if (left == null)
			{
				cpuCoreClockRow.Container.Visibility = Visibility.Collapsed;
				continue;
			}
			cpuCoreClockRow.LeftLabel.Text = FormatCoreClockLabel(left);
			cpuCoreClockRow.LeftLabel.Foreground = foreground;
			cpuCoreClockRow.LeftValue.Text = FormatCoreClockValue(left.ClockMhz);
			cpuCoreClockRow.LeftValue.Foreground = foreground2;
			CpuCoreClockReading right = cpuCoreClockDisplayRow.Right;
			if ((object)right != null)
			{
				cpuCoreClockRow.Separator.Visibility = Visibility.Visible;
				cpuCoreClockRow.RightLabel.Visibility = Visibility.Visible;
				cpuCoreClockRow.RightValue.Visibility = Visibility.Visible;
				cpuCoreClockRow.RightLabel.Text = FormatCoreClockLabel(right);
				cpuCoreClockRow.RightLabel.Foreground = foreground;
				cpuCoreClockRow.RightValue.Text = FormatCoreClockValue(right.ClockMhz);
				cpuCoreClockRow.RightValue.Foreground = foreground2;
			}
			else
			{
				cpuCoreClockRow.Separator.Visibility = Visibility.Hidden;
				cpuCoreClockRow.RightLabel.Visibility = Visibility.Hidden;
				cpuCoreClockRow.RightValue.Visibility = Visibility.Hidden;
				cpuCoreClockRow.RightLabel.Text = string.Empty;
				cpuCoreClockRow.RightValue.Text = string.Empty;
			}
		}
	}

	private bool HasVisibleCpuCoreClocks(IReadOnlyList<CpuCoreClockReading> clocks)
	{
		return GetVisibleCpuCoreClocks(clocks).Count > 0;
	}

	private IReadOnlyList<CpuCoreClockReading> GetVisibleCpuCoreClocks(IReadOnlyList<CpuCoreClockReading> clocks)
	{
		if (clocks.Count == 0)
		{
			return Array.Empty<CpuCoreClockReading>();
		}
		return clocks.Where((CpuCoreClockReading clock) => clock.CoreType switch
		{
			CpuCoreType.Performance => _settings.Cpu.ShowPerformanceCoreClocks, 
			CpuCoreType.Efficiency => _settings.Cpu.ShowEfficiencyCoreClocks, 
			_ => _settings.Cpu.ShowPerformanceCoreClocks || _settings.Cpu.ShowEfficiencyCoreClocks, 
		}).ToArray();
	}

	private static IReadOnlyList<CpuCoreClockDisplayRow> BuildCpuCoreClockDisplayRows(IReadOnlyList<CpuCoreClockReading> clocks)
	{
		if (clocks.Count == 0)
		{
			return Array.Empty<CpuCoreClockDisplayRow>();
		}
		List<CpuCoreClockDisplayRow> list = new List<CpuCoreClockDisplayRow>();
		CpuCoreType[] array = clocks.Select((CpuCoreClockReading clock) => clock.CoreType).Distinct().ToArray();
		bool flag = array.Length > 1 || array.Any((CpuCoreType type) => type != CpuCoreType.Unknown);
		foreach (IGrouping<CpuCoreType, CpuCoreClockReading> item in from clock in clocks
			group clock by clock.CoreType into @group
			orderby GetCpuCoreClockTypeDisplayOrder(@group.Key)
			select @group)
		{
			CpuCoreClockReading[] array2 = item.OrderBy((CpuCoreClockReading clock) => clock.CoreIndex).ToArray();
			if (array2.Length != 0)
			{
				if (flag)
				{
					list.Add(new CpuCoreClockDisplayRow(GetCpuCoreClockGroupTitle(item.Key), null, null));
				}
				for (int num = 0; num < array2.Length; num += 2)
				{
					CpuCoreClockReading right = ((num + 1 < array2.Length) ? array2[num + 1] : null);
					list.Add(new CpuCoreClockDisplayRow(null, array2[num], right));
				}
			}
		}
		return list;
	}

	private static string GetCpuCoreClockGroupTitle(CpuCoreType coreType)
	{
		return coreType switch
		{
			CpuCoreType.Performance => "P-ядра", 
			CpuCoreType.Efficiency => "E-ядра", 
			_ => "Ядра", 
		};
	}

	private static int GetCpuCoreClockTypeDisplayOrder(CpuCoreType coreType)
	{
		return coreType switch
		{
			CpuCoreType.Performance => 0, 
			CpuCoreType.Efficiency => 1, 
			_ => 2, 
		};
	}

	private static void SetCpuCoreClockCellsVisibility(CpuCoreClockRow row, Visibility visibility)
	{
		row.LeftLabel.Visibility = visibility;
		row.LeftValue.Visibility = visibility;
		row.Separator.Visibility = visibility;
		row.RightLabel.Visibility = visibility;
		row.RightValue.Visibility = visibility;
	}

	private void ClearCpuCoreClockRows()
	{
		_requiredCpuCoreClockWidth = 0.0;
		CpuCoreClockRowsPanel.Visibility = Visibility.Collapsed;
		CpuCoreClockRowsPanel.MinWidth = 0.0;
	}

	private void EnsureCpuCoreClockRows(int count)
	{
		while (_cpuCoreClockRows.Count < count)
		{
			Grid grid = new Grid
			{
				MinWidth = 298.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 1.0),
				SnapsToDevicePixels = true
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(68.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(72.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(18.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(68.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(72.0)
			});
			TextBlock textBlock = CreateCpuCoreClockTextBlock(FontWeights.Bold);
			TextBlock textBlock2 = CreateCpuCoreClockTextBlock(FontWeights.Normal);
			TextBlock textBlock3 = CreateCpuCoreClockTextBlock(FontWeights.Bold);
			TextBlock textBlock4 = CreateCpuCoreClockTextBlock(FontWeights.Normal);
			textBlock4.Text = "|";
			textBlock4.TextAlignment = TextAlignment.Center;
			TextBlock textBlock5 = CreateCpuCoreClockTextBlock(FontWeights.Normal);
			TextBlock textBlock6 = CreateCpuCoreClockTextBlock(FontWeights.Bold);
			Grid.SetColumn(textBlock, 0);
			Grid.SetColumnSpan(textBlock, 5);
			Grid.SetColumn(textBlock2, 0);
			Grid.SetColumn(textBlock3, 1);
			Grid.SetColumn(textBlock4, 2);
			Grid.SetColumn(textBlock5, 3);
			Grid.SetColumn(textBlock6, 4);
			grid.Children.Add(textBlock);
			grid.Children.Add(textBlock2);
			grid.Children.Add(textBlock3);
			grid.Children.Add(textBlock4);
			grid.Children.Add(textBlock5);
			grid.Children.Add(textBlock6);
			CpuCoreClockRowsPanel.Children.Add(grid);
			OverlayFontApplicator.Apply(grid, _overlayFont, _settings.Appearance.TextScale);
			_cpuCoreClockRows.Add(new CpuCoreClockRow(grid, textBlock, textBlock2, textBlock3, textBlock4, textBlock5, textBlock6));
		}
	}

	private static TextBlock CreateCpuCoreClockTextBlock(FontWeight fontWeight)
	{
		return new TextBlock
		{
			FontSize = 13.0,
			FontWeight = fontWeight,
			TextAlignment = TextAlignment.Left,
			TextWrapping = TextWrapping.NoWrap,
			TextTrimming = TextTrimming.None
		};
	}

	private static string FormatCoreClockLabel(CpuCoreClockReading reading)
	{
		return $"Ядро {reading.CoreIndex:00}:";
	}

	private string FormatCoreClockValue(double valueMhz)
	{
		if (valueMhz <= 0.0)
		{
			return "N/A";
		}
		if (!_settings.ClockUnit.Equals("ГГц", StringComparison.OrdinalIgnoreCase) && !_settings.ClockUnit.Equals("GHz", StringComparison.OrdinalIgnoreCase))
		{
			return $"{Math.Clamp(Math.Round(valueMhz), 0.0, 9999.0):0} МГц";
		}
		return $"{valueMhz / 1000.0:0.0} ГГц";
	}

	private static double CalculateCpuCoreGraphWidth(IReadOnlyList<CpuCoreLoadGraphBar> bars, double gap)
	{
		if (bars.Count == 0)
		{
			return 0.0;
		}
		return bars.Sum((CpuCoreLoadGraphBar bar) => GetCpuCoreGraphBarWidth(bar.CoreType)) + gap * (double)(bars.Count - 1);
	}

	private static double GetCpuCoreGraphBarWidth(CpuCoreType coreType)
	{
		if (coreType != CpuCoreType.Efficiency)
		{
			return 10.0;
		}
		return Math.Max(2.0, 5.0);
	}

	private static System.Windows.Media.Brush GetCpuCoreLoadBrush(double loadPercent)
	{
		double num = Math.Clamp(loadPercent, 0.0, 100.0);
		System.Windows.Media.Color start = System.Windows.Media.Color.FromRgb(53, 212, 99);
		System.Windows.Media.Color color = System.Windows.Media.Color.FromRgb(byte.MaxValue, 224, 102);
		System.Windows.Media.Color end = System.Windows.Media.Color.FromRgb(byte.MaxValue, 76, 76);
		return new SolidColorBrush((num <= 50.0) ? InterpolateColor(start, color, num / 50.0) : InterpolateColor(color, end, (num - 50.0) / 50.0));
	}

	private static System.Windows.Media.Color InterpolateColor(System.Windows.Media.Color start, System.Windows.Media.Color end, double amount)
	{
		double num = Math.Clamp(amount, 0.0, 1.0);
		return System.Windows.Media.Color.FromRgb((byte)Math.Round((double)(int)start.R + (double)(end.R - start.R) * num), (byte)Math.Round((double)(int)start.G + (double)(end.G - start.G) * num), (byte)Math.Round((double)(int)start.B + (double)(end.B - start.B) * num));
	}

	private static IReadOnlyList<string> BuildCpuCoreGraphLabels(IReadOnlyList<CpuCoreLoadGraphBar> bars, HardwareSnapshot hardware)
	{
		if (bars.Count == 0)
		{
			return Array.Empty<string>();
		}
		bool flag = IsHybridCpu(hardware);
		string[] array = new string[bars.Count];
		Dictionary<string, int> coreIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		Dictionary<CpuCoreType, int> nextIndexesByType = new Dictionary<CpuCoreType, int>();
		int num = 0;
		int num2 = Math.Max(0, GetEffectivePerformanceCoreCount(hardware) * 2);
		int classicGraphThreadsPerCore = GetClassicGraphThreadsPerCore(bars.Count, hardware);
		for (int i = 0; i < bars.Count; i++)
		{
			CpuCoreLoadGraphBar cpuCoreLoadGraphBar = bars[i];
			if (flag)
			{
				if (cpuCoreLoadGraphBar.CoreType == CpuCoreType.Efficiency)
				{
					array[i] = "E";
					continue;
				}
				if (cpuCoreLoadGraphBar.CoreType == CpuCoreType.Performance)
				{
					int coreIndex;
					int value = (TryGetStableCoreIndex(cpuCoreLoadGraphBar, coreIndexes, nextIndexesByType, out coreIndex) ? coreIndex : (num++ / 2));
					array[i] = $"P{value}";
					continue;
				}
				if (cpuCoreLoadGraphBar.CoreType == CpuCoreType.Unknown)
				{
					array[i] = ((i < num2) ? $"P{i / 2}" : "E");
					continue;
				}
			}
			int coreIndex2;
			if (classicGraphThreadsPerCore > 1)
			{
				array[i] = $"C{i / classicGraphThreadsPerCore}";
			}
			else if (TryGetStableCoreIndex(cpuCoreLoadGraphBar with
			{
				CoreType = CpuCoreType.Unknown
			}, coreIndexes, nextIndexesByType, out coreIndex2))
			{
				array[i] = $"C{coreIndex2}";
			}
			else
			{
				array[i] = $"C{i}";
			}
		}
		return array;
	}

	private static int GetClassicGraphThreadsPerCore(int barCount, HardwareSnapshot hardware)
	{
		if (hardware.CpuCoreClocks.Count > 0 && barCount > hardware.CpuCoreClocks.Count)
		{
			int num = (int)Math.Round((double)barCount / (double)hardware.CpuCoreClocks.Count);
			if (num > 1)
			{
				return num;
			}
		}
		if (hardware.CpuPhysicalCoreCount <= 0 || barCount <= hardware.CpuPhysicalCoreCount)
		{
			return 1;
		}
		int num2 = ((hardware.CpuLogicalThreadCount > 0) ? hardware.CpuLogicalThreadCount : barCount);
		if (num2 <= hardware.CpuPhysicalCoreCount)
		{
			return 1;
		}
		int val = (int)Math.Round((double)num2 / (double)hardware.CpuPhysicalCoreCount);
		return Math.Max(1, val);
	}

	private static bool TryGetStableCoreIndex(CpuCoreLoadGraphBar bar, IDictionary<string, int> coreIndexes, IDictionary<CpuCoreType, int> nextIndexesByType, out int coreIndex)
	{
		coreIndex = 0;
		int? coreNumber = bar.CoreNumber;
		if (!coreNumber.HasValue || coreNumber.GetValueOrDefault() <= 0)
		{
			return false;
		}
		string cpuCoreGraphLabelKey = GetCpuCoreGraphLabelKey(bar.CoreType, bar.CoreNumber.Value);
		if (coreIndexes.TryGetValue(cpuCoreGraphLabelKey, out coreIndex))
		{
			return true;
		}
		nextIndexesByType.TryGetValue(bar.CoreType, out var value);
		coreIndexes[cpuCoreGraphLabelKey] = value;
		nextIndexesByType[bar.CoreType] = value + 1;
		coreIndex = value;
		return true;
	}

	private static string FormatCpuCoreGraphFallbackLabel(CpuCoreLoadGraphBar bar)
	{
		return bar.CoreType switch
		{
			CpuCoreType.Performance => "P", 
			CpuCoreType.Efficiency => "E", 
			_ => "C", 
		};
	}

	private static string GetCpuCoreGraphLabelKey(CpuCoreType coreType, int coreNumber)
	{
		return $"{coreType}:{coreNumber}";
	}

	private void LogCpuCoreGraphDiagnostics(HardwareSnapshot hardware, int renderedBars, double requiredGraphWidth, double finalOverlayWidth)
	{
		InlineArray11<object> buffer = default(InlineArray11<object>);
		buffer[0] = hardware.CpuName;
		buffer[1] = hardware.CpuPhysicalCoreCount;
		buffer[2] = hardware.CpuLogicalThreadCount;
		buffer[3] = hardware.CpuPerformanceCoreCount;
		buffer[4] = hardware.CpuEfficiencyCoreCount;
		buffer[5] = hardware.CpuCoreLoadGraphBars.Count((CpuCoreLoadGraphBar bar) => bar.SensorName.Length > 0);
		buffer[6] = hardware.CpuCoreLoadGraphBars.Count((CpuCoreLoadGraphBar bar) => bar.CoreType == CpuCoreType.Performance);
		buffer[7] = hardware.CpuCoreLoadGraphBars.Count((CpuCoreLoadGraphBar bar) => bar.CoreType == CpuCoreType.Efficiency);
		buffer[8] = renderedBars;
		buffer[9] = Math.Round(requiredGraphWidth);
		buffer[10] = Math.Round(finalOverlayWidth);
		string text = string.Join("|", (ReadOnlySpan<object?>)buffer);
		if (!(text == _lastCpuCoreGraphLogSignature))
		{
			_lastCpuCoreGraphLogSignature = text;
			AppLogger.Info($"CPU graph: Name={hardware.CpuName}, PhysicalCores={hardware.CpuPhysicalCoreCount}, LogicalThreads={hardware.CpuLogicalThreadCount}, PCores={hardware.CpuPerformanceCoreCount}, ECores={hardware.CpuEfficiencyCoreCount}, LoadSensors={hardware.CpuCoreLoadGraphBars.Count((CpuCoreLoadGraphBar bar) => bar.SensorName.Length > 0)}, ParsedPThreads={hardware.CpuCoreLoadGraphBars.Count((CpuCoreLoadGraphBar bar) => bar.CoreType == CpuCoreType.Performance)}, ParsedEThreads={hardware.CpuCoreLoadGraphBars.Count((CpuCoreLoadGraphBar bar) => bar.CoreType == CpuCoreType.Efficiency)}, Bars={renderedBars}, RequiredGraphWidth={requiredGraphWidth:0.##}, FinalOverlayWidth={finalOverlayWidth:0.##}");
			for (int num = 0; num < hardware.CpuCoreLoadGraphBars.Count; num++)
			{
				CpuCoreLoadGraphBar cpuCoreLoadGraphBar = hardware.CpuCoreLoadGraphBars[num];
				double cpuCoreGraphBarWidth = GetCpuCoreGraphBarWidth(cpuCoreLoadGraphBar.CoreType);
				double value = Math.Max(2.0, Math.Round(40.0 * Math.Clamp(cpuCoreLoadGraphBar.LoadPercent, 0.0, 100.0) / 100.0));
				AppLogger.Info($"CPU graph bar #{num}: Core={cpuCoreLoadGraphBar.CoreNumber?.ToString() ?? "N/A"}, Thread={cpuCoreLoadGraphBar.ThreadNumber?.ToString() ?? "N/A"}, Type={cpuCoreLoadGraphBar.CoreType}, Sensor='{cpuCoreLoadGraphBar.SensorName}', RawLoad={cpuCoreLoadGraphBar.LoadPercent:0.##}, RenderedWidth={cpuCoreGraphBarWidth:0.##}, RenderedHeight={value:0.##}");
			}
		}
	}

	private void EnsureCpuCoreLoadBars(int count)
	{
		while (_cpuCoreLoadBars.Count < count)
		{
			System.Windows.Shapes.Rectangle rectangle = new System.Windows.Shapes.Rectangle
			{
				SnapsToDevicePixels = true
			};
			TextBlock textBlock = new TextBlock
			{
				FontSize = 7.0,
				FontWeight = FontWeights.Bold,
				TextAlignment = TextAlignment.Center,
				TextWrapping = TextWrapping.NoWrap,
				TextTrimming = TextTrimming.None,
				Foreground = BrushFromHex(_settings.Cpu.LabelColor, "#FF58A6FF")
			};
			_cpuCoreLoadBars.Add(new CpuCoreLoadGraphItem(rectangle, textBlock));
			CpuCoreLoadGraphCanvas.Children.Add(rectangle);
			CpuCoreLoadGraphCanvas.Children.Add(textBlock);
		}
	}

	private async void QueueHardwareUpdate()
	{
		if (_hardwareUpdateInProgress)
		{
			return;
		}
		HardwareMonitorService hardwareMonitor = _hardwareMonitor;
		if (hardwareMonitor == null)
		{
			return;
		}
		hardwareMonitor.PollIntervalMs = _settings.Fps.UpdateIntervalMs;
		_hardwareUpdateInProgress = true;
		try
		{
			long snapshotStart = Stopwatch.GetTimestamp();
			HardwareSnapshot hardwareSnapshot = await Task.Run((Func<HardwareSnapshot>)hardwareMonitor.GetSnapshot);
			LogHardwareSnapshotDiagnostics(snapshotStart, hardwareSnapshot);
			UpdateSessionHardwareStats(hardwareSnapshot);
			_latestHardware = hardwareSnapshot;
			_hardwareSnapshotVersion++;
		}
		catch
		{
		}
		finally
		{
			_hardwareUpdateInProgress = false;
		}
	}

	private static void LogHardwareSnapshotDiagnostics(long snapshotStartTimestamp, HardwareSnapshot snapshot)
	{
		if (AppLogger.SensorDiagnosticsEnabled)
		{
			double value = (double)(Stopwatch.GetTimestamp() - snapshotStartTimestamp) * 1000.0 / (double)Stopwatch.Frequency;
			AppLogger.SensorDiagnostics($"Hardware snapshot complete: Duration={value:0.000} ms; GpuLoad={FormatNullableDiagnostic(snapshot.GpuLoadPercent)}; CpuLoad={FormatNullableDiagnostic(snapshot.CpuLoadPercent)}; RamTemps={snapshot.RamModuleTemperatures.Count}");
		}
	}

	private static string FormatNullableDiagnostic(double? value)
	{
		if (!value.HasValue)
		{
			return "N/A";
		}
		return value.Value.ToString("0.###");
	}

	public void ResetSessionStats()
	{
		_gpuTemperatureStats.Reset();
		_cpuTemperatureStats.Reset();
		_vramTemperatureStats.Reset();
		_hotspotTemperatureStats.Reset();
		_gpuVoltageStats.Reset();
		_gpuPowerStats.Reset();
		_cpuPowerStats.Reset();
		_ramTemperatureStats.Clear();
		_fpsMonitor.ResetStutterStats();
		ResetFrameTimeGraphVisualState();
		UpdateSessionHardwareStats(_latestHardware);
		UpdateHardwareText();
	}

	private void UpdateSessionHardwareStats(HardwareSnapshot snapshot)
	{
		_gpuTemperatureStats.Update(snapshot.GpuTemperatureC);
		_cpuTemperatureStats.Update(snapshot.CpuTemperatureC);
		_vramTemperatureStats.Update(snapshot.GpuVramTemperatureC);
		_hotspotTemperatureStats.Update(snapshot.GpuHotspotTemperatureC);
		_gpuVoltageStats.Update(snapshot.GpuVoltageV);
		_gpuPowerStats.Update(snapshot.GpuPowerW);
		_cpuPowerStats.Update(snapshot.CpuPowerW);
		foreach (RamModuleTemperature ramModuleTemperature in snapshot.RamModuleTemperatures)
		{
			if (!_ramTemperatureStats.TryGetValue(ramModuleTemperature.Name, out SessionStat value))
			{
				value = new SessionStat();
				_ramTemperatureStats[ramModuleTemperature.Name] = value;
			}
			value.Update(ramModuleTemperature.TemperatureC);
		}
	}

	private void DrawHwInfoGraph()
	{
		if (IsLineOverlayMode() || !_settings.Sections.FrameTimeGraph || !_settings.FrameTimeGraph.ShowGraph)
		{
			ResetFrameTimeGraphVisualState();
			return;
		}
		double actualWidth = FrameTimeGraphCanvas.ActualWidth;
		double actualHeight = FrameTimeGraphCanvas.ActualHeight;
		if (actualWidth <= 1.0 || actualHeight <= 1.0)
		{
			ResetFrameTimeGraphVisualState();
			return;
		}
		FrameTimeGraphSnapshot frameTimeGraphSnapshot = _fpsMonitor.GetFrameTimeGraphSnapshot();
		UpdateFrameTimeGraphSamples(frameTimeGraphSnapshot);
		IReadOnlyList<FrameTimeGraphPoint> frameTimeGraphDisplayPoints = GetFrameTimeGraphDisplayPoints();
		LogFrameTimeGraphMode(actualWidth);
		if (frameTimeGraphDisplayPoints.Count < 2)
		{
			ClearFrameTimeGraphVisuals();
			return;
		}
		FrameTimeGraphCanvas.MaxFrameTimeMs = Math.Max(1.0, _settings.FrameTimeGraph.MaxMs);
		FrameTimeGraphCanvas.SetPoints(frameTimeGraphDisplayPoints);
	}

	private void OnFrameTimeGraphRendering(object? sender, EventArgs e)
	{
		if (e is RenderingEventArgs e2)
		{
			if (e2.RenderingTime == _lastFrameTimeGraphRenderTime)
			{
				return;
			}
			_lastFrameTimeGraphRenderTime = e2.RenderingTime;
		}
		DrawHwInfoGraph();
	}

	private void ClearFrameTimeGraphVisuals()
	{
		FrameTimeGraphCanvas.Clear();
	}

	private static bool IsValidGraphFrameTime(double value)
	{
		if (!double.IsNaN(value) && !double.IsInfinity(value))
		{
			return value >= 1.0;
		}
		return false;
	}

	private void UpdateFrameTimeGraphSamples(FrameTimeGraphSnapshot snapshot)
	{
		if (!(snapshot.NowSeconds <= 0.0))
		{
			double num = 0.03333333300000001;
			if (_frameTimeGraphLastSampleEndSeconds <= 0.0)
			{
				_frameTimeGraphLastSampleEndSeconds = Math.Max(0.0, snapshot.NowSeconds - 10.0);
			}
			int num2 = 600;
			if ((snapshot.NowSeconds - _frameTimeGraphLastSampleEndSeconds) / num > (double)num2)
			{
				ClearFrameTimeGraphSampleBuffer();
				_frameTimeGraphLastSampleEndSeconds = Math.Max(0.0, snapshot.NowSeconds - num);
			}
			int num3 = 0;
			while (_frameTimeGraphLastSampleEndSeconds + num <= snapshot.NowSeconds && num3 < num2)
			{
				double frameTimeGraphLastSampleEndSeconds = _frameTimeGraphLastSampleEndSeconds;
				double num4 = frameTimeGraphLastSampleEndSeconds + num;
				FrameTimeGraphPoint point = CreateFrameTimeGraphPoint(snapshot, frameTimeGraphLastSampleEndSeconds, num4);
				AppendFrameTimeGraphPoint(point);
				_frameTimeGraphLastSampleEndSeconds = num4;
				num3++;
			}
		}
	}

	private FrameTimeGraphPoint CreateFrameTimeGraphPoint(FrameTimeGraphSnapshot snapshot, double bucketStartSeconds, double bucketEndSeconds)
	{
		double num = double.MaxValue;
		double num2 = 0.0;
		double num3 = 0.0;
		int num4 = 0;
		FrameTimeGraphSample? frameTimeGraphSample = null;
		foreach (FrameTimeGraphSample sample in snapshot.Samples)
		{
			if (IsValidGraphFrameTime(sample.FrameTimeMs) && !(sample.TimeSeconds <= 0.0) && !(sample.TimeSeconds > snapshot.NowSeconds))
			{
				if (!frameTimeGraphSample.HasValue || sample.TimeSeconds > frameTimeGraphSample.Value.TimeSeconds)
				{
					frameTimeGraphSample = sample;
				}
				double timeSeconds = sample.TimeSeconds;
				if (!(timeSeconds - sample.FrameTimeMs / 1000.0 >= bucketEndSeconds) && !(timeSeconds <= bucketStartSeconds))
				{
					num = Math.Min(num, sample.FrameTimeMs);
					num2 = Math.Max(num2, sample.FrameTimeMs);
					num3 += sample.FrameTimeMs;
					num4++;
				}
			}
		}
		if (num4 > 0)
		{
			double num5 = num3 / (double)num4;
			double value = ((num > 0.0 && num2 / num <= 1.1) ? num5 : num2);
			return new FrameTimeGraphPoint(bucketEndSeconds, value, IsSynthetic: false, IsHold: false, IsInProgress: false);
		}
		double? frameTimeGraphLastValidValue;
		if (frameTimeGraphSample.HasValue)
		{
			double num6 = Math.Max(0.0, (bucketEndSeconds - frameTimeGraphSample.Value.TimeSeconds) * 1000.0);
			if (num6 <= 300.0)
			{
				frameTimeGraphLastValidValue = _frameTimeGraphLastValidValue;
				if (frameTimeGraphLastValidValue.HasValue && frameTimeGraphLastValidValue.GetValueOrDefault() > 0.0)
				{
					return new FrameTimeGraphPoint(bucketEndSeconds, _frameTimeGraphLastValidValue, IsSynthetic: true, IsHold: true, IsInProgress: false);
				}
			}
			if (num6 >= 50.0 && num6 <= 1500.0)
			{
				return new FrameTimeGraphPoint(bucketEndSeconds, num6, IsSynthetic: true, IsHold: false, IsInProgress: true);
			}
		}
		frameTimeGraphLastValidValue = _frameTimeGraphLastValidValue;
		if (frameTimeGraphLastValidValue.HasValue && frameTimeGraphLastValidValue.GetValueOrDefault() > 0.0 && _frameTimeGraphLastValidValueTimeSeconds > 0.0 && Math.Max(0.0, (bucketEndSeconds - _frameTimeGraphLastValidValueTimeSeconds) * 1000.0) <= 300.0)
		{
			return new FrameTimeGraphPoint(bucketEndSeconds, _frameTimeGraphLastValidValue, IsSynthetic: true, IsHold: true, IsInProgress: false);
		}
		return new FrameTimeGraphPoint(bucketEndSeconds, null, IsSynthetic: true, IsHold: false, IsInProgress: false);
	}

	private void AppendFrameTimeGraphPoint(FrameTimeGraphPoint point)
	{
		double? valueMs = point.ValueMs;
		double? num = ((valueMs.HasValue && valueMs.GetValueOrDefault() > 0.0 && !double.IsNaN(point.ValueMs.Value) && !double.IsInfinity(point.ValueMs.Value)) ? point.ValueMs : ((double?)null));
		_frameTimeGraphPoints[_frameTimeGraphWriteIndex] = point with
		{
			ValueMs = num
		};
		_frameTimeGraphWriteIndex = (_frameTimeGraphWriteIndex + 1) % 300;
		_frameTimeGraphCount = Math.Min(_frameTimeGraphCount + 1, 300);
		if (num.HasValue && num.GetValueOrDefault() > 0.0 && !point.IsSynthetic)
		{
			_frameTimeGraphLastValidValue = num;
			_frameTimeGraphLastValidValueTimeSeconds = point.TimeSeconds;
		}
	}

	private void ClearFrameTimeGraphSampleBuffer()
	{
		Array.Clear(_frameTimeGraphPoints, 0, _frameTimeGraphPoints.Length);
		_frameTimeGraphWriteIndex = 0;
		_frameTimeGraphCount = 0;
		_frameTimeGraphLastSampleEndSeconds = 0.0;
		_frameTimeGraphLastValidValueTimeSeconds = 0.0;
		_frameTimeGraphLastValidValue = null;
	}

	private IReadOnlyList<FrameTimeGraphPoint> GetFrameTimeGraphDisplayPoints()
	{
		if (_frameTimeGraphCount == 0)
		{
			return Array.Empty<FrameTimeGraphPoint>();
		}
		FrameTimeGraphPoint[] array = new FrameTimeGraphPoint[_frameTimeGraphCount];
		int num = (_frameTimeGraphWriteIndex - _frameTimeGraphCount + 300) % 300;
		for (int i = 0; i < _frameTimeGraphCount; i++)
		{
			int num2 = (num + i) % 300;
			array[i] = _frameTimeGraphPoints[num2];
		}
		return array;
	}

	private void ResetFrameTimeGraphVisualState()
	{
		_lastFrameTimeGraphLogSignature = null;
		_lastFrameTimeGraphRenderTime = TimeSpan.Zero;
		ClearFrameTimeGraphSampleBuffer();
		ClearFrameTimeGraphVisuals();
	}

	private void LogFrameTimeGraphMode(double width)
	{
		string text = $"fixed-samples:{Math.Round(width)}:{300}:{33.333333:0.###}";
		if (!(_lastFrameTimeGraphLogSignature == text))
		{
			_lastFrameTimeGraphLogSignature = text;
			AppLogger.Info("frametime graph mode: fixed graph-sample buffer");
			AppLogger.Info($"graph window ms = {10000.0:0}");
			AppLogger.Info($"graph sample interval ms = {33.333333:0.###}");
			AppLogger.Info($"graph point count = {300}");
			AppLogger.Info($"graph width = {width:0.##}");
		}
	}

	private void MakeWindowClickThrough()
	{
		nint handle = new WindowInteropHelper(this).Handle;
		int windowLong = NativeMethods.GetWindowLong(handle, -20);
		NativeMethods.SetWindowLong(handle, -20, windowLong | 0x20 | 0x80000);
	}

	private void ForceTopmost()
	{
		if (base.IsVisible)
		{
			base.Topmost = true;
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0, 83u);
			}
		}
	}

	private void RegisterHotKeys()
	{
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle == IntPtr.Zero)
		{
			return;
		}
		HwndSource.FromHwnd(handle)?.AddHook(WndProc);
		List<string> failedHotKeys = new List<string>();
		bool flag = RegisterHotKey(handle, 1, _settings.HotKeys.ToggleOverlay);
		if (_settings.HotKeys.ToggleOverlay.Enabled && !flag)
		{
			failedHotKeys.Add("Показать / скрыть оверлей: " + _settings.HotKeys.ToggleOverlay.DisplayText);
		}
		bool flag2 = RegisterHotKey(handle, 2, _settings.HotKeys.ResetStatistics);
		if (_settings.HotKeys.ResetStatistics.Enabled && !flag2)
		{
			failedHotKeys.Add("Сбросить статистику: " + _settings.HotKeys.ResetStatistics.DisplayText);
		}
		bool flag3 = RegisterHotKey(handle, 4, _settings.HotKeys.ToggleOverlayMode);
		if (_settings.HotKeys.ToggleOverlayMode.Enabled && !flag3)
		{
			failedHotKeys.Add("Переключить режим оверлея: " + _settings.HotKeys.ToggleOverlayMode.DisplayText);
		}
		bool flag4 = NativeMethods.RegisterHotKey(handle, 3, 0u, 121u);
		_hotKeysRegistered = flag || flag2 || flag3 || flag4;
		if (failedHotKeys.Count > 0)
		{
			base.Dispatcher.BeginInvoke((Func<AppDialogResult>)(() => AppDialog.Show(this, "Конфликт горячих клавиш", "Некоторые горячие клавиши уже заняты и не были зарегистрированы:\n\n" + string.Join("\n", failedHotKeys), AppDialogKind.Warning)));
		}
	}

	private void UnregisterHotKeys()
	{
		if (_hotKeysRegistered)
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				NativeMethods.UnregisterHotKey(handle, 1);
				NativeMethods.UnregisterHotKey(handle, 2);
				NativeMethods.UnregisterHotKey(handle, 3);
				NativeMethods.UnregisterHotKey(handle, 4);
				HwndSource.FromHwnd(handle)?.RemoveHook(WndProc);
			}
			_hotKeysRegistered = false;
		}
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg != 786)
		{
			return IntPtr.Zero;
		}
		switch (((IntPtr)wParam).ToInt32())
		{
		case 1:
			ToggleOverlayVisibility();
			handled = true;
			break;
		case 2:
			ResetSessionStats();
			handled = true;
			break;
		case 3:
			OpenSettings();
			handled = true;
			break;
		case 4:
			ToggleOverlayDisplayMode();
			handled = true;
			break;
		}
		return IntPtr.Zero;
	}

	private void ToggleOverlayVisibility()
	{
		_settings.OverlayEnabled = !_settings.OverlayEnabled;
		_settingsService.Save(_settings);
		ApplyOverlayVisibility();
		if (ShouldOverlayBeVisible())
		{
			ForceTopmost();
			MakeWindowClickThrough();
		}
	}

	private void ToggleOverlayDisplayMode()
	{
		_settings.OverlayDisplayMode = (IsLineOverlayMode() ? "Normal" : "Line");
		_settingsService.Save(_settings);
		ApplySettings(_settings);
		if (_settingsWindow != null)
		{
			_settingsWindow.RefreshFromSettings(_settings);
		}
	}

	private static bool RegisterHotKey(nint hwnd, int id, HotKeyDefinition hotKey)
	{
		if (!hotKey.Enabled)
		{
			return false;
		}
		uint num = HotKeyMapper.ToVirtualKey(hotKey);
		if (num != 0)
		{
			return NativeMethods.RegisterHotKey(hwnd, id, HotKeyMapper.ToModifiers(hotKey), num);
		}
		return false;
	}

	protected override void OnClosed(EventArgs e)
	{
		AppLogger.BeginShutdown();
		CloseSettingsWindowForShutdown();
		UnregisterHotKeys();
		_fpsTextTimer.Stop();
		_hardwareTimer.Stop();
		_hardwareTextTimer.Stop();
		CompositionTarget.Rendering -= OnFrameTimeGraphRendering;
		_positionTimer.Stop();
		_topmostTimer.Stop();
		_trayIcon.Visible = false;
		_trayIcon.Dispose();
		_fpsMonitor.Dispose();
		_hardwareMonitor?.Dispose();
		base.OnClosed(e);
	}

	private static void SetRowVisibility(TextBlock label, TextBlock value, bool visible)
	{
		Visibility visibility = (label.Visibility = ToVisibility(visible));
		value.Visibility = visibility;
	}

	private static Visibility ToVisibility(bool value)
	{
		if (!value)
		{
			return Visibility.Collapsed;
		}
		return Visibility.Visible;
	}

	private static System.Windows.Media.Brush BrushFromHex(string hex, string fallback)
	{
		return new SolidColorBrush(ParseColor(hex, ParseColor(fallback, Colors.White)));
	}

	private static System.Windows.Media.Color ParseColor(string hex, System.Windows.Media.Color fallback)
	{
		try
		{
			return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
		}
		catch
		{
			return fallback;
		}
	}

	private static string FormatPercent(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() >= 0.0))
		{
			return "N/A";
		}
		return $"{value.Value:0} %";
	}

	private static string FormatRpm(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		return $"{value.Value:0} Об./мин.";
	}

	private string FormatTemperature(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		if (!_settings.TemperatureUnit.Equals("F", StringComparison.OrdinalIgnoreCase))
		{
			return $"{value.Value:0} °C";
		}
		return $"{value.Value * 9.0 / 5.0 + 32.0:0} °F";
	}

	private void UpdateStatisticsLabels()
	{
		StatsGpuMinMaxLabel.Text = FormatStatsLabel("GPU", _settings.Statistics.GpuTemperatureStatsMode);
		StatsCpuMinMaxLabel.Text = FormatStatsLabel("CPU", _settings.Statistics.CpuTemperatureStatsMode);
		StatsVramMinMaxLabel.Text = FormatStatsLabel("VRAM", _settings.Statistics.VramTemperatureStatsMode);
		StatsHotspotMinMaxLabel.Text = FormatStatsLabel("Hotspot", _settings.Statistics.HotspotTemperatureStatsMode);
		StatsGpuVoltageMinMaxLabel.Text = FormatStatsLabel("напр. GPU", _settings.Statistics.GpuVoltageStatsMode);
		StatsGpuPowerMinMaxLabel.Text = FormatStatsLabel("потр. GPU", _settings.Statistics.GpuPowerStatsMode);
		StatsCpuPowerMinMaxLabel.Text = FormatStatsLabel("потр. CPU", _settings.Statistics.CpuPowerStatsMode);
	}

	private static string FormatStatsLabel(string name, string mode)
	{
		string text = StatisticsDisplayModes.Normalize(mode);
		if (!(text == "Average"))
		{
			if (text == "MinAverageMax")
			{
				return "Min/Avg/Max " + name;
			}
			return "Min/Max " + name;
		}
		return "Avg " + name;
	}

	private static string ShortenRamModuleName(string name)
	{
		string text = name.Trim();
		if (text.Length <= 18)
		{
			return text;
		}
		return text.Substring(0, 18) + "...";
	}

	private string FormatTemperatureStats(SessionStat stats, string mode)
	{
		return FormatValueStats(stats, mode, FormatStatsTemperatureValue, GetStatsTemperatureUnit());
	}

	private string FormatStatsTemperatureValue(double value)
	{
		if (!_settings.TemperatureUnit.Equals("F", StringComparison.OrdinalIgnoreCase))
		{
			return $"{value:0}";
		}
		return $"{value * 9.0 / 5.0 + 32.0:0}";
	}

	private string GetStatsTemperatureUnit()
	{
		if (!_settings.TemperatureUnit.Equals("F", StringComparison.OrdinalIgnoreCase))
		{
			return "°C";
		}
		return "°F";
	}

	private static string FormatValueStats(SessionStat stats, string mode, Func<double, string> formatter, string unit)
	{
		if (!stats.HasValue)
		{
			return "N/A";
		}
		string text = StatisticsDisplayModes.Normalize(mode);
		if (!(text == "Average"))
		{
			if (text == "MinAverageMax")
			{
				return $"{formatter(stats.Min)} / {formatter(stats.Average)} / {formatter(stats.Max)} {unit}";
			}
			return $"{formatter(stats.Min)} / {formatter(stats.Max)} {unit}";
		}
		return formatter(stats.Average) + " " + unit;
	}

	private string FormatClock(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		if (!_settings.ClockUnit.Equals("ГГц", StringComparison.OrdinalIgnoreCase) && !_settings.ClockUnit.Equals("GHz", StringComparison.OrdinalIgnoreCase))
		{
			return $"{value.Value:0} МГц";
		}
		return $"{value.Value / 1000.0:0.0} ГГц";
	}

	private static string FormatMemoryClock(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		return $"{value.Value:0} МГц";
	}

	private static string FormatVoltage(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		return $"{value.Value:0.000} В";
	}

	private static string FormatPower(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		return $"{value.Value:0.0} Вт";
	}

	private static string FormatRamUsed(double usedGb, double totalGb, double loadPercent, RamSettings settings, string memoryUnit)
	{
		if (settings.MemoryFormat == "UsedOnly")
		{
			return FormatRamAmount(usedGb, memoryUnit);
		}
		return $"{FormatRamAmountValue(usedGb, memoryUnit)} / {FormatRamAmountValue(totalGb, memoryUnit)} {GetMemoryUnit(memoryUnit)}";
	}

	private static string FormatRamAmount(double valueGb, string unit)
	{
		return FormatRamAmountValue(valueGb, unit) + " " + GetMemoryUnit(unit);
	}

	private static string FormatRamAmountValue(double valueGb, string unit)
	{
		if (!(unit == "МБ") && !unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
		{
			return $"{valueGb:0.0}";
		}
		return $"{valueGb * 1024.0:0}";
	}

	private static string GetMemoryUnit(string unit)
	{
		if (!(unit == "МБ") && !unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
		{
			return "ГБ";
		}
		return "МБ";
	}

	private System.Windows.Media.Brush GetTemperatureBrush(double? value, HardwareBlockSettings settings)
	{
		if (IsFreeVersion)
		{
			return FreeVersionTextBrush;
		}
		if (value >= (double)settings.CriticalTemperatureC)
		{
			return HotTemperatureBrush;
		}
		if (value >= (double)settings.WarningTemperatureC)
		{
			return WarmTemperatureBrush;
		}
		return BrushFromHex(settings.ValueColor, "#FFF2F2F2");
	}

	private System.Windows.Media.Brush GetLoadBrush(double? value, HardwareBlockSettings settings, bool isGpu)
	{
		if (IsFreeVersion)
		{
			return FreeVersionTextBrush;
		}
		if (!settings.UseLoadGradient || !value.HasValue || !(value.GetValueOrDefault() >= 0.0))
		{
			return BrushFromHex(settings.ValueColor, "#FFF2F2F2");
		}
		System.Windows.Media.Color target = (isGpu ? System.Windows.Media.Color.FromRgb(53, 212, 99) : System.Windows.Media.Color.FromRgb(byte.MaxValue, 76, 76));
		return new SolidColorBrush(InterpolateLoadColor(value.Value, target));
	}

	private static System.Windows.Media.Color InterpolateLoadColor(double loadPercent, System.Windows.Media.Color target)
	{
		System.Windows.Media.Color color = System.Windows.Media.Color.FromRgb(242, 242, 242);
		double num = Math.Clamp((loadPercent - 50.0) / 50.0, 0.0, 1.0);
		return System.Windows.Media.Color.FromRgb((byte)Math.Round((double)(int)color.R + (double)(target.R - color.R) * num), (byte)Math.Round((double)(int)color.G + (double)(target.G - color.G) * num), (byte)Math.Round((double)(int)color.B + (double)(target.B - color.B) * num));
	}

	private static string FormatVram(double? usedGb, double? totalGb, double? loadPercent, string memoryUnit)
	{
		if (usedGb.HasValue && usedGb.GetValueOrDefault() > 0.0 && totalGb.HasValue && totalGb.GetValueOrDefault() > 0.0)
		{
			return $"{FormatRamAmountValue(usedGb.Value, memoryUnit)} / {FormatRamAmountValue(totalGb.Value, memoryUnit)} {GetMemoryUnit(memoryUnit)}";
		}
		if (usedGb.HasValue && usedGb.GetValueOrDefault() > 0.0)
		{
			return FormatRamAmount(usedGb.Value, memoryUnit);
		}
		if (loadPercent.HasValue && loadPercent.GetValueOrDefault() >= 0.0)
		{
			return $"{loadPercent.Value:0} %";
		}
		return "N/A";
	}

	private static string GetHardwareDisplayName(HardwareBlockSettings settings, string fallbackName)
	{
		if (!string.IsNullOrWhiteSpace(settings.CustomName))
		{
			return settings.CustomName.Trim();
		}
		return fallbackName;
	}
}
