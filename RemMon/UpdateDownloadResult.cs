namespace RemMon;

internal sealed class UpdateDownloadResult
{
	public bool Success { get; }

	public string Message { get; }

	public string? FilePath { get; }

	private UpdateDownloadResult(bool success, string message, string? filePath)
	{
		Success = success;
		Message = message;
		FilePath = filePath;
	}

	public static UpdateDownloadResult Completed(string filePath)
	{
		return new UpdateDownloadResult(success: true, "Обновление скачано.", filePath);
	}

	public static UpdateDownloadResult Failed(string message)
	{
		return new UpdateDownloadResult(success: false, message, null);
	}
}
