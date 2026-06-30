namespace RemMon;

public static class StatisticsDisplayModes
{
	public const string MinMax = "MinMax";

	public const string Average = "Average";

	public const string MinAverageMax = "MinAverageMax";

	public static string Normalize(string? value)
	{
		if (!(value == "Average"))
		{
			if (value == "MinAverageMax")
			{
				return "MinAverageMax";
			}
			return "MinMax";
		}
		return "Average";
	}
}
