namespace RemMon;

internal sealed class UpdateDownloadProgress
{
	public double? Percent { get; }

	public string Status { get; }

	public UpdateDownloadProgress(double? percent, string status)
	{
		Percent = percent;
		Status = status;
	}
}
