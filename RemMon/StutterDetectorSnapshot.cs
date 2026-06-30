namespace RemMon;

internal sealed record StutterDetectorSnapshot(StutterState State, int StutterCount, double? LastStutterFrameTimeMs, double? CurrentFrameTimeMs, double? PreviousFrameTimeMs, double? MedianFrameTimeMs, double? Ratio, double? DeltaFrameTimeMs);
