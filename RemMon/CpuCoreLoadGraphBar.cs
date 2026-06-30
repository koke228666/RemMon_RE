namespace RemMon;

internal sealed record CpuCoreLoadGraphBar(double LoadPercent, CpuCoreType CoreType, int? CoreNumber = null, int? ThreadNumber = null, string SensorName = "");
