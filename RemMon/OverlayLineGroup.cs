using System.Collections.Generic;

namespace RemMon;

internal sealed record OverlayLineGroup(string Title, string TitleColor, IReadOnlyList<OverlayLineItem> Items);
