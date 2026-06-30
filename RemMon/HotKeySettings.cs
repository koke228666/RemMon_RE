namespace RemMon;

public sealed class HotKeySettings
{
	public HotKeyDefinition ToggleOverlay { get; set; } = new HotKeyDefinition
	{
		Key = "F8",
		Alt = true
	};

	public HotKeyDefinition ResetStatistics { get; set; } = new HotKeyDefinition
	{
		Key = "F9",
		Alt = true
	};

	public HotKeyDefinition ToggleOverlayMode { get; set; } = new HotKeyDefinition
	{
		Key = "F7",
		Alt = true
	};
}
