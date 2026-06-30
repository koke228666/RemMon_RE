namespace RemMon;

public sealed class StatisticsSettings
{
	public bool ShowBlock { get; set; }

	public bool ShowGpuMinMax { get; set; }

	public bool ShowCpuMinMax { get; set; }

	public bool ShowVramMinMax { get; set; }

	public bool ShowHotspotMinMax { get; set; }

	public bool ShowGpuVoltageMinMax { get; set; }

	public bool ShowGpuPowerMinMax { get; set; }

	public bool ShowCpuPowerMinMax { get; set; }

	public bool ShowRamTemperatureStats { get; set; }

	public string GpuTemperatureStatsMode { get; set; } = "MinMax";

	public string CpuTemperatureStatsMode { get; set; } = "MinMax";

	public string VramTemperatureStatsMode { get; set; } = "MinMax";

	public string HotspotTemperatureStatsMode { get; set; } = "MinMax";

	public string GpuVoltageStatsMode { get; set; } = "MinMax";

	public string GpuPowerStatsMode { get; set; } = "MinMax";

	public string CpuPowerStatsMode { get; set; } = "MinMax";

	public string RamTemperatureStatsMode { get; set; } = "MinAverageMax";

	public bool ShowStutterDetector { get; set; }

	public bool ReduceStutterDetectorSensitivity { get; set; }

	public bool DiagnosticLoggingEnabled { get; set; }

	public bool ResetOnGameChange { get; set; } = true;

	public string Position { get; set; } = "BelowRam";

	public string TitleColor { get; set; } = "#FFFFA64D";

	public string LabelColor { get; set; } = "#FFFFA64D";

	public string ValueColor { get; set; } = "#FFF2F2F2";

	public int StutterFrameThresholdMs { get; set; } = 50;
}
