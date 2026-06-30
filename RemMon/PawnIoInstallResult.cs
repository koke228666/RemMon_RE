namespace RemMon;

internal sealed record PawnIoInstallResult(bool NeedsManualInstall, string Message)
{
	public static PawnIoInstallResult Ok { get; } = new PawnIoInstallResult(NeedsManualInstall: false, string.Empty);

	public static PawnIoInstallResult ManualInstallRequired(string message)
	{
		return new PawnIoInstallResult(NeedsManualInstall: true, message);
	}
}
