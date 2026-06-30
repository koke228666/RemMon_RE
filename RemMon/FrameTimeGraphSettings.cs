namespace RemMon;

public sealed class FrameTimeGraphSettings
{
	public bool ShowGraph { get; set; } = true;

	public int Height { get; set; } = 80;

	public string Color { get; set; } = "#FFFF0000";

	public string BackgroundColor { get; set; } = "#FF000000";

	public int FillOpacity { get; set; } = 50;

	public double MaxMs { get; set; } = 100.0;

	public string Style { get; set; } = "Bars";

	public bool ShowMsLabel { get; set; } = true;

	public bool Smoothing { get; set; } = true;
}
