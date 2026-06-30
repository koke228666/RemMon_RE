using System.Collections.Generic;

namespace RemMon;

public sealed class RamSettings
{
	public bool ShowBlock { get; set; } = true;

	public bool ShowUsed { get; set; } = true;

	public bool ShowLoad { get; set; } = true;

	public bool ShowSpeed { get; set; } = true;

	public bool ShowTemperatures { get; set; }

	public string MemoryFormat { get; set; } = "UsedTotal";

	public string Unit { get; set; } = "ГБ";

	public string TitleColor { get; set; } = "#FFB58CFF";

	public string LabelColor { get; set; } = "#FFB58CFF";

	public string ValueColor { get; set; } = "#FFF2F2F2";

	public List<string> RowOrder { get; set; } = new List<string> { "Used", "Load", "Speed" };
}
