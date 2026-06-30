namespace RemMon;

internal sealed class LicenseOperationResult
{
	public bool Success { get; }

	public string Status { get; }

	public string Message { get; }

	public LicenseFile? License { get; }

	public bool ShouldBlockApplication { get; }

	private LicenseOperationResult(bool success, string status, string message, LicenseFile? license, bool shouldBlockApplication)
	{
		Success = success;
		Status = status;
		Message = message;
		License = license;
		ShouldBlockApplication = shouldBlockApplication;
	}

	public static LicenseOperationResult Ok(string message, LicenseFile? license)
	{
		return new LicenseOperationResult(success: true, "ok", message, license, shouldBlockApplication: false);
	}

	public static LicenseOperationResult Failed(string status, string message, bool shouldBlockApplication = false)
	{
		return new LicenseOperationResult(success: false, status, message, null, shouldBlockApplication);
	}
}
