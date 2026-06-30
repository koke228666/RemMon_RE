namespace RemMon;

internal sealed class LicenseDeactivationResult
{
	public LicenseDeactivationState State { get; }

	public string Status { get; }

	public string Message { get; }

	public bool IsSuccessful
	{
		get
		{
			LicenseDeactivationState state = State;
			if ((uint)state <= 1u)
			{
				return true;
			}
			return false;
		}
	}

	public bool IsServerUnavailable => State == LicenseDeactivationState.ServerUnavailable;

	private LicenseDeactivationResult(LicenseDeactivationState state, string status, string message)
	{
		State = state;
		Status = status;
		Message = message;
	}

	public static LicenseDeactivationResult Success(string message = "Активация удалена.")
	{
		return new LicenseDeactivationResult(LicenseDeactivationState.Success, "ok", message);
	}

	public static LicenseDeactivationResult AlreadyNotActivated(string message = "Активация уже удалена на сервере.")
	{
		return new LicenseDeactivationResult(LicenseDeactivationState.AlreadyNotActivated, "not_activated", message);
	}

	public static LicenseDeactivationResult ServerUnavailable(string message = "Сервер активации недоступен.")
	{
		return new LicenseDeactivationResult(LicenseDeactivationState.ServerUnavailable, "offline", message);
	}

	public static LicenseDeactivationResult Failed(string status, string message)
	{
		return new LicenseDeactivationResult(LicenseDeactivationState.Failed, status, message);
	}
}
