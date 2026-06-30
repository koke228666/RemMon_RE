using System;

namespace RemMon;

internal static class AppLaunchOptions
{
	public static string? SettingsDirectory { get; private set; }

	public static int AutoExitSeconds { get; private set; }

	public static void Parse(string[] args)
	{
		for (int i = 0; i < args.Length; i++)
		{
			string text = args[i];
			int result;
			if (text.Equals("--settings-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
			{
				SettingsDirectory = args[++i];
			}
			else if (text.Equals("--auto-exit-seconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out result))
			{
				AutoExitSeconds = Math.Clamp(result, 3, 300);
			}
		}
	}
}
