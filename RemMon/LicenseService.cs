using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RemMon;

internal sealed class LicenseService
{
	private sealed record ActivationRequest(string LicenseKey, string HardwareId, string MachineName, string AppVersion);

	private sealed record CheckRequest(string ActivationId, string HardwareId, string LicenseToken);

	private sealed class LicenseServerResponse
	{
		public string Status { get; set; } = string.Empty;

		public string ActivationId { get; set; } = string.Empty;

		public string LicenseToken { get; set; } = string.Empty;

		public DateTimeOffset? ExpiresAt { get; set; }

		public int MaxDevices { get; set; }
	}

	private sealed class LicenseTokenEnvelope
	{
		public string Payload { get; set; } = string.Empty;

		public string Signature { get; set; } = string.Empty;

		public string Alg { get; set; } = string.Empty;
	}

	private sealed class LicenseTokenPayload
	{
		public string ActivationId { get; set; } = string.Empty;

		public string LicenseKeyHash { get; set; } = string.Empty;

		public string HardwareId { get; set; } = string.Empty;

		public DateTimeOffset? ExpiresAt { get; set; }

		public DateTimeOffset IssuedAt { get; set; }
	}

	/*private const string ActivateUrl = "https://r-mont.ru/api/license/activate";
	private const string CheckUrl = "https://r-mont.ru/api/license/check";
	private const string DeactivateUrl = "https://r-mont.ru/api/license/deactivate";*/
	//private const string PublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEApAT1UnHwdSiY3VYb63OE\nvcZKK4k+DSdHL0VImWxhEIy1RvjqbvKz4xQo/xFtYtim9v/l0AeSnh97U0mV0PPi\nta9YzQJEvuW0EyBcFrSAUVdMsNcbmpD3BVF6CgNxJz3Q6Bc1DrgkyebrSCNH/NDl\nVtd54+JhPgKfNJXnJD62NFbF1xgGymZY3xMhrPeKLjjK0ZwjkqdDq4KiQmeTxbH6\nD9MY7fnL84ProqhE45jZ+YYbWXF3iLUeVQm4hL9Xjy+Wwa8nPvUeeUaqR7Wz05NL\nvzARIh/KJxWAarqqNiauMYfbvn1pwTzedh1xAU8WfweKWpvz6BeWo2siIrvtMH9B\nl4cT/x0YBtETEndcmVvEZVQYira/LsB08//ncZaLxL08Cy+hvUbM1TAn42d2xt8Z\nhBymIK310EMKzOhTAuAGvmUurt/8TKnGg2V2nlGsfVZzuipgraFkWcWo4aSSb3sC\n7HhKRmw4uswo27lnYBrkBd4ODS3UqyJXuwnds0Yo6p00zsOdq98V2O25jQ/oJVCE\nHqm4r1ctCbxoBnj75RKP1XDlMRAl4tHUk4dooGtu282o0rtnxdGDYAQH0BhwxDTF\nIg461l54SId86Rasz/HCxHu5YcA3ezuUcobRl05chQy9FMmGJ6RtG9kqgt8Zlpkl\nVkyNd4Sf1hcVSEYGaDXg1QMCAwEAAQ==\n-----END PUBLIC KEY-----";
    private const string ActivateUrl = "http://localhost/api/license/activate";
	private const string CheckUrl = "http://localhost/api/license/check";
	private const string DeactivateUrl = "http://localhost/api/license/deactivate";
    private const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA31kKmX1k3/K+a8y47lmj
AkQxSdmpD7jtwJHADaVDEFb8bn1dZ09wDEOj1b5xLX23TjZAmK1CQj94/nWV939m
3bscTJekApD3QG8kNcdWmY1adzDUokVtD9DQGxzqejUEQ1SYhm10r8+e/hNp2007
ROkavWPOcylt2+Id3sqJtR3+n3773GFlMioV/Wr65StyofrFUI5C1nzXmMtyJ2Q1
bteKzuYpwsTQ/2UVb4hifm9Mts3bE5RYS6wq1gEqpTifDsutx+Yw4nr6N0QVSJtu
DFCaYO08tRr2t7Z4su4yyMYVsZ+KU8bLOXI+QujdpGAGz+/j/RIEFLiK5QzXJK0S
0QIDAQAB
-----END PUBLIC KEY-----"; //да ваше крутяк
    
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private static readonly HttpClient HttpClient = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(15L)
	};

	private readonly string _licensePath;

	private readonly string _hardwareId;

	public string LicensePath => _licensePath;

	public string HardwareId => _hardwareId;

	public LicenseService()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		_licensePath = Path.Combine(folderPath, "RemMon", "license.dat");
		_hardwareId = GenerateHardwareId();
		AppLogger.Info("license service initialized");
		AppLogger.Info("license file path: " + _licensePath);
	}

	public LicenseState CheckLocalLicense()
	{
		if (!File.Exists(_licensePath))
		{
			AppLogger.Info("license file missing; free version");
			return LicenseState.Free();
		}
		try
		{
			string json = File.ReadAllText(_licensePath, Encoding.UTF8);
			if (IsLegacyLicenseFile(json))
			{
				AppLogger.Info("legacy license.dat format detected");
				return LicenseState.Invalid("Нужно активировать ключ заново.");
			}
			LicenseFile licenseFile = JsonSerializer.Deserialize<LicenseFile>(json, JsonOptions);
			if (licenseFile == null || string.IsNullOrWhiteSpace(licenseFile.ActivationId) || string.IsNullOrWhiteSpace(licenseFile.LicenseToken))
			{
				return LicenseState.Invalid("Файл лицензии повреждён.");
			}
			AppLogger.Info("license file loaded");
			if (!licenseFile.HardwareId.Equals(_hardwareId, StringComparison.OrdinalIgnoreCase))
			{
				AppLogger.Info("hardwareId mismatch");
				return LicenseState.Invalid("Лицензия привязана к другому устройству.");
			}
			LicenseTokenPayload licenseTokenPayload = VerifyLicenseToken(licenseFile.LicenseToken, licenseFile.ActivationId, licenseFile.HardwareId);
			licenseFile.ExpiresAt = licenseTokenPayload.ExpiresAt;
			if (IsExpired(licenseFile.ExpiresAt))
			{
				return LicenseState.Invalid("Срок действия ключа истёк.");
			}
			return LicenseState.Valid(licenseFile);
		}
		catch (Exception ex) when (((ex is JsonException || ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException || ex is FormatException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			AppLogger.Info("license local check failed: " + ex.Message);
			return LicenseState.Invalid((ex is CryptographicException) ? "Ошибка проверки подписи лицензии." : "Файл лицензии повреждён.");
		}
	}

	public async Task<LicenseOperationResult> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default(CancellationToken))
	{
		licenseKey = NormalizeKey(licenseKey);
		if (string.IsNullOrWhiteSpace(licenseKey))
		{
			return LicenseOperationResult.Failed("invalid", "Ключ не найден.");
		}
		AppLogger.Info("activation started");
		try
		{
			ActivationRequest request = new ActivationRequest(licenseKey, _hardwareId, Environment.MachineName, GetAppVersion());
			using HttpResponseMessage response = await PostJsonAsync(ActivateUrl, request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				AppLogger.Info($"activation failed. HTTP {(int)response.StatusCode}");
				return LicenseOperationResult.Failed("error", "Сервер активации недоступен. Попробуйте позже.");
			}
			LicenseOperationResult result;
			await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
			{
				LicenseServerResponse licenseServerResponse = await JsonSerializer.DeserializeAsync<LicenseServerResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				string text = NormalizeStatus(licenseServerResponse?.Status);
				AppLogger.Info("activation server status: " + text);
				if (text != "ok")
				{
					result = LicenseOperationResult.Failed(text, MessageForStatus(text), ShouldBlockForStatus(text));
				}
				else if (licenseServerResponse == null || string.IsNullOrWhiteSpace(licenseServerResponse.ActivationId) || string.IsNullOrWhiteSpace(licenseServerResponse.LicenseToken))
				{
					result = LicenseOperationResult.Failed("error", "Некорректный ответ сервера.");
				}
				else
				{
					AppLogger.Info("activationId received");
					LicenseTokenPayload licenseTokenPayload = VerifyLicenseToken(licenseServerResponse.LicenseToken, licenseServerResponse.ActivationId, _hardwareId);
					LicenseFile license = new LicenseFile
					{
						ActivationId = licenseServerResponse.ActivationId,
						HardwareId = _hardwareId,
						LicenseToken = licenseServerResponse.LicenseToken,
						ActivatedAt = DateTimeOffset.UtcNow,
						LastOnlineCheckAt = DateTimeOffset.UtcNow,
						ExpiresAt = licenseTokenPayload.ExpiresAt
					};
					SaveLicense(license);
					AppLogger.Info("license.dat saved without license key");
					AppLogger.Info("activation success");
					result = LicenseOperationResult.Ok("Лицензия активирована.", license);
				}
			}
			return result;
		}
		catch (CryptographicException ex)
		{
			AppLogger.Info("activation failed. Signature error: " + ex.Message);
			return LicenseOperationResult.Failed("signature", "Ошибка проверки подписи лицензии.");
		}
		catch (Exception ex2) when (((ex2 is HttpRequestException || ex2 is TaskCanceledException || ex2 is JsonException || ex2 is IOException || ex2 is InvalidOperationException) ? 1 : 0) != 0)
		{
			AppLogger.Info("activation failed: " + ex2.Message);
			return LicenseOperationResult.Failed("error", "Сервер активации недоступен. Попробуйте позже.");
		}
	}

	public bool IsOfflineGraceValid(LicenseFile license)
	{
		if (license.LastOnlineCheckAt.HasValue)
		{
			return DateTimeOffset.UtcNow - license.LastOnlineCheckAt.Value <= TimeSpan.FromDays(7);
		}
		return false;
	}

	public async Task<LicenseOperationResult> CheckOnlineAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		LicenseState local = CheckLocalLicense();
		if (!local.IsValid || local.License == null)
		{
			return LicenseOperationResult.Failed("invalid", local.Message, shouldBlockApplication: true);
		}
		AppLogger.Info("online check by activationId started");
		try
		{
			CheckRequest request = new CheckRequest(local.License.ActivationId, _hardwareId, local.License.LicenseToken);
			using HttpResponseMessage response = await PostJsonAsync(CheckUrl, request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				AppLogger.Info($"online license check failed. HTTP {(int)response.StatusCode}");
				if (response.StatusCode >= HttpStatusCode.InternalServerError)
				{
					return LicenseOperationResult.Failed("offline", "Сервер активации недоступен. Попробуйте позже.", shouldBlockApplication: true);
				}
				return LicenseOperationResult.Failed("error", "Некорректный ответ сервера.", shouldBlockApplication: true);
			}
			LicenseOperationResult result;
			await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
			{
				LicenseServerResponse licenseServerResponse = await JsonSerializer.DeserializeAsync<LicenseServerResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				string text = NormalizeStatus(licenseServerResponse?.Status);
				AppLogger.Info("online license check status: " + text);
				if (text != "ok")
				{
					AppLogger.Info("online license check failed");
					result = LicenseOperationResult.Failed(text, MessageForStatus(text), ShouldBlockForStatus(text));
				}
				else
				{
					local.License.LastOnlineCheckAt = DateTimeOffset.UtcNow;
					if (licenseServerResponse != null)
					{
						local.License.ExpiresAt = licenseServerResponse.ExpiresAt;
					}
					SaveLicense(local.License);
					AppLogger.Info("online license check success");
					result = LicenseOperationResult.Ok("Лицензия активна.", local.License);
				}
			}
			return result;
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is IOException) ? 1 : 0) != 0)
		{
			AppLogger.Info("online license check failed: " + ex.Message);
			return LicenseOperationResult.Failed("offline", "Сервер активации недоступен. Попробуйте позже.", shouldBlockApplication: true);
		}
		catch (Exception ex2) when (((ex2 is JsonException || ex2 is InvalidOperationException) ? 1 : 0) != 0)
		{
			AppLogger.Info("online license check response error: " + ex2.Message);
			return LicenseOperationResult.Failed("error", "Некорректный ответ сервера.", shouldBlockApplication: true);
		}
	}

	public async Task<LicenseDeactivationResult> DeactivateAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		AppLogger.Info("license deactivation started");
		LicenseState licenseState = CheckLocalLicense();
		if (!licenseState.IsValid || licenseState.License == null)
		{
			AppLogger.Info("license deactivation failed: local license missing");
			return LicenseDeactivationResult.Failed("not_activated", "Активация отсутствует.");
		}
		try
		{
			DeactivateRequest request = new DeactivateRequest(licenseState.License.ActivationId, licenseState.License.HardwareId, licenseState.License.LicenseToken);
			using HttpResponseMessage response = await PostJsonAsync(DeactivateUrl, request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			AppLogger.Info("license deactivation request sent");
			if (!response.IsSuccessStatusCode)
			{
				AppLogger.Info($"license deactivation failed. HTTP {(int)response.StatusCode}");
				if (response.StatusCode >= HttpStatusCode.InternalServerError)
				{
					AppLogger.Info("license deactivation server unavailable");
					return LicenseDeactivationResult.ServerUnavailable();
				}
				return LicenseDeactivationResult.Failed("error", "Некорректный ответ сервера.");
			}
			LicenseDeactivationResult result;
			await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
			{
				DeactivateResponse deactivateResponse = await JsonSerializer.DeserializeAsync<DeactivateResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				string text = NormalizeStatus(deactivateResponse?.Status);
				AppLogger.Info("license deactivation server status: " + text);
				if (text == "ok")
				{
					AppLogger.Info("license deactivation success");
					result = LicenseDeactivationResult.Success(string.IsNullOrWhiteSpace(deactivateResponse?.Message) ? "Активация удалена." : deactivateResponse.Message);
				}
				else if (text == "not_activated")
				{
					AppLogger.Info("license deactivation already not activated");
					result = LicenseDeactivationResult.AlreadyNotActivated();
				}
				else
				{
					string message = DeactivationMessageForStatus(text);
					AppLogger.Info("license deactivation failed: " + text);
					result = LicenseDeactivationResult.Failed(text, message);
				}
			}
			return result;
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is IOException) ? 1 : 0) != 0)
		{
			AppLogger.Info("license deactivation server unavailable: " + ex.Message);
			return LicenseDeactivationResult.ServerUnavailable();
		}
		catch (Exception ex2) when (((ex2 is JsonException || ex2 is InvalidOperationException) ? 1 : 0) != 0)
		{
			AppLogger.Info("license deactivation failed: " + ex2.Message);
			return LicenseDeactivationResult.Failed("error", "Некорректный ответ сервера.");
		}
	}

	public void RemoveLicense(bool keyChanged = false)
	{
		try
		{
			if (File.Exists(_licensePath))
			{
				File.Delete(_licensePath);
			}
			AppLogger.Info(keyChanged ? "license changed" : "license removed");
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			AppLogger.Info("license remove failed: " + ex.Message);
		}
	}

	private void SaveLicense(LicenseFile license)
	{
		string directoryName = Path.GetDirectoryName(_licensePath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string contents = JsonSerializer.Serialize(license, JsonOptions);
		File.WriteAllText(_licensePath, contents, Encoding.UTF8);
	}

	private LicenseTokenPayload VerifyLicenseToken(string licenseToken, string expectedActivationId, string expectedHardwareId)
	{
		LicenseTokenEnvelope licenseTokenEnvelope = JsonSerializer.Deserialize<LicenseTokenEnvelope>(DecodeBase64(licenseToken), JsonOptions);
		if (licenseTokenEnvelope == null || licenseTokenEnvelope.Alg != "RS256" || string.IsNullOrWhiteSpace(licenseTokenEnvelope.Payload) || string.IsNullOrWhiteSpace(licenseTokenEnvelope.Signature))
		{
			throw new CryptographicException("Invalid license token envelope.");
		}
		byte[] array = DecodeBase64(licenseTokenEnvelope.Payload);
		byte[] signature = DecodeBase64(licenseTokenEnvelope.Signature);
		using RSA rSA = RSA.Create();
		rSA.ImportFromPem(PublicKeyPem.AsSpan());
		if (!rSA.VerifyData(array, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
		{
			AppLogger.Info("license token signature invalid");
			throw new CryptographicException("License token signature is invalid.");
		}
		AppLogger.Info("license token signature valid");
		LicenseTokenPayload licenseTokenPayload;
		try
		{
			licenseTokenPayload = JsonSerializer.Deserialize<LicenseTokenPayload>(array, JsonOptions);
		}
		catch (JsonException ex) when (Encoding.UTF8.GetString(array).Contains("\"expiresAt\":null", StringComparison.OrdinalIgnoreCase))
		{
			AppLogger.Info("activation failed while parsing expiresAt=null: " + ex.Message);
			throw;
		}
		if (licenseTokenPayload == null)
		{
			throw new CryptographicException("Invalid license token payload.");
		}
		if (!licenseTokenPayload.ActivationId.Equals(expectedActivationId, StringComparison.OrdinalIgnoreCase))
		{
			AppLogger.Info("token activationId mismatch");
			throw new CryptographicException("Activation id mismatch.");
		}
		if (!licenseTokenPayload.HardwareId.Equals(expectedHardwareId, StringComparison.OrdinalIgnoreCase))
		{
			AppLogger.Info("token hardwareId mismatch");
			throw new CryptographicException("Hardware id mismatch.");
		}
		if (!licenseTokenPayload.ExpiresAt.HasValue)
		{
			AppLogger.Info("perpetual license detected");
		}
		else if (IsExpired(licenseTokenPayload.ExpiresAt))
		{
			throw new CryptographicException("License token expired.");
		}
		return licenseTokenPayload;
	}

	private static async Task<HttpResponseMessage> PostJsonAsync<T>(string url, T request, CancellationToken cancellationToken)
	{
		string content = JsonSerializer.Serialize(request, JsonOptions);
		using StringContent content2 = new StringContent(content, Encoding.UTF8, "application/json");
		return await HttpClient.PostAsync(url, content2, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static string GenerateHardwareId()
	{
		string text = string.Empty;
		try
		{
			text = WindowsIdentity.GetCurrent()?.User?.Value ?? string.Empty;
		}
		catch (Exception ex) when (((ex is UnauthorizedAccessException || ex is SecurityException) ? 1 : 0) != 0)
		{
			AppLogger.Info("Windows identity read failed, using fallback: " + ex.Message);
		}
		InlineArray4<string> buffer = default(InlineArray4<string>);
		buffer[0] = Environment.MachineName;
		buffer[1] = text;
		buffer[2] = Environment.UserName;
		buffer[3] = Environment.OSVersion.VersionString;
		string s = string.Join("|", (ReadOnlySpan<string?>)buffer);
		string result = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
		AppLogger.Info("hardwareId generated");
		return result;
	}

	private static byte[] DecodeBase64(string value)
	{
		string text = value.Trim().Replace('-', '+').Replace('_', '/');
		int num = text.Length % 4;
		if (num > 0)
		{
			text = text.PadRight(text.Length + 4 - num, '=');
		}
		return Convert.FromBase64String(text);
	}

	private static string NormalizeKey(string key)
	{
		return key.Trim().ToUpperInvariant();
	}

	private static string NormalizeStatus(string? status)
	{
		if (!string.IsNullOrWhiteSpace(status))
		{
			return status.Trim().ToLowerInvariant();
		}
		return "error";
	}

	private static bool IsLegacyLicenseFile(string json)
	{
		return json.Contains("\"licenseKey\"", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsExpired(DateTimeOffset? expiresAt)
	{
		if (expiresAt.HasValue)
		{
			return expiresAt <= DateTimeOffset.UtcNow;
		}
		return false;
	}

	private static string MessageForStatus(string status)
	{
		return status switch
		{
			"invalid" => "Ключ не найден.", 
			"blocked" => "Ключ заблокирован.", 
			"expired" => "Срок действия ключа истёк.", 
			"device_limit" => "Достигнут лимит устройств.", 
			"not_activated" => "Ключ не найден.", 
			"token_invalid" => "Ошибка проверки подписи лицензии.", 
			"hardware_mismatch" => "Лицензия привязана к другому устройству.", 
			"error" => "Некорректный ответ сервера.", 
			_ => "Некорректный ответ сервера.", 
		};
	}

	private static string DeactivationMessageForStatus(string status)
	{
		return status switch
		{
			"token_invalid" => "Ошибка проверки подписи лицензии.", 
			"hardware_mismatch" => "Лицензия привязана к другому устройству.", 
			"expired" => "Срок действия ключа истёк.", 
			"error" => "Сервер не смог удалить активацию.", 
			_ => "Не удалось удалить активацию.", 
		};
	}

	private static bool ShouldBlockForStatus(string status)
	{
		if (status != null)
		{
			int length = status.Length;
			if (length <= 12)
			{
				if (length != 7)
				{
					if (length == 12 && status == "device_limit")
					{
						goto IL_00c6;
					}
				}
				else
				{
					char c = status[0];
					if (c != 'b')
					{
						if (c != 'e')
						{
							if (c == 'i' && status == "invalid")
							{
								goto IL_00c6;
							}
						}
						else if (status == "expired")
						{
							goto IL_00c6;
						}
					}
					else if (status == "blocked")
					{
						goto IL_00c6;
					}
				}
			}
			else if (length != 13)
			{
				if (length == 17 && status == "hardware_mismatch")
				{
					goto IL_00c6;
				}
			}
			else
			{
				char c = status[0];
				if (c != 'n')
				{
					if (c == 't' && status == "token_invalid")
					{
						goto IL_00c6;
					}
				}
				else if (status == "not_activated")
				{
					goto IL_00c6;
				}
			}
		}
		return false;
		IL_00c6:
		return true;
	}

	private static string GetAppVersion()
	{
		return Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
	}
}
