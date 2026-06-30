namespace RemMon;

internal sealed record CpuCoreClockReading(int CoreIndex, double ClockMhz, CpuCoreType CoreType);
