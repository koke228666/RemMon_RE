namespace RemMon;

internal sealed class LicenseState
{
	public bool IsValid { get; }

	public LicenseFile? License { get; }

	public string Message { get; }

	public bool IsPremium => IsValid;

	public bool IsFree => !IsValid;

	private LicenseState(bool isValid, LicenseFile? license, string message)
	{
		IsValid = isValid;
		License = license;
		Message = message;
	}

	public static LicenseState Valid(LicenseFile license, string message = "Лицензия активна.")
	{
		return new LicenseState(isValid: true, license, message);
	}

	public static LicenseState Invalid(string message)
	{
		return new LicenseState(isValid: false, null, message);
	}

	public static LicenseState Free(string message = "Бесплатная версия программы.")
	{
		return new LicenseState(isValid: false, null, message);
	}
}
