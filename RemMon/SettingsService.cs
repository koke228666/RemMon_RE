using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace RemMon;

public sealed class SettingsService
{
	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string RunValueName = "RemMon";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private readonly string _settingsPath;

	public string SettingsPath => _settingsPath;

	public SettingsService()
	{
		string text = AppLaunchOptions.SettingsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RemMon");
		Directory.CreateDirectory(text);
		_settingsPath = Path.Combine(text, "settings.json");
	}

	public OverlaySettings Load()
	{
		try
		{
			if (!File.Exists(_settingsPath))
			{
				return CreateDefaultSettings();
			}
			string json = File.ReadAllText(_settingsPath);
			OverlaySettings overlaySettings = JsonSerializer.Deserialize<OverlaySettings>(json, JsonOptions);
			using (JsonDocument jsonDocument = JsonDocument.Parse(json))
			{
				if (overlaySettings != null && !jsonDocument.RootElement.TryGetProperty("RemindUpdatesOnStartup", out var _))
				{
					overlaySettings.RemindUpdatesOnStartup = true;
				}
			}
			return Normalize(overlaySettings ?? CreateDefaultSettings());
		}
		catch
		{
			return CreateDefaultSettings();
		}
	}

	public OverlaySettings Save(OverlaySettings settings)
	{
		OverlaySettings overlaySettings = Normalize(settings.Clone());
		File.WriteAllText(_settingsPath, JsonSerializer.Serialize(overlaySettings, JsonOptions));
		ApplyStartupRegistration(overlaySettings.StartWithWindows);
		return overlaySettings;
	}

	public void ExportToFile(OverlaySettings settings, string path)
	{
		OverlaySettings value = Normalize(settings.Clone());
		File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
	}

	public ImportedSettingsResult ImportFromFile(string path)
	{
		string json = File.ReadAllText(path);
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		using JsonDocument jsonDocument2 = JsonDocument.Parse(JsonSerializer.Serialize(CreateDefaultSettings(), JsonOptions));
		bool hasExtraProperties = HasExtraProperties(jsonDocument.RootElement, jsonDocument2.RootElement);
		bool hasMissingProperties = HasMissingProperties(jsonDocument.RootElement, jsonDocument2.RootElement);
		return new ImportedSettingsResult(Normalize(JsonSerializer.Deserialize<OverlaySettings>(json, JsonOptions) ?? throw new InvalidDataException("Файл не содержит настройки RemMon.")), hasExtraProperties, hasMissingProperties);
	}

	public OverlaySettings CreateDefaultSettings()
	{
		return new OverlaySettings
		{
			StartWithWindows = IsStartupRegistrationEnabled()
		};
	}

	public static OverlaySettings Clone(OverlaySettings settings)
	{
		return JsonSerializer.Deserialize<OverlaySettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions) ?? new OverlaySettings();
	}

	public void ApplyStartupRegistration(bool enabled)
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey == null)
			{
				return;
			}
			if (!enabled)
			{
				registryKey.DeleteValue("RemMon", throwOnMissingValue: false);
				return;
			}
			string text = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(text))
			{
				registryKey.SetValue("RemMon", "\"" + text + "\"");
			}
		}
		catch
		{
		}
	}

	private static bool IsStartupRegistrationEnabled()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
			return registryKey?.GetValue("RemMon") is string text && text.Length > 0;
		}
		catch
		{
			return false;
		}
	}

	private static OverlaySettings Normalize(OverlaySettings settings)
	{
		OverlaySettings overlaySettings = settings;
		if (overlaySettings.Sections == null)
		{
			SectionVisibility sectionVisibility = (overlaySettings.Sections = new SectionVisibility());
		}
		overlaySettings = settings;
		if (overlaySettings.Fps == null)
		{
			FpsSettings fpsSettings = (overlaySettings.Fps = new FpsSettings());
		}
		overlaySettings = settings;
		if (overlaySettings.Gpu == null)
		{
			HardwareBlockSettings hardwareBlockSettings = (overlaySettings.Gpu = HardwareBlockSettings.CreateGpuDefaults());
		}
		overlaySettings = settings;
		if (overlaySettings.Cpu == null)
		{
			HardwareBlockSettings hardwareBlockSettings = (overlaySettings.Cpu = HardwareBlockSettings.CreateCpuDefaults());
		}
		overlaySettings = settings;
		if (overlaySettings.Ram == null)
		{
			RamSettings ramSettings = (overlaySettings.Ram = new RamSettings());
		}
		overlaySettings = settings;
		if (overlaySettings.Statistics == null)
		{
			StatisticsSettings statisticsSettings = (overlaySettings.Statistics = new StatisticsSettings());
		}
		overlaySettings = settings;
		if (overlaySettings.FrameTimeGraph == null)
		{
			FrameTimeGraphSettings frameTimeGraphSettings = (overlaySettings.FrameTimeGraph = new FrameTimeGraphSettings());
		}
		overlaySettings = settings;
		if (overlaySettings.Appearance == null)
		{
			AppearanceSettings appearanceSettings = (overlaySettings.Appearance = new AppearanceSettings());
		}
		overlaySettings = settings;
		HotKeySettings hotKeySettings;
		if (overlaySettings.HotKeys == null)
		{
			hotKeySettings = (overlaySettings.HotKeys = new HotKeySettings());
		}
		hotKeySettings = settings.HotKeys;
		if (hotKeySettings.ToggleOverlay == null)
		{
			HotKeySettings hotKeySettings3 = hotKeySettings;
			HotKeyDefinition obj = new HotKeyDefinition
			{
				Key = "F8",
				Alt = true
			};
			HotKeyDefinition hotKeyDefinition = obj;
			hotKeySettings3.ToggleOverlay = obj;
		}
		hotKeySettings = settings.HotKeys;
		if (hotKeySettings.ResetStatistics == null)
		{
			HotKeySettings hotKeySettings4 = hotKeySettings;
			HotKeyDefinition obj2 = new HotKeyDefinition
			{
				Key = "F9",
				Alt = true
			};
			HotKeyDefinition hotKeyDefinition = obj2;
			hotKeySettings4.ResetStatistics = obj2;
		}
		hotKeySettings = settings.HotKeys;
		if (hotKeySettings.ToggleOverlayMode == null)
		{
			HotKeySettings hotKeySettings5 = hotKeySettings;
			HotKeyDefinition obj3 = new HotKeyDefinition
			{
				Key = "F7",
				Alt = true
			};
			HotKeyDefinition hotKeyDefinition = obj3;
			hotKeySettings5.ToggleOverlayMode = obj3;
		}
		settings.BlockOrder = NormalizeBlockOrder(settings.BlockOrder);
		settings.OffsetX = Math.Clamp(settings.OffsetX, 0, 2000);
		settings.OffsetY = Math.Clamp(settings.OffsetY, 0, 2000);
		if (settings.OverlayWidthPercent <= 0.0)
		{
			settings.OverlayWidthPercent = 100.0;
		}
		settings.OverlayWidthPercent = Math.Clamp(settings.OverlayWidthPercent, 50.0, 100.0);
		settings.AnchorTarget = NormalizeAnchorTarget(settings.AnchorTarget);
		settings.OverlayDisplayMode = NormalizeOverlayDisplayMode(settings.OverlayDisplayMode);
		settings.SettingsWindowWidth = Math.Clamp(settings.SettingsWindowWidth, 1000.0, 3000.0);
		settings.SettingsWindowHeight = Math.Clamp(settings.SettingsWindowHeight, 720.0, 2200.0);
		settings.SettingsWindowLeft = NormalizeWindowCoordinate(settings.SettingsWindowLeft);
		settings.SettingsWindowTop = NormalizeWindowCoordinate(settings.SettingsWindowTop);
		if (settings.Fps.HideUnavailableMetrics)
		{
			settings.Fps.HideUnavailableFpsMetrics = true;
			settings.Fps.HideUnavailableMetrics = false;
		}
		settings.Fps.TextSize = Math.Clamp(settings.Fps.TextSize, 8, 32);
		settings.Fps.UpdateIntervalMs = NormalizeUpdateInterval(settings.Fps.UpdateIntervalMs);
		settings.TemperatureUnit = NormalizeTemperatureUnit(settings.TemperatureUnit);
		settings.ClockUnit = NormalizeClockUnit(settings.ClockUnit);
		settings.MemoryUnit = NormalizeRamUnit(settings.MemoryUnit);
		settings.Appearance.Theme = NormalizeTheme(settings.Appearance.Theme);
		settings.Gpu.TemperatureUnit = NormalizeTemperatureUnit(settings.Gpu.TemperatureUnit);
		settings.Cpu.TemperatureUnit = NormalizeTemperatureUnit(settings.Cpu.TemperatureUnit);
		settings.Gpu.ClockUnit = NormalizeClockUnit(settings.Gpu.ClockUnit);
		settings.Cpu.ClockUnit = NormalizeClockUnit(settings.Cpu.ClockUnit);
		settings.Cpu.ShowVoltage = false;
		if (!settings.Cpu.ShowCoreLoadGraph)
		{
			settings.Cpu.ShowCoreLoadGraphLabels = false;
		}
		if (!settings.Cpu.ShowCoreClocks)
		{
			settings.Cpu.ShowPerformanceCoreClocks = false;
			settings.Cpu.ShowEfficiencyCoreClocks = false;
		}
		else if (!settings.Cpu.ShowPerformanceCoreClocks && !settings.Cpu.ShowEfficiencyCoreClocks)
		{
			settings.Cpu.ShowCoreClocks = false;
		}
		settings.Appearance.BackgroundOpacity = Math.Clamp(settings.Appearance.BackgroundOpacity, 0, 100);
		settings.Appearance.CornerRadius = Math.Clamp(settings.Appearance.CornerRadius, 0, 32);
		settings.Appearance.TextScale = Math.Clamp(settings.Appearance.TextScale, 0.5, 3.0);
		settings.Appearance.FontId = FontLibraryService.NormalizeFontId(settings.Appearance.FontId);
		settings.FrameTimeGraph.Height = Math.Clamp(settings.FrameTimeGraph.Height, 20, 140);
		settings.FrameTimeGraph.FillOpacity = Math.Clamp(settings.FrameTimeGraph.FillOpacity, 0, 100);
		settings.FrameTimeGraph.MaxMs = Math.Clamp(settings.FrameTimeGraph.MaxMs, 8.0, 200.0);
		settings.Ram.Unit = NormalizeRamUnit(settings.Ram.Unit);
		settings.Ram.MemoryFormat = NormalizeMemoryFormat(settings.Ram.MemoryFormat);
		settings.Gpu.RowOrder = NormalizeRowOrder(settings.Gpu.RowOrder, HardwareBlockSettings.CreateGpuDefaults().RowOrder);
		settings.Cpu.RowOrder = NormalizeRowOrder(settings.Cpu.RowOrder, HardwareBlockSettings.CreateCpuDefaults().RowOrder);
		settings.Ram.RowOrder = NormalizeRowOrder(settings.Ram.RowOrder, new List<string> { "Used", "Load", "Speed" });
		return settings;
	}

	private static List<string> NormalizeBlockOrder(List<string>? order)
	{
		return NormalizeRowOrder(order, new List<string> { "Fps", "Gpu", "Cpu", "Ram", "Statistics", "FrameTimeGraph" });
	}

	private static List<string> NormalizeRowOrder(List<string>? order, List<string> defaults)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(defaults, StringComparer.OrdinalIgnoreCase);
		if (order != null)
		{
			foreach (string item in order)
			{
				string text = defaults.FirstOrDefault((string value) => value.Equals(item, StringComparison.OrdinalIgnoreCase));
				if (text != null && hashSet.Contains(text) && !list.Contains<string>(text, StringComparer.OrdinalIgnoreCase))
				{
					list.Add(text);
				}
			}
		}
		foreach (string @default in defaults)
		{
			if (!list.Contains<string>(@default, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(@default);
			}
		}
		return list;
	}

	private static string NormalizeRamUnit(string? unit)
	{
		string text = unit?.Trim().ToUpperInvariant();
		if (text == "MB" || text == "МБ")
		{
			return "МБ";
		}
		return "ГБ";
	}

	private static string NormalizeAnchorTarget(string? target)
	{
		if (target == "ActiveMonitor" || target == "ActiveWindow")
		{
			return target;
		}
		return "PrimaryMonitor";
	}

	private static string NormalizeOverlayDisplayMode(string? mode)
	{
		string text = mode?.Trim();
		if (text == "Line" || text == "Строка")
		{
			return "Line";
		}
		return "Normal";
	}

	private static double NormalizeWindowCoordinate(double value)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			return -1.0;
		}
		return Math.Clamp(value, -1.0, 10000.0);
	}

	private static string NormalizeTemperatureUnit(string? unit)
	{
		switch (unit?.Trim().ToUpperInvariant())
		{
		case "F":
		case "°F":
		case "FAHRENHEIT":
			return "F";
		default:
			return "C";
		}
	}

	private static string NormalizeClockUnit(string? unit)
	{
		string text = unit?.Trim().ToUpperInvariant();
		if (text == "GHZ" || text == "ГГЦ")
		{
			return "ГГц";
		}
		return "МГц";
	}

	private static string NormalizeTheme(string? theme)
	{
		switch (theme)
		{
		case "Darker":
		case "Graphite":
		case "Midnight":
		case "Emerald":
		case "Sapphire":
		case "Amber":
			return theme;
		default:
			return "Dark";
		}
	}

	private static string NormalizeMemoryFormat(string? format)
	{
		if (format == "UsedOnly")
		{
			return "UsedOnly";
		}
		return "UsedTotal";
	}

	private static int NormalizeUpdateInterval(int value)
	{
		if (value <= 375)
		{
			if (value <= 175)
			{
				return 100;
			}
			return 250;
		}
		if (value <= 750)
		{
			return 500;
		}
		return 1000;
	}

	private static bool HasExtraProperties(JsonElement imported, JsonElement defaults)
	{
		if (imported.ValueKind != JsonValueKind.Object || defaults.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonProperty item in defaults.EnumerateObject())
		{
			hashSet.Add(item.Name);
		}
		foreach (JsonProperty item2 in imported.EnumerateObject())
		{
			if (!hashSet.Contains(item2.Name))
			{
				return true;
			}
			if (defaults.TryGetProperty(item2.Name, out var value) && HasExtraProperties(item2.Value, value))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasMissingProperties(JsonElement imported, JsonElement defaults)
	{
		if (imported.ValueKind != JsonValueKind.Object || defaults.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonProperty item in imported.EnumerateObject())
		{
			hashSet.Add(item.Name);
		}
		foreach (JsonProperty item2 in defaults.EnumerateObject())
		{
			if (!hashSet.Contains(item2.Name))
			{
				return true;
			}
			if (imported.TryGetProperty(item2.Name, out var value) && HasMissingProperties(value, item2.Value))
			{
				return true;
			}
		}
		return false;
	}
}
