using System;
using System.Collections.Generic;

namespace RemMon;

internal sealed class HardwareSnapshot
{
	public string CpuName { get; init; } = "CPU";

	public double? CpuLoadPercent { get; init; }

	public double? CpuTemperatureC { get; init; }

	public double? CpuClockMhz { get; init; }

	public double? CpuPerformanceClockMhz { get; init; }

	public double? CpuEfficiencyClockMhz { get; init; }

	public double? CpuPowerW { get; init; }

	public IReadOnlyList<double> CpuLogicalThreadLoads { get; init; } = Array.Empty<double>();

	public IReadOnlyList<double> CpuPhysicalCoreLoads { get; init; } = Array.Empty<double>();

	public IReadOnlyList<CpuCoreLoadGraphBar> CpuCoreLoadGraphBars { get; init; } = Array.Empty<CpuCoreLoadGraphBar>();

	public IReadOnlyList<CpuCoreClockReading> CpuCoreClocks { get; init; } = Array.Empty<CpuCoreClockReading>();

	public int CpuPhysicalCoreCount { get; init; }

	public int CpuLogicalThreadCount { get; init; }

	public int CpuPerformanceCoreCount { get; init; }

	public int CpuEfficiencyCoreCount { get; init; }

	public string GpuName { get; init; } = "GPU";

	public double? GpuLoadPercent { get; init; }

	public double? GpuTemperatureC { get; init; }

	public double? GpuHotspotTemperatureC { get; init; }

	public double? GpuVramTemperatureC { get; init; }

	public double? GpuClockMhz { get; init; }

	public double? GpuMemoryClockMhz { get; init; }

	public double? GpuVoltageV { get; init; }

	public double? GpuPowerW { get; init; }

	public double? GpuFanRpm { get; init; }

	public double? GpuFanPercent { get; init; }

	public double? VramUsedGb { get; init; }

	public double? VramTotalGb { get; init; }

	public double? VramLoadPercent { get; init; }

	public double RamUsedGb { get; init; }

	public double RamTotalGb { get; init; }

	public double RamLoadPercent { get; init; }

	public string RamSpeedText { get; init; } = "N/A";

	public IReadOnlyList<RamModuleTemperature> RamModuleTemperatures { get; init; } = Array.Empty<RamModuleTemperature>();
}
