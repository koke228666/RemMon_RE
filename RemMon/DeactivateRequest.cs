namespace RemMon;

internal sealed record DeactivateRequest(string ActivationId, string HardwareId, string LicenseToken);
