using System.Collections.Generic;

namespace RemMon;

public sealed class HardwareBlockSettings
{
	public bool ShowBlock { get; set; } = true;

	public bool ShowLoad { get; set; } = true;

	public bool ShowTemperature { get; set; } = true;

	public bool ShowHotspotTemperature { get; set; }

	public bool ShowVramTemperature { get; set; }

	public bool ShowClock { get; set; } = true;

	public bool ShowMemoryClock { get; set; } = true;

	public bool ShowVoltage { get; set; } = true;

	public bool ShowPower { get; set; } = true;

	public bool ShowEfficiencyAverageClock { get; set; } = true;

	public bool ShowCoreLoadGraph { get; set; }

	public bool ShowCoreLoadGraphLabels { get; set; } = true;

	public bool ShowCoreClocks { get; set; } = true;

	public bool ShowPerformanceCoreClocks { get; set; } = true;

	public bool ShowEfficiencyCoreClocks { get; set; } = true;

	public bool ShowFanRpm { get; set; }

	public bool ShowFanPercent { get; set; }

	public bool ShowVram { get; set; } = true;

	public string TitleColor { get; set; } = "#FF35D463";

	public string LabelColor { get; set; } = "#FF35D463";

	public string ValueColor { get; set; } = "#FFF2F2F2";

	public string CoreLoadGraphColor { get; set; } = "#FF58A6FF";

	public string CustomName { get; set; } = string.Empty;

	public string TemperatureUnit { get; set; } = "C";

	public string ClockUnit { get; set; } = "МГц";

	public int WarningTemperatureC { get; set; } = 75;

	public int CriticalTemperatureC { get; set; } = 85;

	public bool UseLoadGradient { get; set; } = true;

	public List<string> RowOrder { get; set; } = new List<string>();

	public static HardwareBlockSettings CreateGpuDefaults()
	{
		return new HardwareBlockSettings
		{
			ShowVoltage = false,
			RowOrder = new List<string>
			{
				"Load", "Temperature", "HotspotTemperature", "VramTemperature", "Clock", "MemoryClock", "Voltage", "Power", "FanRpm", "FanPercent",
				"Vram"
			}
		};
	}

	public static HardwareBlockSettings CreateCpuDefaults()
	{
		return new HardwareBlockSettings
		{
			TitleColor = "#FF58A6FF",
			LabelColor = "#FF58A6FF",
			ShowMemoryClock = false,
			ShowVoltage = false,
			ShowEfficiencyAverageClock = true,
			ShowCoreLoadGraphLabels = true,
			ShowCoreClocks = false,
			ShowPerformanceCoreClocks = false,
			ShowEfficiencyCoreClocks = false,
			ShowVram = false,
			RowOrder = new List<string> { "Load", "Temperature", "Clock", "Power", "CoreLoadGraph", "FanRpm", "FanPercent" }
		};
	}
}
