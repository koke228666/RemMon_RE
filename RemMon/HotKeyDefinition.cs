using System.Collections.Generic;

namespace RemMon;

public sealed class HotKeyDefinition
{
	public bool Enabled { get; set; } = true;

	public bool Control { get; set; }

	public bool Alt { get; set; }

	public bool Shift { get; set; }

	public bool Win { get; set; }

	public string Key { get; set; } = "F8";

	public string DisplayText
	{
		get
		{
			List<string> list = new List<string>();
			if (Control)
			{
				list.Add("Ctrl");
			}
			if (Alt)
			{
				list.Add("Alt");
			}
			if (Shift)
			{
				list.Add("Shift");
			}
			if (Win)
			{
				list.Add("Win");
			}
			list.Add(Key);
			return string.Join(" + ", list);
		}
	}
}
