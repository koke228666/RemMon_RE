using System.Collections.Generic;

namespace RemMon;

public sealed class OverlaySettings
{
	public bool OverlayEnabled { get; set; } = true;

	public bool StartWithWindows { get; set; }

	public bool ShowTime { get; set; }

	public bool GameOnlyMode { get; set; }

	public bool RemindUpdatesOnStartup { get; set; } = true;

	public bool StartupWelcomeShown { get; set; }

	public int StartupWelcomeVersion { get; set; }

	public string OverlayDisplayMode { get; set; } = "Normal";

	public double OverlayWidthPercent { get; set; } = 100.0;

	public string DisplayScale { get; set; } = "FHD";

	public string TemperatureUnit { get; set; } = "C";

	public string ClockUnit { get; set; } = "МГц";

	public string MemoryUnit { get; set; } = "ГБ";

	public string AnchorTarget { get; set; } = "PrimaryMonitor";

	public string Position { get; set; } = "TopLeft";

	public int OffsetX { get; set; } = 10;

	public int OffsetY { get; set; } = 10;

	public double SettingsWindowWidth { get; set; } = 1180.0;

	public double SettingsWindowHeight { get; set; } = 860.0;

	public double SettingsWindowLeft { get; set; } = -1.0;

	public double SettingsWindowTop { get; set; } = -1.0;

	public List<string> BlockOrder { get; set; } = new List<string> { "Fps", "Gpu", "Cpu", "Ram", "Statistics", "FrameTimeGraph" };

	public SectionVisibility Sections { get; set; } = new SectionVisibility();

	public FpsSettings Fps { get; set; } = new FpsSettings();

	public HardwareBlockSettings Gpu { get; set; } = HardwareBlockSettings.CreateGpuDefaults();

	public HardwareBlockSettings Cpu { get; set; } = HardwareBlockSettings.CreateCpuDefaults();

	public RamSettings Ram { get; set; } = new RamSettings();

	public StatisticsSettings Statistics { get; set; } = new StatisticsSettings();

	public FrameTimeGraphSettings FrameTimeGraph { get; set; } = new FrameTimeGraphSettings();

	public AppearanceSettings Appearance { get; set; } = new AppearanceSettings();

	public HotKeySettings HotKeys { get; set; } = new HotKeySettings();

	public OverlaySettings Clone()
	{
		return SettingsService.Clone(this);
	}
}
