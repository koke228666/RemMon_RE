using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RemMon;

internal sealed class UpdateService
{
	//private const string DefaultUpdateManifestUrl = "https://r-mont.ru/RemMon/update.json";
	private const string DefaultUpdateManifestUrl = "http://localhost/RemMon/update.json";

	private static readonly HttpClient HttpClient = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(15L)
	};

	public string CurrentVersionText { get; } = GetCurrentVersionText();

	public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		AppLogger.Info("Update check started. ManifestUrl="+DefaultUpdateManifestUrl+", CurrentVersion=" + CurrentVersionText);
		try
		{
			using HttpResponseMessage response = await HttpClient.GetAsync(DefaultUpdateManifestUrl, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				AppLogger.Info($"Update check failed. HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
				return UpdateCheckResult.Unavailable("Сервер обновлений недоступен. Попробуйте позже.");
			}
			UpdateCheckResult result;
			await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
			{
				UpdateManifest updateManifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				if (updateManifest == null || string.IsNullOrWhiteSpace(updateManifest.Version))
				{
					AppLogger.Info("Update check failed. Manifest is empty or version is missing.");
					result = UpdateCheckResult.Unavailable("Сервер обновлений недоступен. Попробуйте позже.");
				}
				else
				{
					Version version = ParseVersion(CurrentVersionText);
					Version version2 = ParseVersion(updateManifest.Version);
					bool flag = version2 > version;
					AppLogger.Info($"Update check completed. CurrentVersion={version}, ServerVersion={version2}, UpdateAvailable={flag}");
					result = (flag ? UpdateCheckResult.Available(updateManifest, version.ToString(), version2.ToString()) : UpdateCheckResult.UpToDate(version.ToString(), version2.ToString()));
				}
			}
			return result;
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is IOException || ex is InvalidOperationException || ex is FormatException) ? 1 : 0) != 0)
		{
			AppLogger.Info("Update check error: " + ex.Message);
			return UpdateCheckResult.Unavailable("Сервер обновлений недоступен. Попробуйте позже.");
		}
	}

	public async Task<UpdateDownloadResult> DownloadAsync(UpdateManifest manifest, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(manifest.Url) || !Uri.TryCreate(manifest.Url, UriKind.Absolute, out Uri uri))
		{
			AppLogger.Info("Update download canceled. Update URL is empty or invalid.");
			return UpdateDownloadResult.Failed("Ссылка на обновление недоступна.");
		}
		AppLogger.Info("Update download started. Url=" + manifest.Url);
		try
		{
			bool usedFallback;
			string targetPath = GetDownloadPath(uri, manifest.Version, out usedFallback);
			string directoryName = Path.GetDirectoryName(targetPath);
			Directory.CreateDirectory(directoryName);
			AppLogger.Info($"Update download target folder: {directoryName}. Fallback={usedFallback}");
			using HttpResponseMessage response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				AppLogger.Info($"Update download failed. HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
				return UpdateDownloadResult.Failed("Не удалось скачать обновление. Попробуйте позже.");
			}
			long? totalBytes = response.Content.Headers.ContentLength;
			if (!totalBytes.HasValue || totalBytes.GetValueOrDefault() <= 0)
			{
				progress?.Report(new UpdateDownloadProgress(null, "Загрузка обновления..."));
			}
			UpdateDownloadResult result;
			await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
			{
				string savedPath;
				UpdateDownloadResult updateDownloadResult;
				await using (FileStream destination = CreateDownloadFile(targetPath, out savedPath))
				{
					byte[] buffer = new byte[81920];
					long downloadedBytes = 0L;
					int lastLoggedProgress = -10;
					while (true)
					{
						int num;
						int read = (num = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(continueOnCapturedContext: false));
						if (num <= 0)
						{
							break;
						}
						await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
						downloadedBytes += read;
						if (totalBytes.HasValue && totalBytes.GetValueOrDefault() > 0)
						{
							double num2 = Math.Clamp((double)downloadedBytes * 100.0 / (double)totalBytes.Value, 0.0, 100.0);
							progress?.Report(new UpdateDownloadProgress(num2, $"Загружено {num2:0}%"));
							int num3 = (int)(num2 / 10.0) * 10;
							if (num3 >= lastLoggedProgress + 10)
							{
								lastLoggedProgress = num3;
								AppLogger.Info($"Update download progress: {num2:0}%");
							}
						}
					}
					progress?.Report(new UpdateDownloadProgress(100.0, "Обновление скачано"));
					AppLogger.Info("Update download completed. Path=" + savedPath);
					updateDownloadResult = UpdateDownloadResult.Completed(savedPath);
				}
				result = updateDownloadResult;
			}
			return result;
		}
		catch (UnauthorizedAccessException ex)
		{
			AppLogger.Info("Update download write access error: " + ex.Message);
			return await DownloadToTempAsync(uri, manifest.Version, progress, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex2) when (((ex2 is HttpRequestException || ex2 is TaskCanceledException || ex2 is IOException || ex2 is InvalidOperationException) ? 1 : 0) != 0)
		{
			AppLogger.Info("Update download error: " + ex2.Message);
			return UpdateDownloadResult.Failed("Не удалось скачать обновление. Попробуйте позже.");
		}
	}

	public static void OpenFileInExplorer(string path)
	{
		if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				Arguments = "/select,\"" + path + "\"",
				UseShellExecute = true
			});
		}
	}

	public static void OpenFolderForFile(string path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			string directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(directoryName) && Directory.Exists(directoryName))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = "\"" + directoryName + "\"",
					UseShellExecute = true
				});
			}
		}
	}

	public static void OpenArchive(string path)
	{
		if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = path,
				UseShellExecute = true
			});
		}
	}

	private async Task<UpdateDownloadResult> DownloadToTempAsync(Uri uri, string version, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken)
	{
		_ = 5;
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "RemMon", "New version " + version);
			Directory.CreateDirectory(text);
			string targetPath = Path.Combine(text, GetSafeFileName(uri, version));
			AppLogger.Info("Update download fallback path selected. Path=" + targetPath);
			using HttpResponseMessage response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				return UpdateDownloadResult.Failed("Не удалось скачать обновление. Попробуйте позже.");
			}
			long? totalBytes = response.Content.Headers.ContentLength;
			if (!totalBytes.HasValue || totalBytes.GetValueOrDefault() <= 0)
			{
				progress?.Report(new UpdateDownloadProgress(null, "Загрузка обновления..."));
			}
			UpdateDownloadResult result;
			await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
			{
				string savedPath;
				UpdateDownloadResult updateDownloadResult;
				await using (FileStream destination = CreateDownloadFile(targetPath, out savedPath))
				{
					byte[] buffer = new byte[81920];
					long downloadedBytes = 0L;
					int lastLoggedProgress = -10;
					while (true)
					{
						int num;
						int read = (num = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(continueOnCapturedContext: false));
						if (num <= 0)
						{
							break;
						}
						await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
						downloadedBytes += read;
						if (totalBytes.HasValue && totalBytes.GetValueOrDefault() > 0)
						{
							double num2 = Math.Clamp((double)downloadedBytes * 100.0 / (double)totalBytes.Value, 0.0, 100.0);
							progress?.Report(new UpdateDownloadProgress(num2, $"Загружено {num2:0}%"));
							int num3 = (int)(num2 / 10.0) * 10;
							if (num3 >= lastLoggedProgress + 10)
							{
								lastLoggedProgress = num3;
								AppLogger.Info($"Update download progress: {num2:0}%");
							}
						}
					}
					progress?.Report(new UpdateDownloadProgress(100.0, "Обновление скачано"));
					AppLogger.Info("Update download completed. Path=" + savedPath);
					updateDownloadResult = UpdateDownloadResult.Completed(savedPath);
				}
				result = updateDownloadResult;
			}
			return result;
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			AppLogger.Info("Update download fallback error: " + ex.Message);
			return UpdateDownloadResult.Failed("Не удалось скачать обновление. Попробуйте позже.");
		}
	}

	private static FileStream CreateDownloadFile(string targetPath, out string savedPath)
	{
		savedPath = GetUniquePath(targetPath);
		return new FileStream(savedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
	}

	private static string GetDownloadPath(Uri uri, string version, out bool usedFallback)
	{
		string text = Path.Combine(AppContext.BaseDirectory, "New version " + version);
		usedFallback = false;
		try
		{
			Directory.CreateDirectory(text);
			string path = Path.Combine(text, ".write-test");
			File.WriteAllText(path, string.Empty);
			File.Delete(path);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			usedFallback = true;
			text = Path.Combine(Path.GetTempPath(), "RemMon", "New version " + version);
		}
		return Path.Combine(text, GetSafeFileName(uri, version));
	}

	private static string GetSafeFileName(Uri uri, string version)
	{
		string text = Path.GetFileName(uri.LocalPath);
		if (string.IsNullOrWhiteSpace(text) || !text.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
		{
			text = "RemMon_" + version + ".zip";
		}
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			text = text.Replace(oldChar, '_');
		}
		return text;
	}

	private static string GetUniquePath(string path)
	{
		if (!File.Exists(path))
		{
			return path;
		}
		string path2 = Path.GetDirectoryName(path) ?? string.Empty;
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		string extension = Path.GetExtension(path);
		for (int i = 1; i < 1000; i++)
		{
			string text = Path.Combine(path2, $"{fileNameWithoutExtension} ({i}){extension}");
			if (!File.Exists(text))
			{
				return text;
			}
		}
		return Path.Combine(path2, $"{fileNameWithoutExtension}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
	}

	private static string GetCurrentVersionText()
	{
		Assembly executingAssembly = Assembly.GetExecutingAssembly();
		string text = executingAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text.Split('+')[0];
		}
		return executingAssembly.GetName().Version?.ToString() ?? "0.0.0";
	}

	private static Version ParseVersion(string version)
	{
		string text = version.Trim();
		int num = text.IndexOfAny(new char[2] { '-', '+' });
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		if (!Version.TryParse(text, out Version result))
		{
			throw new FormatException("Invalid version: " + version);
		}
		return new Version(Math.Max(result.Major, 0), Math.Max(result.Minor, 0), Math.Max(result.Build, 0), Math.Max(result.Revision, 0));
	}
}
