using System;

namespace RemMon;

internal sealed class LicenseFile
{
	public string ActivationId { get; set; } = string.Empty;

	public string HardwareId { get; set; } = string.Empty;

	public string LicenseToken { get; set; } = string.Empty;

	public DateTimeOffset ActivatedAt { get; set; }

	public DateTimeOffset? LastOnlineCheckAt { get; set; }

	public DateTimeOffset? ExpiresAt { get; set; }
}
