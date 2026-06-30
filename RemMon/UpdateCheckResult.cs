namespace RemMon;

internal sealed class UpdateCheckResult
{
	public UpdateCheckStatus Status { get; }

	public string Message { get; }

	public UpdateManifest? Manifest { get; }

	public string CurrentVersion { get; }

	public string ServerVersion { get; }

	private UpdateCheckResult(UpdateCheckStatus status, string message, UpdateManifest? manifest, string currentVersion, string serverVersion)
	{
		Status = status;
		Message = message;
		Manifest = manifest;
		CurrentVersion = currentVersion;
		ServerVersion = serverVersion;
	}

	public static UpdateCheckResult Unavailable(string message)
	{
		return new UpdateCheckResult(UpdateCheckStatus.Unavailable, message, null, string.Empty, string.Empty);
	}

	public static UpdateCheckResult UpToDate(string currentVersion, string serverVersion)
	{
		return new UpdateCheckResult(UpdateCheckStatus.UpToDate, "Установлена актуальная версия.", null, currentVersion, serverVersion);
	}

	public static UpdateCheckResult Available(UpdateManifest manifest, string currentVersion, string serverVersion)
	{
		return new UpdateCheckResult(UpdateCheckStatus.Available, "Доступно обновление.", manifest, currentVersion, serverVersion);
	}
}
