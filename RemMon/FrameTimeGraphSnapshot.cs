using System.Collections.Generic;

namespace RemMon;

internal sealed record FrameTimeGraphSnapshot(double NowSeconds, IReadOnlyList<FrameTimeGraphSample> Samples);
