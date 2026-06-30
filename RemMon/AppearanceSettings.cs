namespace RemMon;

public sealed class AppearanceSettings
{
	public string Theme { get; set; } = "Dark";

	public int BackgroundOpacity { get; set; } = 82;

	public int CornerRadius { get; set; } = 10;

	public string BackgroundColor { get; set; } = "#FF080808";

	public string TextColor { get; set; } = "#FFF2F2F2";

	public bool ShowSeparators { get; set; } = true;

	public bool ShowBranding { get; set; } = true;

	public double TextScale { get; set; } = 1.0;

	public string FontId { get; set; } = "system:Segoe UI";
}
