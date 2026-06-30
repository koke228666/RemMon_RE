using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RemMon;

public sealed class FpsSettings
{
	public bool ShowFps { get; set; } = true;

	public bool ShowAverage { get; set; } = true;

	public bool ShowOnePercentLow { get; set; } = true;

	public bool ShowPointOnePercentLow { get; set; } = true;

	public bool ShowFrameTime { get; set; } = true;

	public bool ShowGame { get; set; } = true;

	public bool ShowApi { get; set; }

	public bool HideUnavailableFpsMetrics { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool HideUnavailableMetrics { get; set; }

	public int TextSize { get; set; } = 12;

	public string TextColor { get; set; } = "#FFE7E7E7";

	public string ValueColor { get; set; } = "#FFFF6A00";

	public int UpdateIntervalMs { get; set; } = 250;

	public List<string> RowOrder { get; set; } = new List<string> { "FPS", "AVG", "1% Low", "0.1% Low", "FT", "Window", "API" };
}
