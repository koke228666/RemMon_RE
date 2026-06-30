using System;
using System.Windows.Input;

namespace RemMon;

internal static class HotKeyMapper
{
	public static uint ToModifiers(HotKeyDefinition hotKey)
	{
		uint num = 0u;
		if (hotKey.Alt)
		{
			num |= 1;
		}
		if (hotKey.Control)
		{
			num |= 2;
		}
		if (hotKey.Shift)
		{
			num |= 4;
		}
		if (hotKey.Win)
		{
			num |= 8;
		}
		return num;
	}

	public static uint ToVirtualKey(HotKeyDefinition hotKey)
	{
		Key key = hotKey.Key switch
		{
			"Page Up" => Key.Prior, 
			"Page Down" => Key.Next, 
			"Del" => Key.Delete, 
			_ => Key.None, 
		};
		if (key != Key.None)
		{
			return (uint)KeyInterop.VirtualKeyFromKey(key);
		}
		if (Enum.TryParse<Key>(hotKey.Key, ignoreCase: true, out var result))
		{
			return (uint)KeyInterop.VirtualKeyFromKey(result);
		}
		if (hotKey.Key.Length == 1)
		{
			char c = char.ToUpperInvariant(hotKey.Key[0]);
			if (c >= 'A' && c <= 'Z')
			{
				return c;
			}
			if (c >= '0' && c <= '9')
			{
				return c;
			}
		}
		return 0u;
	}
}
