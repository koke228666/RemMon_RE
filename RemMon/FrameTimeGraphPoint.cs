namespace RemMon;

public readonly record struct FrameTimeGraphPoint(double TimeSeconds, double? ValueMs, bool IsSynthetic, bool IsHold, bool IsInProgress);
