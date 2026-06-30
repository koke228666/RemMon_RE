namespace RemMon;

internal sealed record InstalledFontInfo(string Id, string DisplayName, string FamilyName, string FilePath, bool IsBuiltIn)
{
	public static InstalledFontInfo BuiltIn { get; } = new InstalledFontInfo("system:Segoe UI", "Segoe UI", "Segoe UI", string.Empty, IsBuiltIn: true);
}
