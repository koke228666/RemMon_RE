using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;

namespace RemMon;

internal static class FontLibraryService
{
	private sealed class FontManifest
	{
		public List<FontManifestEntry> Fonts { get; set; } = new List<FontManifestEntry>();
	}

	private sealed class FontManifestEntry
	{
		public string Id { get; set; } = string.Empty;

		public string DisplayName { get; set; } = string.Empty;

		public string FamilyName { get; set; } = string.Empty;

		public string FileName { get; set; } = string.Empty;

		public string Source { get; set; } = string.Empty;

		public DateTime InstalledUtc { get; set; }
	}

	public const string BuiltInFontId = "system:Segoe UI";

	private const string BuiltInFontName = "Segoe UI";

	private const long MaxFontBytes = 31457280L;

	private static readonly string FontFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RemMon", "fonts");

	private static readonly string ManifestPath = Path.Combine(FontFolder, "fonts.json");

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf", ".ttc" };

	public static IReadOnlyList<InstalledFontInfo> GetInstalledFonts()
	{
		EnsureFontFolder();
		FontManifest fontManifest = ReadManifest();
		if (AddLooseFontFilesToManifest(fontManifest) | RepairFontMetadata(fontManifest))
		{
			SaveManifest(fontManifest);
		}
		List<InstalledFontInfo> list = new List<InstalledFontInfo>();
		list.Add(InstalledFontInfo.BuiltIn);
		list.AddRange(fontManifest.Fonts.Where((FontManifestEntry entry) => File.Exists(GetFontPath(entry.FileName))).OrderBy<FontManifestEntry, string>((FontManifestEntry entry) => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase).Select(ToInstalledFontInfo));
		return list;
	}

	public static string NormalizeFontId(string? fontId)
	{
		if (string.IsNullOrWhiteSpace(fontId) || fontId.Equals("system:Segoe UI", StringComparison.OrdinalIgnoreCase))
		{
			return "system:Segoe UI";
		}
		if (!GetInstalledFonts().Any((InstalledFontInfo font) => font.Id.Equals(fontId, StringComparison.OrdinalIgnoreCase)))
		{
			return "system:Segoe UI";
		}
		return fontId;
	}

	public static FontRenderInfo ResolveFontInfo(string? fontId)
	{
		InstalledFontInfo installedFontInfo = GetInstalledFonts().FirstOrDefault((InstalledFontInfo item) => item.Id.Equals(fontId, StringComparison.OrdinalIgnoreCase));
		if (installedFontInfo == null || installedFontInfo.IsBuiltIn || string.IsNullOrWhiteSpace(installedFontInfo.FilePath) || !File.Exists(installedFontInfo.FilePath))
		{
			return new FontRenderInfo(new FontFamily("Segoe UI"), 1.0);
		}
		string text = Path.GetDirectoryName(installedFontInfo.FilePath) ?? FontFolder;
		if (!text.EndsWith(Path.DirectorySeparatorChar))
		{
			text += Path.DirectorySeparatorChar;
		}
		return new FontRenderInfo(new FontFamily(new Uri(text, UriKind.Absolute), "./#" + installedFontInfo.FamilyName), GetVisualScale(installedFontInfo.FilePath));
	}

	public static FontFamily ResolveFontFamily(string? fontId)
	{
		return ResolveFontInfo(fontId).Family;
	}

	public static InstalledFontInfo InstallFromFile(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
		{
			throw new FileNotFoundException("Файл шрифта не найден.", filePath);
		}
		string extension = ValidateFontExtension(Path.GetExtension(filePath));
		return InstallFontBytes(File.ReadAllBytes(filePath), extension, filePath);
	}

	public static async Task<InstalledFontInfo> InstallFromUrlAsync(string url)
	{
		Uri uri;
		bool flag = !Uri.TryCreate(url, UriKind.Absolute, out uri);
		if (!flag)
		{
			string scheme = uri.Scheme;
			bool flag2 = ((scheme == "http" || scheme == "https") ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			throw new InvalidOperationException("Укажите прямую ссылку http/https на файл .ttf, .otf или .ttc.");
		}
		EnsureFontFolder();
		FontManifestEntry fontManifestEntry = ReadManifest().Fonts.FirstOrDefault((FontManifestEntry entry) => entry.Source.Equals(url, StringComparison.OrdinalIgnoreCase) && File.Exists(GetFontPath(entry.FileName)));
		if (fontManifestEntry != null)
		{
			return ToInstalledFontInfo(fontManifestEntry);
		}
		using HttpClient client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(30L)
		};
		using HttpResponseMessage response = await client.GetAsync(uri);
		response.EnsureSuccessStatusCode();
		byte[] obj = await response.Content.ReadAsByteArrayAsync();
		if (obj.LongLength > 31457280)
		{
			throw new InvalidOperationException("Файл шрифта слишком большой. Максимальный размер: 30 МБ.");
		}
		string extension = ValidateFontExtension(GetExtensionFromUrl(uri));
		return InstallFontBytes(obj, extension, url);
	}

	private static InstalledFontInfo InstallFontBytes(byte[] bytes, string extension, string source)
	{
		if (bytes.Length == 0)
		{
			throw new InvalidOperationException("Файл шрифта пустой.");
		}
		EnsureFontFolder();
		string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		string fileName = hash + extension;
		string fontPath = GetFontPath(fileName);
		if (!File.Exists(fontPath))
		{
			File.WriteAllBytes(fontPath, bytes);
		}
		string text = ReadFontFamilyName(fontPath);
		FontManifest fontManifest = ReadManifest();
		fontManifest.Fonts.RemoveAll((FontManifestEntry entry) => entry.Id.Equals(hash, StringComparison.OrdinalIgnoreCase) || entry.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
		FontManifestEntry fontManifestEntry = new FontManifestEntry
		{
			Id = hash,
			DisplayName = text,
			FamilyName = text,
			FileName = fileName,
			Source = source,
			InstalledUtc = DateTime.UtcNow
		};
		fontManifest.Fonts.Add(fontManifestEntry);
		SaveManifest(fontManifest);
		return ToInstalledFontInfo(fontManifestEntry);
	}

	private static bool AddLooseFontFilesToManifest(FontManifest manifest)
	{
		bool result = false;
		HashSet<string> hashSet = manifest.Fonts.Select((FontManifestEntry entry) => entry.FileName).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in Directory.EnumerateFiles(FontFolder))
		{
			string extension = Path.GetExtension(item);
			if (AllowedExtensions.Contains(extension))
			{
				string fileName = Path.GetFileName(item);
				if (!hashSet.Contains(fileName))
				{
					string id = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item))).ToLowerInvariant();
					string text = ReadFontFamilyName(item);
					manifest.Fonts.Add(new FontManifestEntry
					{
						Id = id,
						DisplayName = text,
						FamilyName = text,
						FileName = fileName,
						Source = item,
						InstalledUtc = File.GetCreationTimeUtc(item)
					});
					result = true;
				}
			}
		}
		return result;
	}

	private static bool RepairFontMetadata(FontManifest manifest)
	{
		bool result = false;
		foreach (FontManifestEntry font in manifest.Fonts)
		{
			string fontPath = GetFontPath(font.FileName);
			if (File.Exists(fontPath))
			{
				string text = ReadFontFamilyName(fontPath);
				if (!string.IsNullOrWhiteSpace(text) && (IsHashLikeName(font.DisplayName) || IsHashLikeName(font.FamilyName) || !font.DisplayName.Equals(text, StringComparison.Ordinal) || !font.FamilyName.Equals(text, StringComparison.Ordinal)))
				{
					font.DisplayName = text;
					font.FamilyName = text;
					result = true;
				}
			}
		}
		return result;
	}

	private static bool IsHashLikeName(string value)
	{
		int length = value.Length;
		if (length >= 32 && length <= 128)
		{
			return value.All((char ch) => char.IsDigit(ch) || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F'));
		}
		return false;
	}

	private static string ReadFontFamilyName(string path)
	{
		try
		{
			string text = new GlyphTypeface(new Uri(path, UriKind.Absolute)).FamilyNames.Values.FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text.Trim();
			}
		}
		catch
		{
		}
		return Path.GetFileNameWithoutExtension(path);
	}

	private static double GetVisualScale(string path)
	{
		try
		{
			double capsHeight = new GlyphTypeface(new Uri(path, UriKind.Absolute)).CapsHeight;
			if (capsHeight > 0.0 && capsHeight < 1.5)
			{
				return Math.Clamp(0.7 / capsHeight, 0.85, 1.85);
			}
		}
		catch
		{
		}
		return 1.0;
	}

	private static string GetExtensionFromUrl(Uri uri)
	{
		string extension = Path.GetExtension(uri.AbsolutePath);
		if (!string.IsNullOrWhiteSpace(extension))
		{
			return extension;
		}
		return ".ttf";
	}

	private static string ValidateFontExtension(string extension)
	{
		if (!AllowedExtensions.Contains(extension))
		{
			throw new InvalidOperationException("Поддерживаются только файлы шрифтов .ttf, .otf и .ttc.");
		}
		return extension.ToLowerInvariant();
	}

	private static InstalledFontInfo ToInstalledFontInfo(FontManifestEntry entry)
	{
		return new InstalledFontInfo(entry.Id, entry.DisplayName, entry.FamilyName, GetFontPath(entry.FileName), IsBuiltIn: false);
	}

	private static FontManifest ReadManifest()
	{
		try
		{
			if (!File.Exists(ManifestPath))
			{
				return new FontManifest();
			}
			return JsonSerializer.Deserialize<FontManifest>(File.ReadAllText(ManifestPath), JsonOptions) ?? new FontManifest();
		}
		catch
		{
			return new FontManifest();
		}
	}

	private static void SaveManifest(FontManifest manifest)
	{
		EnsureFontFolder();
		File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
	}

	private static void EnsureFontFolder()
	{
		Directory.CreateDirectory(FontFolder);
	}

	private static string GetFontPath(string fileName)
	{
		return Path.Combine(FontFolder, fileName);
	}
}
