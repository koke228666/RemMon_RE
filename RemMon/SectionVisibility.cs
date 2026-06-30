namespace RemMon;

public sealed class SectionVisibility
{
	public bool Fps { get; set; } = true;

	public bool Gpu { get; set; } = true;

	public bool Cpu { get; set; } = true;

	public bool Ram { get; set; } = true;

	public bool Statistics { get; set; } = true;

	public bool FrameTimeGraph { get; set; } = true;
}
