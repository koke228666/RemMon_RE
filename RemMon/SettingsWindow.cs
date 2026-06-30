using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace RemMon;

public partial class SettingsWindow : Window, IComponentConnector
{
	private struct MinMaxInfo
	{
		public NativePoint ptReserved;

		public NativePoint ptMaxSize;

		public NativePoint ptMaxPosition;

		public NativePoint ptMinTrackSize;

		public NativePoint ptMaxTrackSize;
	}

	private struct NativePoint
	{
		public int X;

		public int Y;
	}

	private sealed record DisplayOption(string Value, string Label);

	private sealed record SettingsThemePalette(string Window, string Bar, string Sidebar, string Card, string Hint);

	private const int WmGetMinMaxInfo = 36;

	private static readonly Color SettingsOptionTextColor = Color.FromRgb(232, 240, 250);

	private static readonly Color SettingsMutedTextColor = Color.FromRgb(184, 196, 212);

	private readonly MainWindow _mainWindow;

	private readonly SettingsService _settingsService;

	private readonly UpdateService _updateService = new UpdateService();

	private readonly LicenseService _licenseService;

	private OverlaySettings _draft;

	private bool _building;

	private UpdateCheckResult? _lastUpdateResult;

	private string _updateStatusText = "Нажмите «Проверить обновления», чтобы узнать, доступна ли новая версия.";

	private bool _updateCheckInProgress;

	private bool _updateDownloadInProgress;

	private double? _updateDownloadProgress;

	private string? _downloadedUpdatePath;

	private string _fontDownloadUrl = string.Empty;

	private string _fontStatusText = string.Empty;

	private static readonly DisplayOption[] PositionOptions = new DisplayOption[9]
	{
		new DisplayOption("TopLeft", "Сверху слева"),
		new DisplayOption("TopCenter", "Сверху по центру"),
		new DisplayOption("TopRight", "Сверху справа"),
		new DisplayOption("MiddleLeft", "По центру слева"),
		new DisplayOption("MiddleCenter", "По центру"),
		new DisplayOption("MiddleRight", "По центру справа"),
		new DisplayOption("BottomLeft", "Снизу слева"),
		new DisplayOption("BottomCenter", "Снизу по центру"),
		new DisplayOption("BottomRight", "Снизу справа")
	};

	private static readonly DisplayOption[] AnchorTargetOptions = new DisplayOption[3]
	{
		new DisplayOption("PrimaryMonitor", "Основной монитор"),
		new DisplayOption("ActiveMonitor", "Активный монитор"),
		new DisplayOption("ActiveWindow", "Активное окно")
	};

	private static readonly DisplayOption[] MemoryFormatOptions = new DisplayOption[2]
	{
		new DisplayOption("UsedTotal", "Использовано / всего"),
		new DisplayOption("UsedOnly", "Только использовано")
	};

	private static readonly DisplayOption[] StatisticsModeOptions = new DisplayOption[3]
	{
		new DisplayOption("MinMax", "Min/Max"),
		new DisplayOption("Average", "Avg"),
		new DisplayOption("MinAverageMax", "Min/Avg/Max")
	};

	private static readonly DisplayOption[] RamUnitOptions = new DisplayOption[2]
	{
		new DisplayOption("ГБ", "ГБ"),
		new DisplayOption("МБ", "МБ")
	};

	private static readonly DisplayOption[] UpdateIntervalOptions = new DisplayOption[4]
	{
		new DisplayOption("100", "100 мс"),
		new DisplayOption("250", "250 мс"),
		new DisplayOption("500", "500 мс"),
		new DisplayOption("1000", "1000 мс")
	};

	private static readonly DisplayOption[] OverlayDisplayModeOptions = new DisplayOption[2]
	{
		new DisplayOption("Normal", "Полноразмерный"),
		new DisplayOption("Line", "Строка")
	};

	private static readonly DisplayOption[] ThemeOptions = new DisplayOption[7]
	{
		new DisplayOption("Dark", "Тёмная"),
		new DisplayOption("Darker", "Глубокая тёмная"),
		new DisplayOption("Graphite", "Графит"),
		new DisplayOption("Midnight", "Полночь"),
		new DisplayOption("Emerald", "Изумруд"),
		new DisplayOption("Sapphire", "Сапфир"),
		new DisplayOption("Amber", "Янтарная")
	};

	private static readonly DisplayOption[] TemperatureUnitOptions = new DisplayOption[2]
	{
		new DisplayOption("C", "°C (Цельсий)"),
		new DisplayOption("F", "°F (Фаренгейт)")
	};

	private static readonly DisplayOption[] ClockUnitOptions = new DisplayOption[2]
	{
		new DisplayOption("МГц", "МГц"),
		new DisplayOption("ГГц", "ГГц")
	};

	private static readonly string[] HotKeyOptions = new string[18]
	{
		"F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10",
		"F11", "F12", "Home", "Insert", "Page Up", "Page Down", "End", "Del"
	};

	private static readonly DisplayOption[] BasicColorOptions = new DisplayOption[11]
	{
		new DisplayOption("#FFF2F2F2", "Белый"),
		new DisplayOption("#FFB8C4D4", "Серый"),
		new DisplayOption("#FFFF0000", "Красный"),
		new DisplayOption("#FFFF6A00", "Оранжевый"),
		new DisplayOption("#FFFFE066", "Жёлтый"),
		new DisplayOption("#FF35D463", "Зелёный"),
		new DisplayOption("#FF58A6FF", "Голубой"),
		new DisplayOption("#FF1677FF", "Синий"),
		new DisplayOption("#FFB58CFF", "Фиолетовый"),
		new DisplayOption("#00000000", "Прозрачный"),
		new DisplayOption("#FF000000", "Чёрный")
	};

	private static readonly DisplayOption[] BlockOrderOptions = new DisplayOption[6]
	{
		new DisplayOption("Fps", "FPS"),
		new DisplayOption("Gpu", "Видеокарта"),
		new DisplayOption("Cpu", "Процессор"),
		new DisplayOption("Ram", "ОЗУ"),
		new DisplayOption("Statistics", "Статистика"),
		new DisplayOption("FrameTimeGraph", "График времени кадра")
	};

	private bool IsPremium => _mainWindow.IsPremium;

	private bool IsFreeVersion => !IsPremium;

	internal SettingsWindow(MainWindow mainWindow, SettingsService settingsService, OverlaySettings settings, LicenseService licenseService)
	{
		InitializeComponent();
		_mainWindow = mainWindow;
		_settingsService = settingsService;
		_licenseService = licenseService;
		_draft = settings.Clone();
		if (!string.IsNullOrWhiteSpace(_mainWindow.LastUpdateStatusText))
		{
			_updateStatusText = _mainWindow.LastUpdateStatusText;
		}
		base.Width = _draft.SettingsWindowWidth;
		base.Height = _draft.SettingsWindowHeight;
		if (IsSavedWindowPlacementUsable(_draft.SettingsWindowLeft, _draft.SettingsWindowTop, base.Width, base.Height))
		{
			base.Left = _draft.SettingsWindowLeft;
			base.Top = _draft.SettingsWindowTop;
		}
		else
		{
			base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		}
		ApplySettingsTheme();
		UpdateLicenseUiState();
		SectionList.SelectedIndex = 0;
		UpdatePreview();
		base.SourceInitialized += SettingsWindow_SourceInitialized;
		base.StateChanged += delegate
		{
			UpdateMaximizeButtonText();
		};
		base.StateChanged += SettingsWindow_StateChanged;
	}

	private void SettingsWindow_StateChanged(object? sender, EventArgs e)
	{
		if (base.WindowState == WindowState.Minimized)
		{
			base.WindowState = WindowState.Normal;
			Hide();
		}
	}

	public void RefreshFromSettings(OverlaySettings settings)
	{
		_draft = settings.Clone();
		BuildCurrentSection();
		UpdatePreview();
	}

	public void RefreshLicenseState()
	{
		UpdateLicenseUiState();
		BuildCurrentSection();
		UpdatePreview();
	}

	private void UpdateLicenseUiState()
	{
		FreeVersionText.Text = _mainWindow.FreeVersionPhrase;
		FreeVersionText.Visibility = ToVisibility(IsFreeVersion);
	}

	private void SettingsWindow_SourceInitialized(object? sender, EventArgs e)
	{
		HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProc);
	}

	private nint WindowProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg == 36)
		{
			ApplyMonitorWorkAreaForMaximize(hwnd, lParam);
		}
		return IntPtr.Zero;
	}

	private static void ApplyMonitorWorkAreaForMaximize(nint hwnd, nint lParam)
	{
		if (hwnd == IntPtr.Zero || lParam == IntPtr.Zero)
		{
			return;
		}
		nint num = NativeMethods.MonitorFromWindow(hwnd, 2u);
		if (num != IntPtr.Zero)
		{
			NativeMethods.MonitorInfo lpmi = new NativeMethods.MonitorInfo
			{
				cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>()
			};
			if (NativeMethods.GetMonitorInfo(num, ref lpmi))
			{
				MinMaxInfo structure = Marshal.PtrToStructure<MinMaxInfo>(lParam);
				NativeMethods.Rect rcWork = lpmi.rcWork;
				NativeMethods.Rect rcMonitor = lpmi.rcMonitor;
				structure.ptMaxPosition.X = rcWork.Left - rcMonitor.Left;
				structure.ptMaxPosition.Y = rcWork.Top - rcMonitor.Top;
				structure.ptMaxSize.X = rcWork.Width;
				structure.ptMaxSize.Y = rcWork.Height;
				structure.ptMaxTrackSize.X = rcWork.Width;
				structure.ptMaxTrackSize.Y = rcWork.Height;
				Marshal.StructureToPtr(structure, lParam, fDeleteOld: true);
			}
		}
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		SaveWindowGeometry();
		base.OnClosing(e);
	}

	private void SaveWindowGeometry()
	{
		Rect rect = ((base.WindowState == WindowState.Normal) ? new Rect(base.Left, base.Top, (base.ActualWidth > 0.0) ? base.ActualWidth : base.Width, (base.ActualHeight > 0.0) ? base.ActualHeight : base.Height) : base.RestoreBounds);
		if (!(rect.Width <= 0.0) && !(rect.Height <= 0.0) && !double.IsNaN(rect.Left) && !double.IsNaN(rect.Top) && !double.IsInfinity(rect.Left) && !double.IsInfinity(rect.Top))
		{
			_draft.SettingsWindowWidth = rect.Width;
			_draft.SettingsWindowHeight = rect.Height;
			_draft.SettingsWindowLeft = rect.Left;
			_draft.SettingsWindowTop = rect.Top;
			_mainWindow.SaveSettingsWindowGeometry(rect.Width, rect.Height, rect.Left, rect.Top);
		}
	}

	private static bool IsSavedWindowPlacementUsable(double left, double top, double width, double height)
	{
		if (left < 0.0 || top < 0.0 || width <= 0.0 || height <= 0.0)
		{
			return false;
		}
		if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(width) || double.IsNaN(height))
		{
			return false;
		}
		if (double.IsInfinity(left) || double.IsInfinity(top) || double.IsInfinity(width) || double.IsInfinity(height))
		{
			return false;
		}
		Rect rect = new Rect(left, top, width, height);
		Rect rect2 = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
		if (rect2.IntersectsWith(rect) && rect.Right > rect2.Left + 40.0 && rect.Bottom > rect2.Top + 40.0 && rect.Left < rect2.Right - 40.0)
		{
			return rect.Top < rect2.Bottom - 40.0;
		}
		return false;
	}

	private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (SectionList.SelectedItem is ListBoxItem listBoxItem)
		{
			BuildSection(listBoxItem.Content?.ToString() ?? "Общие");
		}
	}

	private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount == 2)
		{
			ToggleWindowState();
		}
		else if (e.ButtonState == MouseButtonState.Pressed)
		{
			try
			{
				DragMove();
			}
			catch
			{
			}
		}
	}

	private void MinimizeButton_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = WindowState.Minimized;
	}

	private void MaximizeButton_Click(object sender, RoutedEventArgs e)
	{
		ToggleWindowState();
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void ToggleWindowState()
	{
		base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
		base.Dispatcher.BeginInvoke(new Action(UpdateMaximizeButtonText), DispatcherPriority.Background);
	}

	private void UpdateMaximizeButtonText()
	{
		MaximizeButton.Content = ((base.WindowState == WindowState.Maximized) ? "❐" : "□");
	}

	private void ApplyButton_Click(object sender, RoutedEventArgs e)
	{
		if (!_mainWindow.CurrentSettings.Statistics.DiagnosticLoggingEnabled && _draft.Statistics.DiagnosticLoggingEnabled)
		{
			if (AppDialog.ShowLoggingRestartWarning(this) != AppDialogResult.Yes)
			{
				_draft.Statistics.DiagnosticLoggingEnabled = false;
				BuildCurrentSection();
				UpdatePreview();
			}
			else
			{
				_mainWindow.SaveSettingsAndRestart(_draft);
			}
		}
		else
		{
			_mainWindow.SaveAndApplySettings(_draft);
			_draft = _mainWindow.CurrentSettings;
			ApplySettingsTheme();
			BuildCurrentSection();
			UpdatePreview();
			FlashFooterButton((sender as Button) ?? ApplyButton, "Применить", "Применено");
		}
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		_draft = _mainWindow.CurrentSettings;
		ApplySettingsTheme();
		BuildCurrentSection();
		UpdatePreview();
		FlashFooterButton((sender as Button) ?? CancelButton, "Отмена", "Отменено");
	}

	private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
	{
		_draft = _settingsService.CreateDefaultSettings();
		ApplySettingsTheme();
		BuildCurrentSection();
		UpdatePreview();
	}

	private void SaveSettingsFileButton_Click(object sender, RoutedEventArgs e)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "Сохранить настройки",
			Filter = "Файл настроек RemMon (*.json)|*.json|Все файлы (*.*)|*.*",
			FileName = "RemMon-settings.json",
			AddExtension = true,
			DefaultExt = ".json"
		};
		if (saveFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			_settingsService.ExportToFile(_draft, saveFileDialog.FileName);
			AppDialog.Show(this, "Сохранение настроек", "Настройки сохранены.", AppDialogKind.Info);
		}
		catch (Exception ex)
		{
			AppDialog.Show(this, "Сохранение настроек", "Не удалось сохранить настройки:\n" + ex.Message, AppDialogKind.Error);
		}
	}

	private void LoadSettingsFileButton_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Загрузить настройки",
			Filter = "Файл настроек RemMon (*.json)|*.json|Все файлы (*.*)|*.*"
		};
		if (openFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			ImportedSettingsResult importedSettingsResult = _settingsService.ImportFromFile(openFileDialog.FileName);
			_draft = importedSettingsResult.Settings;
			ApplySettingsTheme();
			BuildCurrentSection();
			UpdatePreview();
			if (importedSettingsResult.HasSchemaDifferences)
			{
				AppDialog.Show(this, "Загрузка настроек", "Файл настроек отличается от текущей версии приложения. Лишние параметры были проигнорированы, отсутствующие заменены значениями по умолчанию.", AppDialogKind.Warning);
			}
			else
			{
				AppDialog.Show(this, "Загрузка настроек", "Настройки загружены. Нажмите «Применить», чтобы использовать их в оверлее.", AppDialogKind.Info);
			}
		}
		catch (Exception ex)
		{
			AppDialog.Show(this, "Загрузка настроек", "Не удалось загрузить настройки:\n" + ex.Message, AppDialogKind.Error);
		}
	}

	private void BuildCurrentSection()
	{
		if (SectionList.SelectedItem is ListBoxItem listBoxItem)
		{
			BuildSection(listBoxItem.Content?.ToString() ?? "Общие");
		}
	}

	private void FlashFooterButton(Button button, string defaultText, string feedbackText)
	{
		button.Content = feedbackText;
		button.Opacity = 0.86;
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(750L)
		};
		timer.Tick += delegate
		{
			timer.Stop();
			button.Content = defaultText;
			button.Opacity = 1.0;
		};
		timer.Start();
	}

	private bool IsLineOverlayMode()
	{
		return _draft.OverlayDisplayMode.Equals("Line", StringComparison.OrdinalIgnoreCase);
	}

	private UIElement DisableWhenLineMode(UIElement element)
	{
		if (IsLineOverlayMode())
		{
			element.IsEnabled = false;
			element.Opacity = 0.55;
		}
		return element;
	}

	private static UIElement DisableWhen(bool disabled, UIElement element)
	{
		if (disabled)
		{
			element.IsEnabled = false;
			element.Opacity = 0.55;
		}
		return element;
	}

	private void BuildSection(string section)
	{
		_building = true;
		ContentPanel.Children.Clear();
		ContentPanel.Children.Add(Header(section));
		switch (section)
		{
		case "Общие":
			BuildGeneral();
			break;
		case "FPS":
			BuildFps();
			break;
		case "Видеокарта":
			BuildHardware(_draft.Gpu, isGpu: true);
			break;
		case "Процессор":
			BuildHardware(_draft.Cpu, isGpu: false);
			break;
		case "ОЗУ":
			BuildRam();
			break;
		case "Статистика":
			BuildStatistics();
			break;
		case "График времени кадра":
			BuildGraph();
			break;
		case "Внешний вид":
			BuildAppearance();
			break;
		case "Горячие клавиши":
			BuildHotKeys();
			break;
		case "Активация":
			BuildActivation();
			break;
		case "Обновления":
			BuildUpdates();
			break;
		case "Оставить отзыв":
			BuildFeedback();
			break;
		}
		_building = false;
	}

	private void BuildGeneral()
	{
		AddCard("Основные параметры", Check("Включить оверлей", _draft.OverlayEnabled, delegate(bool v)
		{
			_draft.OverlayEnabled = v;
		}), Check("Запускать вместе с Windows", _draft.StartWithWindows, delegate(bool v)
		{
			_draft.StartWithWindows = v;
		}), Check("Показывать время", _draft.ShowTime, delegate(bool v)
		{
			_draft.ShowTime = v;
		}), CheckWithHelp("Режим «Только игра»", _draft.GameOnlyMode, delegate(bool v)
		{
			_draft.GameOnlyMode = v;
		}, "Скрывает оверлей, если нет активной запущенной игры или нет показателей FPS."), Check("Разделитель", _draft.Appearance.ShowSeparators, delegate(bool v)
		{
			_draft.Appearance.ShowSeparators = v;
		}), ComboDisplayWithHelp("Режим оверлея", "Режим «Строка» заменяет обычные блоки компактной строкой.", _draft.OverlayDisplayMode, OverlayDisplayModeOptions, delegate(string v)
		{
			_draft.OverlayDisplayMode = v;
			BuildCurrentSection();
		}), DisableWhenLineMode(PercentNumber("Ширина оверлея", _draft.OverlayWidthPercent / 100.0, 0.5, 1.0, 0.05, delegate(double v)
		{
			_draft.OverlayWidthPercent = v * 100.0;
		})), ComboDisplay("Интервал обновления", _draft.Fps.UpdateIntervalMs.ToString(), UpdateIntervalOptions, delegate(string v)
		{
			_draft.Fps.UpdateIntervalMs = int.Parse(v);
		}), ComboDisplay("Температура", _draft.TemperatureUnit, TemperatureUnitOptions, delegate(string v)
		{
			_draft.TemperatureUnit = v;
		}), ComboDisplay("Частота", _draft.ClockUnit, ClockUnitOptions, delegate(string v)
		{
			_draft.ClockUnit = v;
		}), ComboDisplay("Единицы памяти", _draft.MemoryUnit, RamUnitOptions, delegate(string v)
		{
			_draft.MemoryUnit = v;
		}), ComboDisplay("Привязка оверлея", _draft.AnchorTarget, AnchorTargetOptions, delegate(string v)
		{
			_draft.AnchorTarget = v;
		}), ComboDisplay("Позиция", _draft.Position, PositionOptions, delegate(string v)
		{
			_draft.Position = v;
		}), Number("Отступ X", _draft.OffsetX, 0, 500, delegate(int v)
		{
			_draft.OffsetX = v;
		}), Number("Отступ Y", _draft.OffsetY, 0, 500, delegate(int v)
		{
			_draft.OffsetY = v;
		}));
		UIElement uIElement = DisableWhenLineMode(BuildBlockOrder());
		if (IsLineOverlayMode())
		{
			AddCard("Порядок блоков", uIElement, TextNote("В режиме «Строка» порядок блоков недоступен."));
		}
		else
		{
			AddCard("Порядок блоков", uIElement);
		}
	}

	private void BuildFps()
	{
		bool flag = IsLineOverlayMode();
		AddCard("Отображать показатели", Check("Показывать FPS", _draft.Fps.ShowFps, delegate(bool v)
		{
			_draft.Fps.ShowFps = v;
		}), Check("AVG", _draft.Fps.ShowAverage, delegate(bool v)
		{
			_draft.Fps.ShowAverage = v;
		}), Check("1% Low", _draft.Fps.ShowOnePercentLow, delegate(bool v)
		{
			_draft.Fps.ShowOnePercentLow = v;
		}), Check("0.1% Low", _draft.Fps.ShowPointOnePercentLow, delegate(bool v)
		{
			_draft.Fps.ShowPointOnePercentLow = v;
		}), Check("Окно", _draft.Fps.ShowGame, delegate(bool v)
		{
			_draft.Fps.ShowGame = v;
		}, !flag), Check("API", _draft.Fps.ShowApi, delegate(bool v)
		{
			_draft.Fps.ShowApi = v;
		}, !flag), Check("Скрывать показатели, если FPS = N/A", _draft.Fps.HideUnavailableFpsMetrics, delegate(bool v)
		{
			_draft.Fps.HideUnavailableFpsMetrics = v;
		}));
		AddPremiumCard("Внешний вид", TextValue("Цвет текста", _draft.Fps.TextColor, delegate(string v)
		{
			_draft.Fps.TextColor = v;
		}), TextValue("Цвет значений", _draft.Fps.ValueColor, delegate(string v)
		{
			_draft.Fps.ValueColor = v;
		}));
	}

	private void BuildHardware(HardwareBlockSettings block, bool isGpu)
	{
		string text = (isGpu ? "Видеокарта" : "Процессор");
		bool flag = IsLineOverlayMode();
		List<UIElement> list = new List<UIElement>
		{
			Check("Показывать блок «" + text + "»", block.ShowBlock, delegate(bool v)
			{
				block.ShowBlock = v;
			}, enabled: true, refreshSectionOnChange: true),
			Check("Загрузка", block.ShowLoad, delegate(bool v)
			{
				block.ShowLoad = v;
			}, block.ShowBlock),
			Check("Температура", block.ShowTemperature, delegate(bool v)
			{
				block.ShowTemperature = v;
			}, block.ShowBlock),
			Check("Частота", block.ShowClock, delegate(bool v)
			{
				block.ShowClock = v;
			}, block.ShowBlock, !isGpu),
			Check("Потребление", block.ShowPower, delegate(bool v)
			{
				block.ShowPower = v;
			}, block.ShowBlock)
		};
		if (isGpu)
		{
			list.Insert(4, Check("Напряжение", block.ShowVoltage, delegate(bool v)
			{
				block.ShowVoltage = v;
			}, block.ShowBlock));
			list.Add(Check("Обороты вент.", block.ShowFanRpm, delegate(bool v)
			{
				block.ShowFanRpm = v;
			}, block.ShowBlock));
			list.Add(Check("Обороты вент. в %", block.ShowFanPercent, delegate(bool v)
			{
				block.ShowFanPercent = v;
			}, block.ShowBlock));
			list.Add(Check("Темп. горячей точки", block.ShowHotspotTemperature, delegate(bool v)
			{
				block.ShowHotspotTemperature = v;
			}, block.ShowBlock));
			list.Add(Check("Температура памяти", block.ShowVramTemperature, delegate(bool v)
			{
				block.ShowVramTemperature = v;
			}, block.ShowBlock));
			list.Add(Check("Память GPU", block.ShowMemoryClock, delegate(bool v)
			{
				block.ShowMemoryClock = v;
			}, block.ShowBlock));
			list.Add(Check("Видеопамять", block.ShowVram, delegate(bool v)
			{
				block.ShowVram = v;
			}, block.ShowBlock));
		}
		else
		{
			list.Add(Check("Показывать частоту E ядер", block.ShowEfficiencyAverageClock, delegate(bool v)
			{
				block.ShowEfficiencyAverageClock = v;
			}, block.ShowBlock && block.ShowClock));
			list.Add(Check("График загрузки по ядрам", block.ShowCoreLoadGraph, delegate(bool v)
			{
				ApplyCpuCoreLoadGraphToggle(block, v);
			}, block.ShowBlock && !flag, refreshSectionOnChange: true));
			list.Add(Check("Подписи под графиком", block.ShowCoreLoadGraphLabels, delegate(bool v)
			{
				block.ShowCoreLoadGraphLabels = v;
			}, block.ShowBlock && block.ShowCoreLoadGraph && !flag));
			list.Add(Check("Частоты по ядрам", block.ShowCoreClocks, delegate(bool v)
			{
				ApplyCpuCoreClockParentToggle(block, v);
			}, block.ShowBlock && !flag, refreshSectionOnChange: true));
			list.Add(Check("P-ядра", block.ShowPerformanceCoreClocks, delegate(bool v)
			{
				ApplyCpuCoreClockChildToggle(block, v);
			}, block.ShowBlock && block.ShowCoreClocks && !flag, refreshSectionOnChange: true));
			list.Add(Check("E-ядра", block.ShowEfficiencyCoreClocks, delegate(bool v)
			{
				HardwareBlockSettings block2 = block;
				bool? efficiency = v;
				ApplyCpuCoreClockChildToggle(block2, null, efficiency);
			}, block.ShowBlock && block.ShowCoreClocks && !flag, refreshSectionOnChange: true));
		}
		AddCard("Показатели", list.ToArray());
		if (isGpu)
		{
			AddCard("Внешний вид", DisableWhenFreeVersion(TextValue("Цвет заголовка", block.TitleColor, delegate(string v)
			{
				block.TitleColor = v;
			})), DisableWhenFreeVersion(DisableWhenLineMode(TextValue("Цвет подписей", block.LabelColor, delegate(string v)
			{
				block.LabelColor = v;
			}))), DisableWhenFreeVersion(TextValue("Цвет значений", block.ValueColor, delegate(string v)
			{
				block.ValueColor = v;
			})), TextInputRow("Пользовательское название", GetHardwareNameText(block, isGpu), delegate(string v)
			{
				block.CustomName = NormalizeCustomHardwareName(v, isGpu);
			}), FreeVersionNote());
		}
		else
		{
			AddCard("Внешний вид", DisableWhenFreeVersion(TextValue("Цвет заголовка", block.TitleColor, delegate(string v)
			{
				block.TitleColor = v;
			})), DisableWhenFreeVersion(DisableWhenLineMode(TextValue("Цвет подписей", block.LabelColor, delegate(string v)
			{
				block.LabelColor = v;
			}))), DisableWhenFreeVersion(TextValue("Цвет значений", block.ValueColor, delegate(string v)
			{
				block.ValueColor = v;
			})), DisableWhenFreeVersion(DisableWhenLineMode(TextValue("Цвет графика", block.CoreLoadGraphColor, delegate(string v)
			{
				block.CoreLoadGraphColor = v;
			}))), TextInputRow("Пользовательское название", GetHardwareNameText(block, isGpu), delegate(string v)
			{
				block.CustomName = NormalizeCustomHardwareName(v, isGpu);
			}), FreeVersionNote());
		}
		AddCard("Пороги предупреждений", CheckWithHelp(isGpu ? "Цветовая индикация загрузки GPU" : "Цветовая индикация загрузки CPU", block.UseLoadGradient, delegate(bool v)
		{
			block.UseLoadGradient = v;
		}, isGpu ? "Окрашивает процент загрузки видеокарты градиентом: 50% — белый, 100% — зелёный. Имеет приоритет над цветом значений." : "Окрашивает процент загрузки процессора градиентом: 50% — белый, 100% — красный. Имеет приоритет над цветом значений."), NumberWithHelp("Температура предупреждения", "Когда температура станет равна этому значению или выше, значение температуры в оверлее станет оранжевым.", block.WarningTemperatureC, 40, 110, delegate(int v)
		{
			block.WarningTemperatureC = v;
		}), NumberWithHelp("Температура критическая", "Когда температура станет равна этому значению или выше, значение температуры в оверлее станет красным.", block.CriticalTemperatureC, 50, 120, delegate(int v)
		{
			block.CriticalTemperatureC = v;
		}));
	}

	private string GetHardwareNameText(HardwareBlockSettings block, bool isGpu)
	{
		if (!string.IsNullOrWhiteSpace(block.CustomName))
		{
			return block.CustomName;
		}
		return _mainWindow.GetCurrentHardwareName(isGpu);
	}

	private string NormalizeCustomHardwareName(string value, bool isGpu)
	{
		string text = value.Trim();
		string currentHardwareName = _mainWindow.GetCurrentHardwareName(isGpu);
		if (!text.Equals(currentHardwareName, StringComparison.Ordinal))
		{
			return text;
		}
		return string.Empty;
	}

	private void BuildRam()
	{
		AddCard("Основные параметры", Check("Показывать блок ОЗУ", _draft.Ram.ShowBlock, delegate(bool v)
		{
			_draft.Ram.ShowBlock = v;
		}, enabled: true, refreshSectionOnChange: true), Check("Использовано", _draft.Ram.ShowUsed, delegate(bool v)
		{
			_draft.Ram.ShowUsed = v;
		}, _draft.Ram.ShowBlock), Check("Загрузка", _draft.Ram.ShowLoad, delegate(bool v)
		{
			_draft.Ram.ShowLoad = v;
		}, _draft.Ram.ShowBlock), Check("Показывать скорость в мегагерцах", _draft.Ram.ShowSpeed, delegate(bool v)
		{
			_draft.Ram.ShowSpeed = v;
		}, _draft.Ram.ShowBlock), CheckWithHelp("Температуры плашек ОЗУ", _draft.Ram.ShowTemperatures, delegate(bool v)
		{
			_draft.Ram.ShowTemperatures = v;
		}, "Показывает температуру каждого модуля памяти, если этот датчик доступен в системе.", _draft.Ram.ShowBlock), ComboDisplay("Формат памяти", _draft.Ram.MemoryFormat, MemoryFormatOptions, delegate(string v)
		{
			_draft.Ram.MemoryFormat = v;
		}, 184.0));
		AddPremiumCard("Внешний вид", TextValue("Цвет заголовка", _draft.Ram.TitleColor, delegate(string v)
		{
			_draft.Ram.TitleColor = v;
		}), DisableWhenLineMode(TextValue("Цвет подписей", _draft.Ram.LabelColor, delegate(string v)
		{
			_draft.Ram.LabelColor = v;
		})), TextValue("Цвет значений", _draft.Ram.ValueColor, delegate(string v)
		{
			_draft.Ram.ValueColor = v;
		}));
	}

	private void BuildStatistics()
	{
		bool flag = IsLineOverlayMode();
		AddCard("Состав блока", Check("Показывать блок статистики", _draft.Statistics.ShowBlock, delegate(bool v)
		{
			_draft.Statistics.ShowBlock = v;
		}, !flag, refreshSectionOnChange: true), StatisticsMetricRow("Температура GPU", _draft.Statistics.ShowGpuMinMax, delegate(bool v)
		{
			_draft.Statistics.ShowGpuMinMax = v;
		}, _draft.Statistics.GpuTemperatureStatsMode, delegate(string v)
		{
			_draft.Statistics.GpuTemperatureStatsMode = v;
		}), StatisticsMetricRow("Температура CPU", _draft.Statistics.ShowCpuMinMax, delegate(bool v)
		{
			_draft.Statistics.ShowCpuMinMax = v;
		}, _draft.Statistics.CpuTemperatureStatsMode, delegate(string v)
		{
			_draft.Statistics.CpuTemperatureStatsMode = v;
		}), StatisticsMetricRow("Температура VRAM", _draft.Statistics.ShowVramMinMax, delegate(bool v)
		{
			_draft.Statistics.ShowVramMinMax = v;
		}, _draft.Statistics.VramTemperatureStatsMode, delegate(string v)
		{
			_draft.Statistics.VramTemperatureStatsMode = v;
		}), StatisticsMetricRow("Hotspot", _draft.Statistics.ShowHotspotMinMax, delegate(bool v)
		{
			_draft.Statistics.ShowHotspotMinMax = v;
		}, _draft.Statistics.HotspotTemperatureStatsMode, delegate(string v)
		{
			_draft.Statistics.HotspotTemperatureStatsMode = v;
		}), StatisticsMetricRow("Напряжение GPU", _draft.Statistics.ShowGpuVoltageMinMax, delegate(bool v)
		{
			_draft.Statistics.ShowGpuVoltageMinMax = v;
		}, _draft.Statistics.GpuVoltageStatsMode, delegate(string v)
		{
			_draft.Statistics.GpuVoltageStatsMode = v;
		}), StatisticsMetricRow("Потребление GPU", _draft.Statistics.ShowGpuPowerMinMax, delegate(bool v)
		{
			_draft.Statistics.ShowGpuPowerMinMax = v;
		}, _draft.Statistics.GpuPowerStatsMode, delegate(string v)
		{
			_draft.Statistics.GpuPowerStatsMode = v;
		}), StatisticsMetricRow("Потребление CPU", _draft.Statistics.ShowCpuPowerMinMax, delegate(bool v)
		{
			_draft.Statistics.ShowCpuPowerMinMax = v;
		}, _draft.Statistics.CpuPowerStatsMode, delegate(string v)
		{
			_draft.Statistics.CpuPowerStatsMode = v;
		}), StatisticsMetricRow("Температуры ОЗУ", _draft.Statistics.ShowRamTemperatureStats, delegate(bool v)
		{
			_draft.Statistics.ShowRamTemperatureStats = v;
		}, _draft.Statistics.RamTemperatureStatsMode, delegate(string v)
		{
			_draft.Statistics.RamTemperatureStatsMode = v;
		}), Check("Автосброс при смене игры", _draft.Statistics.ResetOnGameChange, delegate(bool v)
		{
			_draft.Statistics.ResetOnGameChange = v;
		}, _draft.Statistics.ShowBlock && !flag));
		AddCard("Детектор статтеров", Check("Включить", _draft.Statistics.ShowStutterDetector, delegate(bool v)
		{
			_draft.Statistics.ShowStutterDetector = v;
		}, _draft.Statistics.ShowBlock && !flag, refreshSectionOnChange: true), TextNote("Детектор статтеров — экспериментальная функция."), CheckWithHelp("Уменьшить чувствительность детектора (экспериментальная)", _draft.Statistics.ReduceStutterDetectorSensitivity, delegate(bool v)
		{
			_draft.Statistics.ReduceStutterDetectorSensitivity = v;
		}, "Уменьшает чувствительность Детектора статтеров, чтобы фиксировались только более заметные просадки, лучше совпадающие с теми статтерами, которые реально видны в игре.", _draft.Statistics.ShowBlock && _draft.Statistics.ShowStutterDetector && !flag));
		AddCard("Логирование", DiagnosticLoggingSwitch(), TextNote("Расширенный журнал датчиков сохраняется в RemMon-sensors.log рядом с исполняемым файлом и предназначен только для диагностики."));
		AddPremiumCard("Внешний вид", DisableWhenLineMode(TextValue("Цвет заголовка", _draft.Statistics.TitleColor, delegate(string v)
		{
			_draft.Statistics.TitleColor = v;
		})), DisableWhenLineMode(TextValue("Цвет подписей", _draft.Statistics.LabelColor, delegate(string v)
		{
			_draft.Statistics.LabelColor = v;
		})), DisableWhenLineMode(TextValue("Цвет значений", _draft.Statistics.ValueColor, delegate(string v)
		{
			_draft.Statistics.ValueColor = v;
		})));
		ContentPanel.Children.Add(DisableWhenLineMode(ButtonRow("↻  Сбросить статистику", delegate
		{
			_mainWindow.ResetSessionStats();
		})));
	}

	private void BuildGraph()
	{
		bool flag = IsLineOverlayMode();
		AddCard("Основные параметры", Check("Показывать график", _draft.FrameTimeGraph.ShowGraph, delegate(bool v)
		{
			_draft.FrameTimeGraph.ShowGraph = v;
		}, !flag, refreshSectionOnChange: true), DisableWhenLineMode(Number("Высота", _draft.FrameTimeGraph.Height, 20, 140, delegate(int v)
		{
			_draft.FrameTimeGraph.Height = v;
		})), DisableWhenLineMode(Number("Прозрачность заливки", _draft.FrameTimeGraph.FillOpacity, 0, 100, delegate(int v)
		{
			_draft.FrameTimeGraph.FillOpacity = v;
		})), DisableWhenLineMode(NumberDouble("Максимальное время кадра", _draft.FrameTimeGraph.MaxMs, 8.0, 200.0, delegate(double v)
		{
			_draft.FrameTimeGraph.MaxMs = v;
		})), Check("Показывать время кадра", _draft.FrameTimeGraph.ShowMsLabel, delegate(bool v)
		{
			_draft.FrameTimeGraph.ShowMsLabel = v;
		}, _draft.FrameTimeGraph.ShowGraph && !flag), CheckWithHelp("Сглаживание", _draft.FrameTimeGraph.Smoothing, delegate(bool v)
		{
			_draft.FrameTimeGraph.Smoothing = v;
		}, "Сглаживание делает график времени кадра визуально ровнее и уменьшает резкие одиночные всплески.", _draft.FrameTimeGraph.ShowGraph && !flag));
		AddPremiumCard("Внешний вид", DisableWhenLineMode(TextValue("Цвет графика", _draft.FrameTimeGraph.Color, delegate(string v)
		{
			_draft.FrameTimeGraph.Color = v;
		})), DisableWhenLineMode(TextValue("Фон", _draft.FrameTimeGraph.BackgroundColor, delegate(string v)
		{
			_draft.FrameTimeGraph.BackgroundColor = v;
		})));
	}

	private void BuildAppearance()
	{
		AddCard("Тема и фон", DisableWhenFreeVersion(ComboDisplay("Тема", _draft.Appearance.Theme, ThemeOptions, ApplyTheme)), DisableWhenFreeVersion(TextValue("Цвет фона", _draft.Appearance.BackgroundColor, delegate(string v)
		{
			_draft.Appearance.BackgroundColor = v;
		})), Number("Прозрачность фона", _draft.Appearance.BackgroundOpacity, 0, 100, delegate(int v)
		{
			_draft.Appearance.BackgroundOpacity = v;
		}), Number("Скругление", _draft.Appearance.CornerRadius, 0, 32, delegate(int v)
		{
			_draft.Appearance.CornerRadius = v;
		}), FreeVersionNote());
		AddCard("Шрифты", DisableWhenFreeVersion(ComboDisplay("Шрифт оверлея", GetEffectiveFontIdForSettings(), GetFontOptions(), delegate(string v)
		{
			_draft.Appearance.FontId = v;
		})), PercentNumber("Размер шрифта", _draft.Appearance.TextScale, 0.5, 3.0, 0.25, delegate(double v)
		{
			_draft.Appearance.TextScale = v;
		}), FontFileInstallRow(), FontUrlInstallRow(), TextNote(GetFontStatusText()));
		AddCard("Текст", DisableWhenFreeVersion(Check("Показывать реквизиты бренда", _draft.Appearance.ShowBranding, delegate(bool v)
		{
			_draft.Appearance.ShowBranding = v;
		})), FreeVersionNote());
	}

	private IEnumerable<DisplayOption> GetFontOptions()
	{
		_draft.Appearance.FontId = FontLibraryService.NormalizeFontId(_draft.Appearance.FontId);
		IReadOnlyList<InstalledFontInfo> source;
		if (!IsFreeVersion)
		{
			source = FontLibraryService.GetInstalledFonts();
		}
		else
		{
			IReadOnlyList<InstalledFontInfo> readOnlyList = new InstalledFontInfo[1] { InstalledFontInfo.BuiltIn };
			source = readOnlyList;
		}
		return source.Select((InstalledFontInfo font) => new DisplayOption(font.Id, font.IsBuiltIn ? (font.DisplayName + " (по умолчанию)") : font.DisplayName)).ToArray();
	}

	private string GetEffectiveFontIdForSettings()
	{
		if (!IsFreeVersion)
		{
			return _draft.Appearance.FontId;
		}
		return "system:Segoe UI";
	}

	private UIElement FontFileInstallRow()
	{
		Button button = CreateInlineButton("Загрузить из файла", InstallFontFromFile);
		button.IsEnabled = !IsFreeVersion;
		return Row("Из файла", button);
	}

	private UIElement FontUrlInstallRow()
	{
		TextBox urlBox = new TextBox
		{
			Text = _fontDownloadUrl,
			Width = 330.0,
			MinWidth = 330.0,
			ToolTip = "Прямая ссылка на файл .ttf, .otf или .ttc"
		};
		ToolTipService.SetInitialShowDelay(urlBox, 1000);
		urlBox.TextChanged += delegate
		{
			_fontDownloadUrl = urlBox.Text;
		};
		Button button = CreateInlineButton("Скачать", delegate
		{
			InstallFontFromUrlAsync(urlBox.Text);
		});
		urlBox.IsEnabled = !IsFreeVersion;
		button.IsEnabled = !IsFreeVersion;
		return Row("Из интернета", urlBox, button);
	}

	private string GetFontStatusText()
	{
		if (IsFreeVersion)
		{
			return "Пользовательские шрифты доступны только в полной версии. Сейчас используется шрифт по умолчанию.";
		}
		if (!string.IsNullOrWhiteSpace(_fontStatusText))
		{
			return _fontStatusText + "\nУстановленные шрифты сохраняются локально и остаются доступными в списке без повторной загрузки.";
		}
		return "Установленные шрифты сохраняются локально и остаются доступными в списке без повторной загрузки.";
	}

	private void InstallFontFromFile()
	{
		if (IsFreeVersion)
		{
			_fontStatusText = "Загрузка шрифтов доступна только в полной версии.";
			BuildCurrentSection();
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Загрузить шрифт",
			Filter = "Файлы шрифтов (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc|Все файлы (*.*)|*.*"
		};
		if (openFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			SelectInstalledFont(FontLibraryService.InstallFromFile(openFileDialog.FileName), "Шрифт установлен");
		}
		catch (Exception ex)
		{
			_fontStatusText = "Не удалось установить шрифт: " + ex.Message;
			BuildCurrentSection();
		}
	}

	private async Task InstallFontFromUrlAsync(string url)
	{
		if (IsFreeVersion)
		{
			_fontStatusText = "Загрузка шрифтов доступна только в полной версии.";
			BuildCurrentSection();
			return;
		}
		try
		{
			_fontStatusText = "Загрузка шрифта...";
			BuildCurrentSection();
			SelectInstalledFont(await FontLibraryService.InstallFromUrlAsync(url), "Шрифт загружен");
		}
		catch (Exception ex)
		{
			_fontStatusText = "Не удалось загрузить шрифт: " + ex.Message;
			BuildCurrentSection();
		}
	}

	private void SelectInstalledFont(InstalledFontInfo font, string status)
	{
		_draft.Appearance.FontId = font.Id;
		_fontStatusText = status + ": " + font.DisplayName;
		BuildCurrentSection();
		Changed();
	}

	private void ApplyTheme(string theme)
	{
		_draft.Appearance.Theme = theme;
		ApplySettingsTheme();
		BuildCurrentSection();
	}

	private void ApplySettingsTheme()
	{
		SettingsThemePalette settingsThemePalette = GetSettingsThemePalette(_draft.Appearance.Theme);
		base.Background = BrushFromHex(settingsThemePalette.Window, "#FF07111B");
		SettingsRoot.Background = BrushFromHex(settingsThemePalette.Window, "#FF07111B");
		HeaderBar.Background = BrushFromHex(settingsThemePalette.Bar, "#EE050B12");
		SidebarPanel.Background = BrushFromHex(settingsThemePalette.Sidebar, "#DD0A1521");
		FooterBar.Background = BrushFromHex(settingsThemePalette.Bar, "#EE050B12");
		HintPanel.Background = BrushFromHex(settingsThemePalette.Hint, "#66101B28");
	}

	private static SettingsThemePalette GetSettingsThemePalette(string theme)
	{
		return theme switch
		{
			"Graphite" => new SettingsThemePalette("#FF101418", "#FF0B0F14", "#FF151B22", "#FF18202A", "#FF202A35"), 
			"Midnight" => new SettingsThemePalette("#FF050816", "#FF030611", "#FF071225", "#FF0A1530", "#FF101B33"), 
			"Emerald" => new SettingsThemePalette("#FF06150F", "#FF03100B", "#FF092018", "#FF0B261D", "#FF102E24"), 
			"Sapphire" => new SettingsThemePalette("#FF06111F", "#FF030B17", "#FF0A1728", "#FF0E2036", "#FF102844"), 
			"Amber" => new SettingsThemePalette("#FF181008", "#FF100A04", "#FF231608", "#FF2C1B08", "#FF33220F"), 
			"Darker" => new SettingsThemePalette("#FF03070C", "#FF010408", "#FF050B12", "#FF07101A", "#FF0A1420"), 
			_ => new SettingsThemePalette("#FF07111B", "#EE050B12", "#DD0A1521", "#FF101B28", "#66101B28"), 
		};
	}

	private void BuildHotKeys()
	{
		AddCard("Глобальные горячие клавиши", HotKeyEditor("Показать / скрыть оверлей", _draft.HotKeys.ToggleOverlay), HotKeyEditor("Сбросить статистику", _draft.HotKeys.ResetStatistics), HotKeyEditor("Переключить режим оверлея", _draft.HotKeys.ToggleOverlayMode));
		AddCard("Проверка конфликтов", TextNote("Если сочетание занято другим приложением, после «Применить» оно просто не зарегистрируется."));
	}

	private void BuildUpdates()
	{
		AddCard("Проверка обновлений", Row("Текущая версия", ValueText(_updateService.CurrentVersionText)), Check("Напоминать о новых версиях при запуске", _draft.RemindUpdatesOnStartup, delegate(bool v)
		{
			_draft.RemindUpdatesOnStartup = v;
		}), TextNote(_updateStatusText), ButtonRow(_updateCheckInProgress ? "Проверка..." : "⟳  Проверить обновления", delegate
		{
			CheckForUpdatesAsync();
		}));
		UpdateCheckResult? lastUpdateResult = _lastUpdateResult;
		if (lastUpdateResult != null && lastUpdateResult.Status == UpdateCheckStatus.Available && _lastUpdateResult.Manifest != null)
		{
			List<UIElement> list = new List<UIElement>();
			list.Add(Row("Новая версия", ValueText(_lastUpdateResult.ServerVersion)));
			list.Add(TextNote("Что нового"));
			list.Add(CreateNotesBox(_lastUpdateResult.Manifest.Notes));
			List<UIElement> list2 = list;
			if (_updateDownloadInProgress)
			{
				string value = (_updateDownloadProgress.HasValue ? $"{_updateDownloadProgress.Value:0}%" : "идёт загрузка");
				list2.Add(Row("Загрузка", CreateDownloadProgressBar(_updateDownloadProgress), ValueText(value)));
			}
			list2.Add(ButtonRow(_updateDownloadInProgress ? "Скачивание..." : "⇩  Скачать", delegate
			{
				DownloadUpdateAsync();
			}));
			if (!string.IsNullOrWhiteSpace(_downloadedUpdatePath))
			{
				list2.Add(TextNote("Файл скачан: " + _downloadedUpdatePath));
				list2.Add(CreateDownloadedUpdateButtons(_downloadedUpdatePath));
			}
			AddCard("Доступно обновление", list2.ToArray());
		}
	}

	private void BuildActivation()
	{
		LicenseState licenseState = _licenseService.CheckLocalLicense();
		LicenseFile license = licenseState.License;
		if (licenseState.IsValid)
		{
			AddCard("Состояние активации", Row("Статус", ValueText("Программа активирована")), Row("ActivationId", ValueText(ShortId(license?.ActivationId ?? string.Empty))), Row("HardwareId", ValueText(ShortHardwareId(_licenseService.HardwareId))), Row("Срок действия", ValueText(FormatExpiresDate(license?.ExpiresAt))), Row("Последняя онлайн-проверка", ValueText(FormatLicenseDate(license?.LastOnlineCheckAt))));
			AddCard("Действия", ButtonRow("⟳  Проверить активацию", delegate
			{
				CheckLicenseNowAsync();
			}), ButtonRow("↻  Сменить ключ", delegate
			{
				ChangeLicenseKeyAsync();
			}), ButtonRow("Удалить активацию", delegate
			{
				RemoveLicenseAsync();
			}));
		}
		else
		{
			AddCard("Состояние активации", Row("Статус", ValueText("Бесплатная версия")), Row("HardwareId", ValueText(ShortHardwareId(_licenseService.HardwareId))), TextNote("Вы используете бесплатную версию программы."));
			AddCard("Действия", ButtonRow("Активировать", ActivateLicense), DisableWhen(disabled: true, ButtonRow("⟳  Проверить активацию", delegate
			{
				CheckLicenseNowAsync();
			})));
		}
	}

	private async Task CheckLicenseNowAsync()
	{
		LicenseOperationResult licenseOperationResult = await _licenseService.CheckOnlineAsync();
		if (!licenseOperationResult.Success && licenseOperationResult.ShouldBlockApplication && licenseOperationResult.Status != "offline")
		{
			_licenseService.RemoveLicense();
			_mainWindow.RefreshLicenseState();
			AppDialog.Show(this, "Активация", "Активация недействительна. Включена бесплатная версия программы.", AppDialogKind.Warning);
		}
		else
		{
			AppDialog.Show(this, "Активация", licenseOperationResult.Message, (!licenseOperationResult.Success) ? AppDialogKind.Warning : AppDialogKind.Info);
			_mainWindow.RefreshLicenseState();
		}
	}

	private void ActivateLicense()
	{
		if (new ActivationWindow(_licenseService)
		{
			Owner = this
		}.ShowDialog() == true)
		{
			AppDialog.Show(this, "Активация", "Лицензия активирована.", AppDialogKind.Info);
			_mainWindow.RefreshLicenseState();
		}
	}

	private async Task ChangeLicenseKeyAsync()
	{
		AppLogger.Info("change key started");
		if (_licenseService.CheckLocalLicense().IsValid)
		{
			LicenseDeactivationResult licenseDeactivationResult = await _licenseService.DeactivateAsync();
			if (licenseDeactivationResult.IsSuccessful)
			{
				_licenseService.RemoveLicense(keyChanged: true);
				AppLogger.Info("change key old license deactivated");
				AppLogger.Info("local license removed after server deactivation");
				_mainWindow.RefreshLicenseState();
			}
			else
			{
				if (!licenseDeactivationResult.IsServerUnavailable)
				{
					AppLogger.Info("change key cancelled: " + licenseDeactivationResult.Status);
					AppDialog.Show(this, "Сменить ключ", licenseDeactivationResult.Message, AppDialogKind.Warning);
					return;
				}
				if (AppDialog.Show(this, "Сменить ключ", "Сервер активации недоступен. Если продолжить смену ключа, старая активация останется занятой на сервере. Старый ключ может быть недоступен для активации на другом ПК. Продолжить?", AppDialogKind.Warning, ("Продолжить локально", AppDialogResult.ContinueLocal, true), ("Отмена", AppDialogResult.No, false)) != AppDialogResult.ContinueLocal)
				{
					AppLogger.Info("change key cancelled");
					return;
				}
				_licenseService.RemoveLicense(keyChanged: true);
				AppLogger.Info("change key continued locally without server deactivation");
				AppLogger.Info("local license removed without server deactivation");
				_mainWindow.RefreshLicenseState();
			}
		}
		if (new ActivationWindow(_licenseService)
		{
			Owner = this
		}.ShowDialog() == true)
		{
			AppLogger.Info("new key activation success");
			AppDialog.Show(this, "Активация", "Лицензия активирована.", AppDialogKind.Info);
		}
		else
		{
			AppLogger.Info("new key activation cancelled");
		}
		_mainWindow.RefreshLicenseState();
	}

	private async Task RemoveLicenseAsync()
	{
		LicenseState licenseState = _licenseService.CheckLocalLicense();
		if (!licenseState.IsValid || licenseState.License == null)
		{
			AppDialog.Show(this, "Активация", "Активация отсутствует.", AppDialogKind.Info);
		}
		else
		{
			if (AppDialog.Show(this, "Удалить активацию", "Удалить активацию с этого компьютера?", AppDialogKind.Confirmation, ("Удалить активацию", AppDialogResult.Yes, true), ("Отмена", AppDialogResult.No, false)) != AppDialogResult.Yes)
			{
				return;
			}
			LicenseDeactivationResult licenseDeactivationResult = await _licenseService.DeactivateAsync();
			if (licenseDeactivationResult.IsSuccessful)
			{
				_licenseService.RemoveLicense();
				AppLogger.Info("local license removed after server deactivation");
				_mainWindow.RefreshLicenseState();
				AppDialog.Show(this, "Активация", "Активация удалена. Включена бесплатная версия программы.", AppDialogKind.Info);
			}
			else if (licenseDeactivationResult.IsServerUnavailable)
			{
				if (AppDialog.Show(this, "Удалить активацию", "Сервер активации недоступен. Если удалить локальную лицензию сейчас, сервер продолжит считать этот ключ активированным, и его нельзя будет использовать на другом ПК, пока активация не будет сброшена вручную. Удалить только локальную лицензию?", AppDialogKind.Warning, ("Удалить только локально", AppDialogResult.LocalOnly, true), ("Отмена", AppDialogResult.No, false)) == AppDialogResult.LocalOnly)
				{
					_licenseService.RemoveLicense();
					AppLogger.Info("local license removed without server deactivation");
					_mainWindow.RefreshLicenseState();
					AppDialog.Show(this, "Активация", "Локальная лицензия удалена. Включена бесплатная версия программы.", AppDialogKind.Info);
				}
			}
			else
			{
				AppDialog.Show(this, "Удалить активацию", licenseDeactivationResult.Message, AppDialogKind.Warning);
			}
		}
	}

	private static string ShortId(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			if (value.Length > 8)
			{
				return value.Substring(0, 8) + "...";
			}
			return value;
		}
		return "N/A";
	}

	private static string ShortHardwareId(string hardwareId)
	{
		if (string.IsNullOrWhiteSpace(hardwareId))
		{
			return "N/A";
		}
		if (hardwareId.Length > 8)
		{
			return hardwareId.Substring(0, 8) + "...";
		}
		return hardwareId;
	}

	private static string FormatLicenseDate(DateTimeOffset? value)
	{
		return value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "N/A";
	}

	private static string FormatExpiresDate(DateTimeOffset? value)
	{
		return value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "Бессрочно";
	}

	private void BuildFeedback()
	{
		AddCard("Обратная связь", ButtonRow("Оставить отзыв о программе", OpenFeedbackForm));
	}

	private void OpenFeedbackForm()
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "https://forms.gle/GiPYSmDMEVRPUUeN9",
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			AppDialog.Show(this, "Оставить отзыв", "Не удалось открыть форму:\n" + ex.Message, AppDialogKind.Warning);
		}
	}

	private async Task CheckForUpdatesAsync()
	{
		if (!_updateCheckInProgress && !_updateDownloadInProgress)
		{
			_updateCheckInProgress = true;
			_downloadedUpdatePath = null;
			_updateDownloadProgress = null;
			_updateStatusText = "Проверка обновлений...";
			BuildCurrentSection();
			UpdateCheckResult updateCheckResult = (_lastUpdateResult = await _updateService.CheckAsync());
			_updateCheckInProgress = false;
			string updateStatusText = ((updateCheckResult.Status != UpdateCheckStatus.Available) ? updateCheckResult.Message : ("Доступна новая версия: " + updateCheckResult.ServerVersion + "."));
			_updateStatusText = updateStatusText;
			BuildCurrentSection();
		}
	}

	private async Task DownloadUpdateAsync()
	{
		if (_updateDownloadInProgress || _lastUpdateResult?.Manifest == null)
		{
			return;
		}
		_updateDownloadInProgress = true;
		_updateDownloadProgress = null;
		_downloadedUpdatePath = null;
		_updateStatusText = "Скачивание обновления...";
		BuildCurrentSection();
		Progress<UpdateDownloadProgress> progress = new Progress<UpdateDownloadProgress>(delegate(UpdateDownloadProgress value)
		{
			_updateDownloadProgress = value.Percent;
			_updateStatusText = value.Status;
			if (SectionList.SelectedItem is ListBoxItem { Content: var content } && content?.ToString() == "Обновления")
			{
				BuildCurrentSection();
			}
		});
		UpdateDownloadResult updateDownloadResult = await _updateService.DownloadAsync(_lastUpdateResult.Manifest, progress);
		_updateDownloadInProgress = false;
		_updateStatusText = updateDownloadResult.Message;
		_downloadedUpdatePath = updateDownloadResult.FilePath;
		BuildCurrentSection();
		if (!updateDownloadResult.Success)
		{
			AppDialog.Show(this, "Обновления", updateDownloadResult.Message, AppDialogKind.Warning);
			return;
		}
		UpdateService.OpenFolderForFile(updateDownloadResult.FilePath);
		AppLogger.Info("Explorer opened for downloaded update.");
	}

	private UIElement HotKeyEditor(string label, HotKeyDefinition hotKey)
	{
		Border border = new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(90, 10, 21, 33)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(34, 50, 71)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0),
			Margin = new Thickness(0.0, 6.0, 0.0, 8.0)
		};
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(210.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.Children.Add(new TextBlock
		{
			Text = label,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = SettingsOptionTextBrush()
		});
		WrapPanel wrapPanel = new WrapPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(wrapPanel, 1);
		grid.Children.Add(wrapPanel);
		wrapPanel.Children.Add(CheckInline("Вкл", hotKey.Enabled, delegate(bool v)
		{
			hotKey.Enabled = v;
		}));
		wrapPanel.Children.Add(CheckInline("Ctrl", hotKey.Control, delegate(bool v)
		{
			hotKey.Control = v;
		}));
		wrapPanel.Children.Add(CheckInline("Alt", hotKey.Alt, delegate(bool v)
		{
			hotKey.Alt = v;
		}));
		wrapPanel.Children.Add(CheckInline("Shift", hotKey.Shift, delegate(bool v)
		{
			hotKey.Shift = v;
		}));
		wrapPanel.Children.Add(CheckInline("Win", hotKey.Win, delegate(bool v)
		{
			hotKey.Win = v;
		}));
		ComboBox keyBox = new ComboBox
		{
			Width = 120.0,
			MinWidth = 120.0,
			Margin = new Thickness(12.0, 0.0, 0.0, 0.0)
		};
		string[] hotKeyOptions = HotKeyOptions;
		foreach (string newItem in hotKeyOptions)
		{
			keyBox.Items.Add(newItem);
		}
		keyBox.SelectedItem = GetHotKeyDisplayValue(hotKey.Key);
		UpdateComboSelectedText(keyBox);
		keyBox.SelectionChanged += delegate
		{
			hotKey.Key = keyBox.SelectedItem?.ToString() ?? hotKey.Key;
			UpdateComboSelectedText(keyBox);
			Changed();
		};
		keyBox.Loaded += delegate
		{
			UpdateComboSelectedText(keyBox);
		};
		wrapPanel.Children.Add(keyBox);
		TextBlock display = new TextBlock
		{
			Text = hotKey.DisplayText,
			Margin = new Thickness(12.0, 2.0, 0.0, 0.0),
			Foreground = new SolidColorBrush(Color.FromRgb(149, 166, 187)),
			VerticalAlignment = VerticalAlignment.Center
		};
		wrapPanel.Children.Add(display);
		foreach (UIElement child in wrapPanel.Children)
		{
			if (child is CheckBox checkBox)
			{
				checkBox.Checked += delegate
				{
					RefreshDisplay();
				};
				checkBox.Unchecked += delegate
				{
					RefreshDisplay();
				};
			}
		}
		keyBox.SelectionChanged += delegate
		{
			RefreshDisplay();
		};
		border.Child = grid;
		return border;
		void RefreshDisplay()
		{
			display.Text = hotKey.DisplayText;
		}
	}

	private void AddCard(string title, params UIElement[] controls)
	{
		Border border = new Border
		{
			Background = BrushFromHex(GetSettingsThemePalette(_draft.Appearance.Theme).Card, "#8C101B28"),
			BorderBrush = new SolidColorBrush(Color.FromRgb(34, 50, 71)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(16.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(new TextBlock
		{
			Text = title,
			FontWeight = FontWeights.SemiBold,
			FontSize = 15.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		foreach (UIElement element in controls)
		{
			stackPanel.Children.Add(element);
		}
		border.Child = stackPanel;
		ContentPanel.Children.Add(border);
	}

	private void AddPremiumCard(string title, params UIElement[] controls)
	{
		Border border = new Border
		{
			Background = BrushFromHex(GetSettingsThemePalette(_draft.Appearance.Theme).Card, "#8C101B28"),
			BorderBrush = new SolidColorBrush(Color.FromRgb(34, 50, 71)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(16.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(new TextBlock
		{
			Text = title,
			FontWeight = FontWeights.SemiBold,
			FontSize = 15.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		StackPanel stackPanel2 = new StackPanel();
		foreach (UIElement element in controls)
		{
			stackPanel2.Children.Add(element);
		}
		if (IsFreeVersion)
		{
			stackPanel2.IsEnabled = false;
			stackPanel2.Opacity = 0.55;
		}
		stackPanel.Children.Add(stackPanel2);
		if (IsFreeVersion)
		{
			stackPanel.Children.Add(FreeVersionNote());
		}
		border.Child = stackPanel;
		ContentPanel.Children.Add(border);
	}

	private UIElement DisableWhenFreeVersion(UIElement element)
	{
		if (IsFreeVersion)
		{
			element.IsEnabled = false;
			element.Opacity = 0.55;
		}
		return element;
	}

	private UIElement FreeVersionNote()
	{
		if (!IsFreeVersion)
		{
			return new Border
			{
				Visibility = Visibility.Collapsed
			};
		}
		return TextNote("Настройка цветов недоступна в бесплатной версии программы");
	}

	private UIElement BuildBlockOrder()
	{
		StackPanel stackPanel = new StackPanel();
		string[] array = _draft.BlockOrder.ToArray();
		foreach (string key in array)
		{
			DisplayOption displayOption = BlockOrderOptions.FirstOrDefault((DisplayOption item) => item.Value.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? new DisplayOption(key, key);
			int num = _draft.BlockOrder.FindIndex((string item) => item.Equals(key, StringComparison.OrdinalIgnoreCase));
			Border border = new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(95, 10, 21, 33)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(32, 48, 68)),
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(7.0),
				Padding = new Thickness(10.0, 8.0, 10.0, 8.0),
				Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
			};
			Grid grid = new Grid();
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = GridLength.Auto
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = GridLength.Auto
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = GridLength.Auto
			});
			grid.Children.Add(new TextBlock
			{
				Text = "⋮⋮",
				FontSize = 18.0,
				FontWeight = FontWeights.Bold,
				Foreground = new SolidColorBrush(Color.FromRgb(150, 166, 188)),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
			});
			TextBlock element = new TextBlock
			{
				Text = displayOption.Label,
				FontSize = 15.0,
				FontWeight = FontWeights.SemiBold,
				VerticalAlignment = VerticalAlignment.Center
			};
			Grid.SetColumn(element, 1);
			Grid.SetColumnSpan(element, 2);
			grid.Children.Add(element);
			StackPanel stackPanel2 = new StackPanel
			{
				Orientation = Orientation.Horizontal
			};
			Button button = new Button
			{
				Content = "↑",
				Width = 34.0,
				Padding = new Thickness(0.0),
				Margin = new Thickness(4.0, 0.0, 0.0, 0.0),
				IsEnabled = (num > 0)
			};
			Button button2 = new Button
			{
				Content = "↓",
				Width = 34.0,
				Padding = new Thickness(0.0),
				Margin = new Thickness(4.0, 0.0, 0.0, 0.0),
				IsEnabled = (num >= 0 && num < _draft.BlockOrder.Count - 1)
			};
			button.Click += delegate
			{
				MoveBlock(key, -1);
			};
			button2.Click += delegate
			{
				MoveBlock(key, 1);
			};
			stackPanel2.Children.Add(button);
			stackPanel2.Children.Add(button2);
			Grid.SetColumn(stackPanel2, 3);
			grid.Children.Add(stackPanel2);
			border.Child = grid;
			stackPanel.Children.Add(border);
		}
		return stackPanel;
	}

	private void MoveBlock(string key, int delta)
	{
		int num = _draft.BlockOrder.FindIndex((string item) => item.Equals(key, StringComparison.OrdinalIgnoreCase));
		int num2 = num + delta;
		if (num >= 0 && num2 >= 0 && num2 < _draft.BlockOrder.Count)
		{
			List<string> blockOrder = _draft.BlockOrder;
			int index = num;
			List<string> blockOrder2 = _draft.BlockOrder;
			int index2 = num2;
			string value = _draft.BlockOrder[num2];
			string value2 = _draft.BlockOrder[num];
			blockOrder[index] = value;
			blockOrder2[index2] = value2;
			BuildCurrentSection();
			Changed();
		}
	}

	private static TextBlock Header(string text)
	{
		return new TextBlock
		{
			Text = text,
			FontWeight = FontWeights.Bold,
			FontSize = 22.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		};
	}

	private UIElement Check(string label, bool value, Action<bool> apply, bool enabled = true, bool refreshSectionOnChange = false)
	{
		CheckBox checkBox = new CheckBox();
		checkBox.Content = label;
		checkBox.IsChecked = value;
		checkBox.IsEnabled = enabled;
		checkBox.Style = (Style)FindResource("SwitchCheckBoxStyle");
		checkBox.Checked += delegate
		{
			ApplyCheckChange(next: true);
		};
		checkBox.Unchecked += delegate
		{
			ApplyCheckChange(next: false);
		};
		return checkBox;
		void ApplyCheckChange(bool next)
		{
			apply(next);
			if (refreshSectionOnChange)
			{
				BuildCurrentSection();
			}
			Changed();
		}
	}

	private static void ApplyCpuCoreLoadGraphToggle(HardwareBlockSettings block, bool show)
	{
		block.ShowCoreLoadGraph = show;
		if (!show)
		{
			block.ShowCoreLoadGraphLabels = false;
		}
	}

	private static void ApplyCpuCoreClockParentToggle(HardwareBlockSettings block, bool show)
	{
		block.ShowCoreClocks = show;
		if (!show)
		{
			block.ShowPerformanceCoreClocks = false;
			block.ShowEfficiencyCoreClocks = false;
		}
		else if (!block.ShowPerformanceCoreClocks && !block.ShowEfficiencyCoreClocks)
		{
			block.ShowPerformanceCoreClocks = true;
			block.ShowEfficiencyCoreClocks = true;
		}
	}

	private static void ApplyCpuCoreClockChildToggle(HardwareBlockSettings block, bool? performance = null, bool? efficiency = null)
	{
		if (performance.HasValue)
		{
			block.ShowPerformanceCoreClocks = performance.Value;
		}
		if (efficiency.HasValue)
		{
			block.ShowEfficiencyCoreClocks = efficiency.Value;
		}
		if (!block.ShowPerformanceCoreClocks && !block.ShowEfficiencyCoreClocks)
		{
			block.ShowCoreClocks = false;
		}
	}

	private UIElement DiagnosticLoggingSwitch()
	{
		CheckBox box = new CheckBox
		{
			Content = "Включить расширенное логирование датчиков",
			IsChecked = _draft.Statistics.DiagnosticLoggingEnabled,
			Style = (Style)FindResource("SwitchCheckBoxStyle")
		};
		bool suppress = false;
		box.PreviewMouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (ShouldConfirmEnable())
			{
				e.Handled = true;
				ConfirmAndEnableDiagnosticLogging(box);
			}
		};
		box.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if ((e.Key == Key.Space || e.Key == Key.Return) && ShouldConfirmEnable())
			{
				e.Handled = true;
				ConfirmAndEnableDiagnosticLogging(box);
			}
		};
		box.Checked += delegate
		{
			if (!suppress)
			{
				if (!_mainWindow.CurrentSettings.Statistics.DiagnosticLoggingEnabled)
				{
					suppress = true;
					box.IsChecked = false;
					suppress = false;
				}
				else
				{
					_draft.Statistics.DiagnosticLoggingEnabled = true;
					Changed();
				}
			}
		};
		box.Unchecked += delegate
		{
			if (!suppress)
			{
				_draft.Statistics.DiagnosticLoggingEnabled = false;
				Changed();
			}
		};
		return box;
		void ConfirmAndEnableDiagnosticLogging(CheckBox target)
		{
			if (AppDialog.ShowLoggingRestartWarning(this) != AppDialogResult.Yes)
			{
				_draft.Statistics.DiagnosticLoggingEnabled = false;
				return;
			}
			_draft.Statistics.DiagnosticLoggingEnabled = true;
			try
			{
				_mainWindow.SaveSettingsAndRestart(_draft);
			}
			catch (Exception ex)
			{
				_draft.Statistics.DiagnosticLoggingEnabled = false;
				suppress = true;
				target.IsChecked = false;
				suppress = false;
				AppDialog.Show(this, "Логирование датчиков", "Не удалось перезапустить приложение: " + ex.Message, AppDialogKind.Error);
			}
		}
		bool ShouldConfirmEnable()
		{
			if (box.IsChecked != true)
			{
				return !_mainWindow.CurrentSettings.Statistics.DiagnosticLoggingEnabled;
			}
			return false;
		}
	}

	private UIElement CheckWithHelp(string label, bool value, Action<bool> apply, string helpText, bool enabled = true)
	{
		CheckBox checkBox = new CheckBox();
		checkBox.Content = CreateLabelWithHelp(label, helpText);
		checkBox.IsChecked = value;
		checkBox.IsEnabled = enabled;
		checkBox.Style = (Style)FindResource("SwitchCheckBoxStyle");
		checkBox.VerticalAlignment = VerticalAlignment.Center;
		checkBox.Checked += delegate
		{
			apply(obj: true);
			Changed();
		};
		checkBox.Unchecked += delegate
		{
			apply(obj: false);
			Changed();
		};
		return checkBox;
	}

	private UIElement StatisticsMetricRow(string label, bool visible, Action<bool> applyVisible, string mode, Action<string> applyMode)
	{
		bool flag = _draft.Statistics.ShowBlock && !IsLineOverlayMode();
		Grid obj = new Grid
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0),
			MinHeight = 34.0,
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(230.0)
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				},
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			},
			Children = { (UIElement)CreateLabel(label) }
		};
		ComboBox comboBox = CreateDisplayCombo(mode, StatisticsModeOptions, applyMode, 128.0);
		comboBox.IsEnabled = flag && visible;
		comboBox.Opacity = (comboBox.IsEnabled ? 1.0 : 0.55);
		Grid.SetColumn(comboBox, 1);
		obj.Children.Add(comboBox);
		CheckBox checkBox = new CheckBox
		{
			IsChecked = visible,
			IsEnabled = flag,
			Style = (Style)FindResource("SwitchCheckBoxStyle"),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center
		};
		checkBox.Checked += delegate
		{
			ApplyMetricVisibility(next: true);
		};
		checkBox.Unchecked += delegate
		{
			ApplyMetricVisibility(next: false);
		};
		Grid.SetColumn(checkBox, 3);
		obj.Children.Add(checkBox);
		return obj;
		void ApplyMetricVisibility(bool next)
		{
			applyVisible(next);
			BuildCurrentSection();
			Changed();
		}
	}

	private UIElement CheckInline(string label, bool value, Action<bool> apply)
	{
		CheckBox checkBox = new CheckBox();
		checkBox.Content = label;
		checkBox.IsChecked = value;
		checkBox.Margin = new Thickness(8.0, 3.0, 8.0, 3.0);
		checkBox.MinWidth = 58.0;
		checkBox.Checked += delegate
		{
			apply(obj: true);
			Changed();
		};
		checkBox.Unchecked += delegate
		{
			apply(obj: false);
			Changed();
		};
		return checkBox;
	}

	private UIElement Combo(string label, string value, IEnumerable<string> values, Action<string> apply)
	{
		ComboBox combo = new ComboBox
		{
			SelectedItem = value
		};
		foreach (string value2 in values)
		{
			combo.Items.Add(value2);
		}
		UpdateComboSelectedText(combo);
		combo.SelectionChanged += delegate
		{
			apply(combo.SelectedItem?.ToString() ?? value);
			Changed();
		};
		combo.SelectionChanged += delegate
		{
			UpdateComboSelectedText(combo);
		};
		combo.Loaded += delegate
		{
			UpdateComboSelectedText(combo);
		};
		return Row(label, combo);
	}

	private UIElement ComboDisplay(string label, string value, IEnumerable<DisplayOption> values, Action<string> apply, double? width = null)
	{
		return ComboDisplay(CreateLabel(label), value, values, apply, width);
	}

	private UIElement ComboDisplayWithHelp(string label, string helpText, string value, IEnumerable<DisplayOption> values, Action<string> apply, double? width = null)
	{
		return ComboDisplay(CreateLabelWithHelp(label, helpText), value, values, apply, width);
	}

	private UIElement ComboDisplay(UIElement label, string value, IEnumerable<DisplayOption> values, Action<string> apply, double? width = null)
	{
		ComboBox comboBox = CreateDisplayCombo(value, values, apply, width);
		return Row(label, comboBox);
	}

	private ComboBox CreateDisplayCombo(string value, IEnumerable<DisplayOption> values, Action<string> apply, double? width = null)
	{
		ComboBox combo = new ComboBox
		{
			DisplayMemberPath = "Label",
			SelectedValuePath = "Value"
		};
		if (width.HasValue)
		{
			combo.Width = width.Value;
			combo.MinWidth = width.Value;
		}
		foreach (DisplayOption value2 in values)
		{
			combo.Items.Add(value2);
		}
		combo.SelectedValue = value;
		if (combo.SelectedItem == null && value.Equals("GB", StringComparison.OrdinalIgnoreCase))
		{
			combo.SelectedValue = "ГБ";
		}
		if (combo.SelectedItem == null && value.Equals("MB", StringComparison.OrdinalIgnoreCase))
		{
			combo.SelectedValue = "МБ";
		}
		if (combo.SelectedItem == null && value.Equals("MHz", StringComparison.OrdinalIgnoreCase))
		{
			combo.SelectedValue = "МГц";
		}
		if (combo.SelectedItem == null && value.Equals("GHz", StringComparison.OrdinalIgnoreCase))
		{
			combo.SelectedValue = "ГГц";
		}
		UpdateComboSelectedText(combo);
		combo.SelectionChanged += delegate
		{
			apply(combo.SelectedValue?.ToString() ?? value);
			UpdateComboSelectedText(combo);
			Changed();
		};
		combo.Loaded += delegate
		{
			UpdateComboSelectedText(combo);
		};
		return combo;
	}

	private static void UpdateComboSelectedText(ComboBox combo)
	{
		object selectedItem = combo.SelectedItem;
		string tag = ((selectedItem is DisplayOption displayOption) ? displayOption.Label : ((selectedItem is ComboBoxItem { Content: var content }) ? (content?.ToString() ?? string.Empty) : ((selectedItem != null) ? (selectedItem.ToString() ?? string.Empty) : string.Empty)));
		combo.Tag = tag;
	}

	private UIElement Number(string label, int value, int min, int max, Action<int> apply)
	{
		return Number(CreateLabel(label), value, min, max, apply);
	}

	private UIElement NumberWithHelp(string label, string helpText, int value, int min, int max, Action<int> apply)
	{
		return Number(CreateLabelWithHelp(label, helpText), value, min, max, apply);
	}

	private UIElement Number(UIElement label, int value, int min, int max, Action<int> apply)
	{
		Slider slider = new Slider
		{
			Minimum = min,
			Maximum = max,
			Value = value,
			TickFrequency = 1.0,
			IsSnapToTickEnabled = true
		};
		TextBox text = CreateSliderValueTextBox(value.ToString());
		slider.ValueChanged += delegate
		{
			int obj = (int)Math.Round(slider.Value);
			text.Text = obj.ToString();
			apply(obj);
			Changed();
		};
		text.LostFocus += delegate
		{
			if (int.TryParse(text.Text, out var result))
			{
				slider.Value = Math.Clamp(result, min, max);
			}
		};
		return Row(label, slider, text);
	}

	private UIElement NumberDouble(string label, double value, double min, double max, Action<double> apply)
	{
		Slider slider = new Slider
		{
			Minimum = min,
			Maximum = max,
			Value = value
		};
		TextBox text = CreateSliderValueTextBox(value.ToString("0.##"));
		slider.ValueChanged += delegate
		{
			double obj = Math.Round(slider.Value, 2);
			text.Text = obj.ToString("0.##");
			apply(obj);
			Changed();
		};
		return Row(label, slider, text);
	}

	private UIElement PercentNumber(string label, double value, double min, double max, double step, Action<double> apply)
	{
		Slider slider = new Slider
		{
			Minimum = min,
			Maximum = max,
			Value = Math.Clamp(value, min, max),
			TickFrequency = step,
			IsSnapToTickEnabled = true
		};
		TextBox text = CreateSliderValueTextBox(FormatPercentScale(slider.Value), isReadOnly: true);
		slider.ValueChanged += delegate
		{
			double value2 = Math.Round(slider.Value / step) * step;
			value2 = Math.Clamp(value2, min, max);
			text.Text = FormatPercentScale(value2);
			apply(value2);
			Changed();
		};
		return Row(label, slider, text);
	}

	private static TextBox CreateSliderValueTextBox(string text, bool isReadOnly = false)
	{
		return new TextBox
		{
			Text = text,
			Width = 68.0,
			Height = 30.0,
			MinWidth = 68.0,
			Padding = new Thickness(7.0, 3.0, 7.0, 3.0),
			TextAlignment = TextAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			IsReadOnly = isReadOnly,
			Margin = new Thickness(14.0, 0.0, 0.0, 0.0)
		};
	}

	private UIElement TextValue(string label, string value, Action<string> apply)
	{
		TextBox text = new TextBox
		{
			Text = value,
			Width = 120.0,
			ToolTip = "Можно ввести цвет вручную в формате HEX"
		};
		ToolTipService.SetInitialShowDelay(text, 1000);
		text.TextChanged += delegate
		{
			apply(text.Text);
			Changed();
		};
		Border swatch = new Border
		{
			Width = 34.0,
			Height = 22.0,
			BorderBrush = Brushes.White,
			BorderThickness = new Thickness(1.0),
			Cursor = Cursors.Hand,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
			ToolTip = "Выбрать цвет из палитры"
		};
		ToolTipService.SetInitialShowDelay(swatch, 1000);
		text.TextChanged += delegate
		{
			swatch.Background = BrushFromHex(text.Text, "#00000000");
		};
		swatch.Background = BrushFromHex(value, "#00000000");
		swatch.ContextMenu = CreateColorPalette(text);
		swatch.MouseLeftButtonUp += delegate
		{
			swatch.ContextMenu.PlacementTarget = swatch;
			swatch.ContextMenu.IsOpen = true;
		};
		return Row(label, text, swatch);
	}

	private UIElement TextInputRow(string label, string value, Action<string> apply)
	{
		TextBox text = new TextBox
		{
			Text = value,
			Width = 240.0,
			MinWidth = 240.0
		};
		text.TextChanged += delegate
		{
			apply(text.Text);
			Changed();
		};
		return Row(label, text);
	}

	private ContextMenu CreateColorPalette(TextBox target)
	{
		ContextMenu contextMenu = new ContextMenu
		{
			Background = new SolidColorBrush(Color.FromRgb(13, 23, 36)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(49, 69, 95)),
			Foreground = SettingsOptionTextBrush(),
			Padding = new Thickness(0.0),
			HasDropShadow = false
		};
		DisplayOption[] basicColorOptions = BasicColorOptions;
		foreach (DisplayOption displayOption in basicColorOptions)
		{
			MenuItem item = new MenuItem
			{
				Header = CreateColorMenuHeader(displayOption.Value, displayOption.Label),
				Tag = displayOption.Value,
				Style = CreateColorMenuItemStyle()
			};
			item.Click += delegate
			{
				target.Text = (string)item.Tag;
			};
			contextMenu.Items.Add(item);
		}
		return contextMenu;
	}

	private static Style CreateColorMenuItemStyle()
	{
		Style obj = new Style(typeof(MenuItem))
		{
			Setters = 
			{
				(SetterBase)new Setter(Control.ForegroundProperty, SettingsOptionTextBrush()),
				(SetterBase)new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(13, 23, 36))),
				(SetterBase)new Setter(Control.PaddingProperty, new Thickness(8.0, 3.0, 10.0, 3.0)),
				(SetterBase)new Setter(FrameworkElement.MinHeightProperty, 22.0)
			}
		};
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory.Name = "ItemBorder";
		frameworkElementFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
		frameworkElementFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(ContentPresenter));
		frameworkElementFactory2.SetValue(ContentPresenter.ContentSourceProperty, "Header");
		frameworkElementFactory2.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
		frameworkElementFactory2.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		ControlTemplate controlTemplate = new ControlTemplate(typeof(MenuItem))
		{
			VisualTree = frameworkElementFactory
		};
		Trigger trigger = new Trigger
		{
			Property = UIElement.IsMouseOverProperty,
			Value = true
		};
		trigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(28, 68, 116))));
		controlTemplate.Triggers.Add(trigger);
		obj.Setters.Add(new Setter(Control.TemplateProperty, controlTemplate));
		return obj;
	}

	private static UIElement CreateColorMenuHeader(string color, string label)
	{
		return new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Children = 
			{
				(UIElement)new Border
				{
					Width = 18.0,
					Height = 18.0,
					Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
					BorderBrush = Brushes.White,
					BorderThickness = new Thickness(1.0),
					Background = BrushFromHex(color, "#00000000")
				},
				(UIElement)new TextBlock
				{
					Text = label,
					Foreground = SettingsOptionTextBrush()
				}
			}
		};
	}

	private UIElement ButtonRow(string label, Action action)
	{
		Button button = new Button();
		button.Content = label;
		button.HorizontalAlignment = HorizontalAlignment.Left;
		button.MinWidth = 196.0;
		button.Padding = new Thickness(16.0, 9.0, 16.0, 9.0);
		button.Margin = new Thickness(6.0, 2.0, 6.0, 14.0);
		button.Click += delegate
		{
			action();
		};
		return button;
	}

	private static Button CreateInlineButton(string label, Action action)
	{
		Button button = new Button();
		button.Content = label;
		button.MinWidth = 130.0;
		button.Padding = new Thickness(16.0, 9.0, 16.0, 9.0);
		button.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
		button.Click += delegate
		{
			action();
		};
		return button;
	}

	private static TextBlock ValueText(string value)
	{
		return new TextBlock
		{
			Text = value,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = SettingsOptionTextBrush()
		};
	}

	private static TextBox CreateNotesBox(string notes)
	{
		string text = (string.IsNullOrWhiteSpace(notes) ? "Описание изменений не указано." : notes.Replace("\\n", Environment.NewLine));
		return new TextBox
		{
			Text = text,
			IsReadOnly = true,
			TextWrapping = TextWrapping.Wrap,
			AcceptsReturn = true,
			MinHeight = 120.0,
			MaxHeight = 220.0,
			Width = 420.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
	}

	private static ProgressBar CreateDownloadProgressBar(double? value)
	{
		return new ProgressBar
		{
			Minimum = 0.0,
			Maximum = 100.0,
			Value = (value.HasValue ? Math.Clamp(value.Value, 0.0, 100.0) : 0.0),
			IsIndeterminate = !value.HasValue,
			Height = 10.0,
			Width = 220.0,
			Margin = new Thickness(0.0, 10.0, 0.0, 10.0),
			Foreground = new SolidColorBrush(Color.FromRgb(47, 140, byte.MaxValue)),
			Background = new SolidColorBrush(Color.FromRgb(17, 27, 42)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(49, 69, 95))
		};
	}

	private UIElement CreateDownloadedUpdateButtons(string filePath)
	{
		StackPanel obj = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
		};
		Button button = new Button
		{
			Content = "Открыть папку с файлом",
			MinWidth = 190.0,
			Padding = new Thickness(16.0, 9.0, 16.0, 9.0),
			Margin = new Thickness(6.0, 2.0, 6.0, 8.0)
		};
		button.Click += delegate
		{
			UpdateService.OpenFileInExplorer(filePath);
		};
		Button button2 = new Button
		{
			Content = "Открыть архив",
			MinWidth = 150.0,
			Padding = new Thickness(16.0, 9.0, 16.0, 9.0),
			Margin = new Thickness(6.0, 2.0, 6.0, 8.0)
		};
		button2.Click += delegate
		{
			UpdateService.OpenArchive(filePath);
		};
		obj.Children.Add(button);
		obj.Children.Add(button2);
		return obj;
	}

	private static UIElement TextNote(string text)
	{
		return new TextBlock
		{
			Text = text,
			TextWrapping = TextWrapping.Wrap,
			Foreground = new SolidColorBrush(SettingsMutedTextColor)
		};
	}

	private static UIElement Row(string label, params UIElement[] controls)
	{
		return Row(CreateLabel(label), controls);
	}

	private static UIElement Row(UIElement label, params UIElement[] controls)
	{
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(230.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.Children.Add(label);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		foreach (UIElement element in controls)
		{
			stackPanel.Children.Add(element);
		}
		Grid.SetColumn(stackPanel, 1);
		grid.Children.Add(stackPanel);
		return grid;
	}

	private static TextBlock CreateLabel(string label)
	{
		return new TextBlock
		{
			Text = label,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = SettingsOptionTextBrush()
		};
	}

	private static SolidColorBrush SettingsOptionTextBrush()
	{
		return new SolidColorBrush(SettingsOptionTextColor);
	}

	private static UIElement CreateLabelWithHelp(string label, string helpText)
	{
		return new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
			Children = 
			{
				(UIElement)CreateLabel(label),
				(UIElement)CreateHelpButton(helpText)
			}
		};
	}

	private static Button CreateHelpButton(string helpText)
	{
		ToolTip toolTip = new ToolTip
		{
			Content = helpText,
			Placement = PlacementMode.MousePoint
		};
		Button button = new Button();
		button.Content = "?";
		button.Width = 20.0;
		button.Height = 20.0;
		button.Padding = new Thickness(0.0);
		button.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
		button.BorderThickness = new Thickness(1.0);
		button.BorderBrush = new SolidColorBrush(Color.FromRgb(88, 166, byte.MaxValue));
		button.Background = new SolidColorBrush(Color.FromArgb(70, 88, 166, byte.MaxValue));
		button.Foreground = new SolidColorBrush(Color.FromRgb(234, 242, byte.MaxValue));
		button.FontWeight = FontWeights.Bold;
		button.ToolTip = toolTip;
		button.Click += delegate
		{
			toolTip.IsOpen = false;
			toolTip.IsOpen = true;
		};
		button.MouseLeave += delegate
		{
			if (!toolTip.IsMouseOver)
			{
				toolTip.IsOpen = false;
			}
		};
		toolTip.MouseLeave += delegate
		{
			toolTip.IsOpen = false;
		};
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
		frameworkElementFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
		frameworkElementFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
		frameworkElementFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(10.0));
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(ContentPresenter));
		frameworkElementFactory2.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
		frameworkElementFactory2.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		button.Template = new ControlTemplate(typeof(Button))
		{
			VisualTree = frameworkElementFactory
		};
		ToolTipService.SetInitialShowDelay(button, 1000);
		return button;
	}

	private void Changed()
	{
		if (!_building)
		{
			UpdatePreview();
		}
	}

	private void UpdatePreview()
	{
		PreviewRoot.CornerRadius = new CornerRadius(_draft.Appearance.CornerRadius);
		PreviewRoot.Background = BrushFromHex(WithOpacity(_draft.Appearance.BackgroundColor, _draft.Appearance.BackgroundOpacity), "#D0080808");
		PreviewRoot.LayoutTransform = Transform.Identity;
		PreviewRoot.Width = GetPreviewOverlayContentWidth();
		if (_draft.OverlayDisplayMode.Equals("Line", StringComparison.OrdinalIgnoreCase))
		{
			PreviewRoot.Width = double.NaN;
			PreviewStack.Children.Clear();
			PreviewStack.Children.Add(new TextBlock
			{
				Text = "Для оверлея выбран режим «Строка», предпросмотр недоступен.",
				TextWrapping = TextWrapping.Wrap,
				TextAlignment = TextAlignment.Center,
				Foreground = SettingsOptionTextBrush(),
				FontSize = 14.0,
				Margin = new Thickness(6.0, 18.0, 6.0, 18.0)
			});
			return;
		}
		RestorePreviewChildren();
		PreviewTime.Text = DateTime.Now.ToString("HH:mm:ss");
		PreviewTime.Visibility = ToVisibility(_draft.ShowTime);
		PreviewBrandPanel.Visibility = ToVisibility(_draft.Appearance.ShowBranding);
		PreviewSeparator.Visibility = ToVisibility(_draft.Appearance.ShowSeparators);
		PreviewFpsBlock.Visibility = ToVisibility(_draft.Sections.Fps);
		PreviewFpsHeader.Visibility = ToVisibility(_draft.Sections.Fps && _draft.Fps.ShowFps);
		PreviewFpsLabel.Visibility = ToVisibility(_draft.Sections.Fps && _draft.Fps.ShowFps);
		PreviewFpsValue.Visibility = ToVisibility(_draft.Sections.Fps && _draft.Fps.ShowFps);
		PreviewGame.Visibility = ToVisibility(_draft.Sections.Fps && _draft.Fps.ShowGame);
		PreviewApi.Visibility = ToVisibility(_draft.Sections.Fps && _draft.Fps.ShowApi);
		List<string> list = new List<string>();
		if (_draft.Fps.ShowAverage)
		{
			list.Add("AVG: 142");
		}
		if (_draft.Fps.ShowOnePercentLow)
		{
			list.Add("1% Low: 98");
		}
		if (_draft.Fps.ShowPointOnePercentLow)
		{
			list.Add("0.1% Low: 75");
		}
		PreviewFpsDetails.Text = string.Join("   ", list);
		PreviewFpsDetails.Visibility = ToVisibility(_draft.Sections.Fps && list.Count > 0);
		PreviewFpsDetails.FontSize = 12.0;
		PreviewFpsDetails.Foreground = BrushFromHex(_draft.Fps.TextColor, "#FFE7E7E7");
		PreviewFpsLabel.Foreground = BrushFromHex(_draft.Fps.TextColor, "#FFE7E7E7");
		PreviewFpsValue.Foreground = BrushFromHex(_draft.Fps.ValueColor, "#FFFF6A00");
		FillHardwarePreview(PreviewGpu, GetHardwarePreviewTitle(_draft.Gpu, isGpu: true, "GeForce RTX 5060 Ti"), _draft.Gpu, _draft.Sections.Gpu, new(string, string, string)[11]
		{
			("Load", "Загрузка", "74 %"),
			("Temperature", "Температура", FormatTemperaturePreview(46.0)),
			("HotspotTemperature", "Темп. горячей точки", FormatTemperaturePreview(57.0)),
			("VramTemperature", "Температура памяти", FormatTemperaturePreview(52.0)),
			("Clock", "Частота", FormatClockPreview(1687.0)),
			("MemoryClock", "Память GPU", FormatMemoryClockPreview(7001.0)),
			("Voltage", "Напряжение", "0.950 В"),
			("Power", "Потребление", "14.8 Вт"),
			("FanRpm", "Обороты вент.", "1420 Об./мин."),
			("FanPercent", "Обороты вент. в %", "38 %"),
			("Vram", "Видеопамять", FormatMemoryPairPreview(1.8, 15.9))
		});
		string hardwarePreviewTitle = GetHardwarePreviewTitle(_draft.Cpu, isGpu: false, "Ryzen 7 7800X3D");
		FillHardwarePreview(PreviewCpu, hardwarePreviewTitle, _draft.Cpu, _draft.Sections.Cpu, BuildCpuPreviewRows(hardwarePreviewTitle));
		PreviewRam.Children.Clear();
		PreviewRam.Visibility = ToVisibility(_draft.Sections.Ram && _draft.Ram.ShowBlock);
		AddPreviewTitle(PreviewRam, "ОЗУ", _draft.Ram.TitleColor);
		if (_draft.Ram.ShowUsed)
		{
			AddPreviewRow(PreviewRam, "Использовано", FormatRamPreviewUsed(), _draft.Ram.LabelColor, _draft.Ram.ValueColor);
		}
		if (_draft.Ram.ShowLoad)
		{
			AddPreviewRow(PreviewRam, "Загрузка", "44 %", _draft.Ram.LabelColor, _draft.Ram.ValueColor);
		}
		if (_draft.Ram.ShowSpeed)
		{
			AddPreviewRow(PreviewRam, "Скорость", "6000 МГц", _draft.Ram.LabelColor, _draft.Ram.ValueColor);
		}
		if (_draft.Ram.ShowTemperatures)
		{
			AddPreviewRow(PreviewRam, "Температура DIMM 1", FormatTemperaturePreview(42.0), _draft.Ram.LabelColor, _draft.Ram.ValueColor);
			AddPreviewRow(PreviewRam, "Температура DIMM 2", FormatTemperaturePreview(44.0), _draft.Ram.LabelColor, _draft.Ram.ValueColor);
		}
		AddPreviewSeparator(PreviewRam);
		PreviewStats.Children.Clear();
		PreviewStats.Visibility = ToVisibility(_draft.Sections.Statistics && _draft.Statistics.ShowBlock);
		AddPreviewTitle(PreviewStats, "Статистика", _draft.Statistics.TitleColor);
		if (_draft.Statistics.ShowGpuMinMax)
		{
			AddPreviewRow(PreviewStats, FormatStatsLabelPreview("GPU", _draft.Statistics.GpuTemperatureStatsMode), FormatTemperatureStatsPreview(46.0, 47.0, 48.0, _draft.Statistics.GpuTemperatureStatsMode), _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
		}
		if (_draft.Statistics.ShowCpuMinMax)
		{
			AddPreviewRow(PreviewStats, FormatStatsLabelPreview("CPU", _draft.Statistics.CpuTemperatureStatsMode), FormatTemperatureStatsPreview(66.0, 70.0, 73.0, _draft.Statistics.CpuTemperatureStatsMode), _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
		}
		if (_draft.Statistics.ShowVramMinMax)
		{
			AddPreviewRow(PreviewStats, FormatStatsLabelPreview("VRAM", _draft.Statistics.VramTemperatureStatsMode), FormatTemperatureStatsPreview(50.0, 52.0, 54.0, _draft.Statistics.VramTemperatureStatsMode), _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
		}
		if (_draft.Statistics.ShowHotspotMinMax)
		{
			AddPreviewRow(PreviewStats, FormatStatsLabelPreview("Hotspot", _draft.Statistics.HotspotTemperatureStatsMode), FormatTemperatureStatsPreview(56.0, 58.0, 61.0, _draft.Statistics.HotspotTemperatureStatsMode), _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
		}
		if (_draft.Statistics.ShowGpuVoltageMinMax)
		{
			AddPreviewRow(PreviewStats, FormatStatsLabelPreview("напр. GPU", _draft.Statistics.GpuVoltageStatsMode), FormatValueStatsPreview("0.900", "0.975", "1.050", "В", _draft.Statistics.GpuVoltageStatsMode), _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
		}
		if (_draft.Statistics.ShowGpuPowerMinMax)
		{
			AddPreviewRow(PreviewStats, FormatStatsLabelPreview("потр. GPU", _draft.Statistics.GpuPowerStatsMode), FormatValueStatsPreview("12.0", "76.2", "142.5", "Вт", _draft.Statistics.GpuPowerStatsMode), _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
		}
		if (_draft.Statistics.ShowCpuPowerMinMax)
		{
			AddPreviewRow(PreviewStats, FormatStatsLabelPreview("потр. CPU", _draft.Statistics.CpuPowerStatsMode), FormatValueStatsPreview("18.7", "54.1", "88.4", "Вт", _draft.Statistics.CpuPowerStatsMode), _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
		}
		if (_draft.Statistics.ShowRamTemperatureStats)
		{
			AddPreviewRow(PreviewStats, FormatStatsLabelPreview("DIMM 1", _draft.Statistics.RamTemperatureStatsMode), FormatTemperatureStatsPreview(40.0, 42.0, 45.0, _draft.Statistics.RamTemperatureStatsMode), _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
			AddPreviewRow(PreviewStats, FormatStatsLabelPreview("DIMM 2", _draft.Statistics.RamTemperatureStatsMode), FormatTemperatureStatsPreview(41.0, 44.0, 47.0, _draft.Statistics.RamTemperatureStatsMode), _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
		}
		if (_draft.Statistics.ShowStutterDetector)
		{
			AddPreviewRow(PreviewStats, "Состояние", "Плавно", _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
			AddPreviewRow(PreviewStats, "Количество статтеров", "0", _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
			AddPreviewRow(PreviewStats, "Последний статтер", "N/A", _draft.Statistics.LabelColor, _draft.Statistics.ValueColor);
		}
		AddPreviewSeparator(PreviewStats);
		PreviewGraph.Visibility = ToVisibility(_draft.Sections.FrameTimeGraph && _draft.FrameTimeGraph.ShowGraph);
		PreviewGraph.Height = _draft.FrameTimeGraph.Height;
		PreviewGraph.Background = BrushFromHex(_draft.FrameTimeGraph.BackgroundColor, "#FF000000");
		PreviewGraph.Children.Clear();
		DrawPreviewGraph();
		PreviewGraphLabel.Visibility = ToVisibility(_draft.Sections.FrameTimeGraph && _draft.FrameTimeGraph.ShowGraph && _draft.FrameTimeGraph.ShowMsLabel);
		PreviewGraphLabel.Foreground = BrushFromHex(_draft.FrameTimeGraph.Color, "#FFFF0000");
		ApplyPreviewBlockOrder();
		OverlayFontApplicator.Apply(PreviewRoot, FontLibraryService.ResolveFontInfo(GetEffectiveFontIdForSettings()), _draft.Appearance.TextScale);
	}

	private void RestorePreviewChildren()
	{
		PreviewStack.Children.Clear();
		PreviewStack.Children.Add(PreviewBrandPanel);
		PreviewStack.Children.Add(PreviewFpsBlock);
		PreviewStack.Children.Add(PreviewGpu);
		PreviewStack.Children.Add(PreviewCpu);
		PreviewStack.Children.Add(PreviewRam);
		PreviewStack.Children.Add(PreviewStats);
		PreviewStack.Children.Add(PreviewGraph);
		PreviewStack.Children.Add(PreviewGraphLabel);
	}

	private void FillHardwarePreview(StackPanel panel, string title, HardwareBlockSettings block, bool sectionVisible, IEnumerable<(string Key, string Label, string Value)> rows)
	{
		panel.Children.Clear();
		panel.Visibility = ToVisibility(sectionVisible && block.ShowBlock);
		AddPreviewTitle(panel, title, block.TitleColor);
		foreach (var (text, label, value) in rows)
		{
			if (text switch
			{
				"Load" => block.ShowLoad, 
				"Temperature" => block.ShowTemperature, 
				"HotspotTemperature" => block.ShowHotspotTemperature, 
				"VramTemperature" => block.ShowVramTemperature, 
				"Clock" => block.ShowClock, 
				"EfficiencyClock" => block.ShowClock && block.ShowEfficiencyAverageClock, 
				"MemoryClock" => block.ShowMemoryClock, 
				"Voltage" => block.ShowVoltage, 
				"Power" => block.ShowPower, 
				"FanRpm" => block.ShowFanRpm, 
				"FanPercent" => block.ShowFanPercent, 
				"Vram" => block.ShowVram, 
				_ => true, 
			})
			{
				AddPreviewRow(panel, label, value, block.LabelColor, GetPreviewHardwareValueColor(text, block, value, panel == PreviewGpu));
			}
		}
		if (panel == PreviewCpu && block.ShowCoreClocks && (block.ShowPerformanceCoreClocks || block.ShowEfficiencyCoreClocks))
		{
			AddPreviewCoreClockRows(panel, block);
		}
		if (panel == PreviewCpu && block.ShowCoreLoadGraph)
		{
			AddPreviewCoreLoadGraph(panel, block);
		}
		AddPreviewSeparator(panel);
	}

	private string GetHardwarePreviewTitle(HardwareBlockSettings block, bool isGpu, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(block.CustomName))
		{
			return block.CustomName.Trim();
		}
		string currentHardwareName = _mainWindow.GetCurrentHardwareName(isGpu);
		bool flag = string.IsNullOrWhiteSpace(currentHardwareName);
		if (!flag)
		{
			bool flag2 = ((currentHardwareName == "GPU" || currentHardwareName == "CPU") ? true : false);
			flag = flag2;
		}
		if (!flag)
		{
			return currentHardwareName;
		}
		return fallback;
	}

	private IEnumerable<(string Key, string Label, string Value)> BuildCpuPreviewRows(string title)
	{
		List<(string, string, string)> list = new List<(string, string, string)>
		{
			("Load", "Загрузка", "82 %"),
			("Temperature", "Температура", FormatTemperaturePreview(70.0))
		};
		if (IsHybridCpuPreviewTitle(title))
		{
			list.Add(("Clock", "Частота P ядер", FormatClockPreview(5040.0)));
			list.Add(("EfficiencyClock", "Частота E ядер", FormatClockPreview(3890.0)));
		}
		else
		{
			list.Add(("Clock", "Частота", FormatClockPreview(5040.0)));
		}
		list.Add(("Power", "Потребление", "31.5 Вт"));
		return list;
	}

	private static bool IsHybridCpuPreviewTitle(string title)
	{
		if (!title.Contains("Intel", StringComparison.OrdinalIgnoreCase))
		{
			return Regex.IsMatch(title, "\\b(?:i[3579][- ]?)?(?:12|13|14)\\d{3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		}
		return true;
	}

	private void AddPreviewCoreLoadGraph(StackPanel panel, HardwareBlockSettings block)
	{
		double[] array = new double[16]
		{
			12.0, 20.0, 18.0, 31.0, 24.0, 35.0, 44.0, 29.0, 38.0, 66.0,
			52.0, 25.0, 58.0, 81.0, 43.0, 32.0
		};
		CpuCoreType[] array2 = new CpuCoreType[16]
		{
			CpuCoreType.Performance,
			CpuCoreType.Performance,
			CpuCoreType.Performance,
			CpuCoreType.Performance,
			CpuCoreType.Performance,
			CpuCoreType.Performance,
			CpuCoreType.Performance,
			CpuCoreType.Performance,
			CpuCoreType.Efficiency,
			CpuCoreType.Efficiency,
			CpuCoreType.Efficiency,
			CpuCoreType.Efficiency,
			CpuCoreType.Efficiency,
			CpuCoreType.Efficiency,
			CpuCoreType.Efficiency,
			CpuCoreType.Efficiency
		};
		Canvas canvas = new Canvas
		{
			Height = (block.ShowCoreLoadGraphLabels ? 52 : 40),
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
			ClipToBounds = true
		};
		double num = ((array.Length > 24) ? 1 : 2);
		double num2 = 0.0;
		for (int i = 0; i < array.Length; i++)
		{
			CpuCoreType cpuCoreType = array2[i];
			double num3 = ((cpuCoreType == CpuCoreType.Efficiency) ? 5 : 10);
			double num4 = Math.Max(2.0, Math.Round(40.0 * array[i] / 100.0));
			Rectangle element = new Rectangle
			{
				Width = num3,
				Height = num4,
				RadiusX = 1.0,
				RadiusY = 1.0,
				Fill = PreviewCpuCoreLoadBrush(array[i])
			};
			Canvas.SetLeft(element, num2);
			Canvas.SetTop(element, 40.0 - num4);
			canvas.Children.Add(element);
			if (block.ShowCoreLoadGraphLabels)
			{
				TextBlock element2 = new TextBlock
				{
					Text = ((cpuCoreType == CpuCoreType.Efficiency) ? "E" : $"P{i / 2}"),
					Width = num3,
					Height = 11.0,
					FontSize = 7.0,
					FontWeight = FontWeights.Bold,
					TextAlignment = TextAlignment.Center,
					TextWrapping = TextWrapping.NoWrap,
					Foreground = BrushFromHex(block.LabelColor, "#FF58A6FF")
				};
				Canvas.SetLeft(element2, num2);
				Canvas.SetTop(element2, 41.0);
				canvas.Children.Add(element2);
			}
			num2 += num3 + num;
		}
		panel.Children.Add(canvas);
	}

	private static Brush PreviewCpuCoreLoadBrush(double loadPercent)
	{
		double num = Math.Clamp(loadPercent, 0.0, 100.0);
		Color start = Color.FromRgb(53, 212, 99);
		Color color = Color.FromRgb(byte.MaxValue, 224, 102);
		Color end = Color.FromRgb(byte.MaxValue, 76, 76);
		return new SolidColorBrush((num <= 50.0) ? PreviewInterpolateColor(start, color, num / 50.0) : PreviewInterpolateColor(color, end, (num - 50.0) / 50.0));
	}

	private static Color PreviewInterpolateColor(Color start, Color end, double amount)
	{
		double num = Math.Clamp(amount, 0.0, 1.0);
		return Color.FromRgb((byte)Math.Round((double)(int)start.R + (double)(end.R - start.R) * num), (byte)Math.Round((double)(int)start.G + (double)(end.G - start.G) * num), (byte)Math.Round((double)(int)start.B + (double)(end.B - start.B) * num));
	}

	private void AddPreviewCoreClockRows(StackPanel panel, HardwareBlockSettings block)
	{
		if (block.ShowPerformanceCoreClocks)
		{
			AddPreviewCoreClockHeader(panel, "P-ядра", block);
			AddPreviewCoreClockGroup(panel, block, new int[4] { 5040, 5025, 4988, 5012 }, 0);
		}
		if (block.ShowEfficiencyCoreClocks)
		{
			AddPreviewCoreClockHeader(panel, "E-ядра", block);
			AddPreviewCoreClockGroup(panel, block, new int[4] { 3890, 3865, 3820, 3844 }, 0);
		}
	}

	private static void AddPreviewCoreClockHeader(StackPanel panel, string text, HardwareBlockSettings block)
	{
		panel.Children.Add(new TextBlock
		{
			Text = text,
			FontSize = 13.0,
			FontWeight = FontWeights.Bold,
			Foreground = BrushFromHex(block.LabelColor, "#FF58A6FF"),
			Margin = new Thickness(0.0, 1.0, 0.0, 0.0),
			TextWrapping = TextWrapping.NoWrap
		});
	}

	private static void AddPreviewCoreClockGroup(StackPanel panel, HardwareBlockSettings block, IReadOnlyList<int> clocks, int firstIndex)
	{
		for (int i = 0; i < clocks.Count; i += 2)
		{
			Grid grid = new Grid
			{
				MinWidth = 294.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 1.0)
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
			TextBlock element = PreviewCoreClockText($"Ядро {firstIndex + i:00}:", block.LabelColor, FontWeights.Normal);
			TextBlock element2 = PreviewCoreClockText($"{clocks[i]:0} МГц", "#FFF2F2F2", FontWeights.Bold);
			TextBlock textBlock = PreviewCoreClockText("|", "#FFF2F2F2", FontWeights.Normal);
			textBlock.TextAlignment = TextAlignment.Center;
			TextBlock element3 = PreviewCoreClockText($"Ядро {firstIndex + i + 1:00}:", block.LabelColor, FontWeights.Normal);
			TextBlock element4 = PreviewCoreClockText($"{clocks[i + 1]:0} МГц", "#FFF2F2F2", FontWeights.Bold);
			Grid.SetColumn(element, 0);
			Grid.SetColumn(element2, 1);
			Grid.SetColumn(textBlock, 2);
			Grid.SetColumn(element3, 3);
			Grid.SetColumn(element4, 4);
			grid.Children.Add(element);
			grid.Children.Add(element2);
			grid.Children.Add(textBlock);
			grid.Children.Add(element3);
			grid.Children.Add(element4);
			panel.Children.Add(grid);
		}
	}

	private static TextBlock PreviewCoreClockText(string text, string color, FontWeight fontWeight)
	{
		return new TextBlock
		{
			Text = text,
			FontSize = 13.0,
			FontWeight = fontWeight,
			Foreground = BrushFromHex(color, "#FFF2F2F2"),
			TextWrapping = TextWrapping.NoWrap
		};
	}

	private void AddPreviewTitle(StackPanel panel, string title, string color)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 8.0, 0.0, 3.0)
		};
		if (panel != PreviewGpu && panel != PreviewCpu && !title.Equals("ОЗУ", StringComparison.OrdinalIgnoreCase) && !title.Equals("Статистика", StringComparison.OrdinalIgnoreCase))
		{
			_ = string.Empty;
		}
		stackPanel.Children.Add(new TextBlock
		{
			Text = title,
			FontWeight = FontWeights.Bold,
			FontSize = 15.0,
			Foreground = BrushFromHex(color, "#FFF2F2F2")
		});
		panel.Children.Add(stackPanel);
	}

	private void ApplyPreviewBlockOrder()
	{
		Dictionary<string, UIElement> dictionary = new Dictionary<string, UIElement>(StringComparer.OrdinalIgnoreCase)
		{
			["Fps"] = PreviewFpsBlock,
			["Gpu"] = PreviewGpu,
			["Cpu"] = PreviewCpu,
			["Ram"] = PreviewRam,
			["Statistics"] = PreviewStats,
			["FrameTimeGraph"] = PreviewGraph
		};
		foreach (UIElement value2 in dictionary.Values)
		{
			PreviewStack.Children.Remove(value2);
		}
		PreviewStack.Children.Remove(PreviewGraphLabel);
		foreach (string item in _draft.BlockOrder)
		{
			if (dictionary.TryGetValue(item, out var value) && !PreviewStack.Children.Contains(value))
			{
				PreviewStack.Children.Add(value);
				if (item.Equals("FrameTimeGraph", StringComparison.OrdinalIgnoreCase))
				{
					PreviewStack.Children.Add(PreviewGraphLabel);
				}
			}
		}
		foreach (var (text2, element) in dictionary)
		{
			if (!PreviewStack.Children.Contains(element))
			{
				PreviewStack.Children.Add(element);
				if (text2.Equals("FrameTimeGraph", StringComparison.OrdinalIgnoreCase))
				{
					PreviewStack.Children.Add(PreviewGraphLabel);
				}
			}
		}
	}

	private static void AddPreviewRow(StackPanel panel, string label, string value, string labelColor, string valueColor)
	{
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.Children.Add(new TextBlock
		{
			Text = label,
			FontFamily = new FontFamily("Segoe UI"),
			FontSize = 13.0,
			Foreground = BrushFromHex(labelColor, "#FFF2F2F2")
		});
		TextBlock element = new TextBlock
		{
			Text = value,
			FontFamily = new FontFamily("Segoe UI"),
			FontWeight = FontWeights.Bold,
			FontSize = 13.0,
			Foreground = BrushFromHex(valueColor, "#FFF2F2F2"),
			TextAlignment = TextAlignment.Right
		};
		Grid.SetColumn(element, 1);
		grid.Children.Add(element);
		panel.Children.Add(grid);
	}

	private static string GetPreviewHardwareValueColor(string key, HardwareBlockSettings block, string value, bool isGpu)
	{
		double? value2 = TryReadLeadingNumber(value);
		switch (key)
		{
		case "Load":
			return GetPreviewLoadColor(value2, block, isGpu);
		case "Temperature":
		case "HotspotTemperature":
		case "VramTemperature":
			return GetPreviewTemperatureColor(value2, block);
		default:
			return block.ValueColor;
		}
	}

	private static string GetPreviewTemperatureColor(double? value, HardwareBlockSettings block)
	{
		if (value >= (double)block.CriticalTemperatureC)
		{
			return "#FFFF4C4C";
		}
		if (value >= (double)block.WarningTemperatureC)
		{
			return "#FFFFA64D";
		}
		return block.ValueColor;
	}

	private static string GetPreviewLoadColor(double? value, HardwareBlockSettings block, bool isGpu)
	{
		if (!block.UseLoadGradient || !value.HasValue || !(value.GetValueOrDefault() >= 0.0))
		{
			return block.ValueColor;
		}
		Color target = (isGpu ? Color.FromRgb(53, 212, 99) : Color.FromRgb(byte.MaxValue, 76, 76));
		Color color = InterpolateLoadColor(value.Value, target);
		return $"#FF{color.R:X2}{color.G:X2}{color.B:X2}";
	}

	private static Color InterpolateLoadColor(double loadPercent, Color target)
	{
		Color color = Color.FromRgb(242, 242, 242);
		double num = Math.Clamp((loadPercent - 50.0) / 50.0, 0.0, 1.0);
		return Color.FromRgb((byte)Math.Round((double)(int)color.R + (double)(target.R - color.R) * num), (byte)Math.Round((double)(int)color.G + (double)(target.G - color.G) * num), (byte)Math.Round((double)(int)color.B + (double)(target.B - color.B) * num));
	}

	private static double? TryReadLeadingNumber(string value)
	{
		string text = value.Trim().Replace(',', '.');
		int i;
		for (i = 0; i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'); i++)
		{
		}
		if (i <= 0 || !double.TryParse(text.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return null;
		}
		return result;
	}

	private void AddPreviewSeparator(StackPanel panel)
	{
		panel.Children.Add(new Rectangle
		{
			Height = 1.0,
			Fill = new SolidColorBrush(Color.FromArgb(51, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			Visibility = ToVisibility(_draft.Appearance.ShowSeparators)
		});
	}

	private void DrawPreviewGraph()
	{
		double num = Math.Max(1.0, PreviewGraph.Height);
		double num2 = 330.0;
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			for (int i = 0; i < 120; i++)
			{
				double x = (double)i * num2 / 119.0;
				bool flag = ((i == 12 || i == 70 || i == 95) ? true : false);
				double num3 = (flag ? 24.0 : (5.0 + Math.Sin((double)i / 7.0) * 1.5));
				double y = num - Math.Clamp(num3 / _draft.FrameTimeGraph.MaxMs, 0.0, 1.0) * num;
				streamGeometryContext.BeginFigure(new Point(x, num), isFilled: false, isClosed: false);
				streamGeometryContext.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: false);
			}
		}
		streamGeometry.Freeze();
		PreviewGraph.Children.Add(new Path
		{
			Data = streamGeometry,
			Stroke = BrushFromHex(_draft.FrameTimeGraph.Color, "#FFFF0000"),
			StrokeThickness = 1.0
		});
	}

	private static Visibility ToVisibility(bool value)
	{
		if (!value)
		{
			return Visibility.Collapsed;
		}
		return Visibility.Visible;
	}

	private static Brush BrushFromHex(string hex, string fallback)
	{
		try
		{
			return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		}
		catch
		{
			try
			{
				return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
			}
			catch
			{
				return Brushes.White;
			}
		}
	}

	private static string WithOpacity(string colorText, int opacity)
	{
		try
		{
			Color color = (Color)ColorConverter.ConvertFromString(colorText);
			return Color.FromArgb((byte)Math.Clamp(opacity * 255 / 100, 0, 255), color.R, color.G, color.B).ToString();
		}
		catch
		{
			return "#D0080808";
		}
	}

	private string FormatRamPreviewUsed()
	{
		if (_draft.Ram.MemoryFormat == "UsedOnly")
		{
			return FormatMemoryAmountPreview(13.7);
		}
		return FormatMemoryPairPreview(13.7, 31.1);
	}

	private string FormatMemoryPairPreview(double usedGb, double totalGb)
	{
		return $"{FormatMemoryAmountValuePreview(usedGb)} / {FormatMemoryAmountValuePreview(totalGb)} {GetMemoryUnitPreview()}";
	}

	private string FormatMemoryAmountPreview(double gb)
	{
		return FormatMemoryAmountValuePreview(gb) + " " + GetMemoryUnitPreview();
	}

	private string FormatMemoryAmountValuePreview(double gb)
	{
		if (!(_draft.MemoryUnit == "МБ") && !_draft.MemoryUnit.Equals("MB", StringComparison.OrdinalIgnoreCase))
		{
			return $"{gb:0.0}";
		}
		return $"{gb * 1024.0:0}";
	}

	private string GetMemoryUnitPreview()
	{
		if (!(_draft.MemoryUnit == "МБ") && !_draft.MemoryUnit.Equals("MB", StringComparison.OrdinalIgnoreCase))
		{
			return "ГБ";
		}
		return "МБ";
	}

	private string FormatTemperaturePreview(double valueC)
	{
		if (!_draft.TemperatureUnit.Equals("F", StringComparison.OrdinalIgnoreCase))
		{
			return $"{valueC:0} °C";
		}
		return $"{valueC * 9.0 / 5.0 + 32.0:0} °F";
	}

	private string FormatMinMaxTemperaturePreview(double minValueC, double maxValueC)
	{
		if (!_draft.TemperatureUnit.Equals("F", StringComparison.OrdinalIgnoreCase))
		{
			return $"{minValueC:0} / {maxValueC:0} °C";
		}
		return $"{minValueC * 9.0 / 5.0 + 32.0:0} / {maxValueC * 9.0 / 5.0 + 32.0:0} °F";
	}

	private string FormatTemperatureStatsPreview(double minValueC, double averageValueC, double maxValueC, string mode)
	{
		return FormatValueStatsPreview(FormatTemperatureValuePreview(minValueC), FormatTemperatureValuePreview(averageValueC), FormatTemperatureValuePreview(maxValueC), GetTemperatureUnitPreview(), mode);
	}

	private static string FormatValueStatsPreview(string minValue, string averageValue, string maxValue, string unit, string mode)
	{
		string text = StatisticsDisplayModes.Normalize(mode);
		if (!(text == "Average"))
		{
			if (text == "MinAverageMax")
			{
				return $"{minValue} / {averageValue} / {maxValue} {unit}";
			}
			return $"{minValue} / {maxValue} {unit}";
		}
		return averageValue + " " + unit;
	}

	private static string FormatStatsLabelPreview(string name, string mode)
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

	private string FormatTemperatureValuePreview(double valueC)
	{
		if (_draft.TemperatureUnit.Equals("F", StringComparison.OrdinalIgnoreCase))
		{
			return $"{valueC * 9.0 / 5.0 + 32.0:0}";
		}
		return $"{valueC:0}";
	}

	private string GetTemperatureUnitPreview()
	{
		if (!_draft.TemperatureUnit.Equals("F", StringComparison.OrdinalIgnoreCase))
		{
			return "°C";
		}
		return "°F";
	}

	private string FormatClockPreview(double valueMhz)
	{
		if (!_draft.ClockUnit.Equals("ГГц", StringComparison.OrdinalIgnoreCase) && !_draft.ClockUnit.Equals("GHz", StringComparison.OrdinalIgnoreCase))
		{
			return $"{valueMhz:0} МГц";
		}
		return $"{valueMhz / 1000.0:0.0} ГГц";
	}

	private static string FormatMemoryClockPreview(double valueMhz)
	{
		return $"{valueMhz:0} МГц";
	}

	private static string GetHotKeyDisplayValue(string key)
	{
		return key switch
		{
			"PageUp" => "Page Up", 
			"PageDown" => "Page Down", 
			"Delete" => "Del", 
			_ => key, 
		};
	}

	private static string FormatPercentScale(double value)
	{
		return $"{value * 100.0:0}%";
	}

	private double GetPreviewOverlayContentWidth()
	{
		double num = Math.Clamp(_draft.OverlayWidthPercent, 50.0, 100.0) / 100.0;
		return Math.Max(165.0, 330.0 * num);
	}
}
