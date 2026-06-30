namespace RemMon;

internal sealed class FpsStats
{
	public static FpsStats Empty { get; } = new FpsStats();

	public bool HasValues { get; init; }

	public string? ProcessName { get; init; }

	public uint? ProcessId { get; init; }

	public string? Status { get; init; }

	public double? CurrentFps { get; init; }

	public double? AverageFps { get; init; }

	public double? OnePercentLowFps { get; init; }

	public double? PointOnePercentLowFps { get; init; }

	public double? FrameTimeMs { get; init; }

	public double? OnePercentLowFrameTimeMs
	{
		get
		{
			double? onePercentLowFps = OnePercentLowFps;
			if (!onePercentLowFps.HasValue || !(onePercentLowFps.GetValueOrDefault() > 0.0))
			{
				return null;
			}
			return 1000.0 / OnePercentLowFps.Value;
		}
	}

	public string? RenderInfoText { get; init; }

	public string CurrentFpsText => FormatFps(CurrentFps);

	public string AverageFpsText => FormatFps(AverageFps);

	public string OnePercentLowText => FormatFps(OnePercentLowFps);

	public string PointOnePercentLowText => FormatFps(PointOnePercentLowFps);

	public string FrameTimeText
	{
		get
		{
			double? frameTimeMs = FrameTimeMs;
			if (!frameTimeMs.HasValue || !(frameTimeMs.GetValueOrDefault() > 0.0))
			{
				return "N/A";
			}
			return $"{FrameTimeMs.Value:0.0} ms";
		}
	}

	public string RenderText
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(RenderInfoText))
			{
				return RenderInfoText;
			}
			return "API: N/A  N/A";
		}
	}

	public string GameText
	{
		get
		{
			string processName = ProcessName;
			string text = ((processName != null && processName.Length > 0) ? ("Окно: " + ProcessName + ".exe") : "Окно: N/A");
			if (!string.IsNullOrWhiteSpace(Status))
			{
				return text + " | " + Status;
			}
			return text;
		}
	}

	public string WindowText
	{
		get
		{
			string processName = ProcessName;
			string text = ((processName != null && processName.Length > 0) ? ("Окно: " + ProcessName + ".exe") : "Окно: N/A");
			if (!string.IsNullOrWhiteSpace(Status))
			{
				return text + " | " + Status;
			}
			return text;
		}
	}

	private static string FormatFps(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		return value.Value.ToString("0");
	}
}
