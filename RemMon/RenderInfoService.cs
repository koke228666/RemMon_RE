using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RemMon;

internal sealed class RenderInfoService
{
	private static class PeImportReader
	{
		private readonly record struct PeSection(uint VirtualAddress, uint Size, uint RawPointer);

		public static IReadOnlyCollection<string> ReadImportedDllNames(string filePath)
		{
			using FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using BinaryReader binaryReader = new BinaryReader(fileStream, Encoding.ASCII, leaveOpen: false);
			if (fileStream.Length < 64)
			{
				throw new BadImageFormatException("PE image is too small.");
			}
			fileStream.Position = 60L;
			uint num = binaryReader.ReadUInt32();
			if (num == 0 || num > fileStream.Length - 24)
			{
				throw new BadImageFormatException("Invalid PE header offset.");
			}
			fileStream.Position = num;
			if (binaryReader.ReadUInt32() != 17744)
			{
				throw new BadImageFormatException("Missing PE signature.");
			}
			binaryReader.ReadUInt16();
			ushort count = binaryReader.ReadUInt16();
			fileStream.Position += 12L;
			ushort num2 = binaryReader.ReadUInt16();
			fileStream.Position += 2L;
			long position = fileStream.Position;
			long num3 = position + binaryReader.ReadUInt16() switch
			{
				267 => 96, 
				523 => 112, 
				_ => throw new BadImageFormatException("Unsupported PE optional header."), 
			} + 8;
			if (num3 > fileStream.Length - 8)
			{
				throw new BadImageFormatException("Missing PE import directory.");
			}
			fileStream.Position = num3;
			uint num4 = binaryReader.ReadUInt32();
			binaryReader.ReadUInt32();
			long offset = position + num2;
			List<PeSection> sections = ReadSections(binaryReader, fileStream, offset, count);
			long num5 = RvaToFileOffset(num4, sections);
			if (num4 == 0 || num5 < 0)
			{
				return Array.Empty<string>();
			}
			List<string> list = new List<string>();
			fileStream.Position = num5;
			while (fileStream.Position <= fileStream.Length - 20)
			{
				binaryReader.ReadUInt32();
				binaryReader.ReadUInt32();
				binaryReader.ReadUInt32();
				uint num6 = binaryReader.ReadUInt32();
				binaryReader.ReadUInt32();
				if (num6 == 0)
				{
					break;
				}
				long num7 = RvaToFileOffset(num6, sections);
				if (num7 >= 0)
				{
					list.Add(ReadNullTerminatedAscii(binaryReader, fileStream, num7));
				}
			}
			return list;
		}

		private static List<PeSection> ReadSections(BinaryReader reader, Stream stream, long offset, ushort count)
		{
			List<PeSection> list = new List<PeSection>(count);
			stream.Position = offset;
			for (int i = 0; i < count; i++)
			{
				if (stream.Position > stream.Length - 40)
				{
					break;
				}
				stream.Position += 8L;
				uint val = reader.ReadUInt32();
				uint virtualAddress = reader.ReadUInt32();
				uint val2 = reader.ReadUInt32();
				uint rawPointer = reader.ReadUInt32();
				stream.Position += 16L;
				list.Add(new PeSection(virtualAddress, Math.Max(val, val2), rawPointer));
			}
			return list;
		}

		private static long RvaToFileOffset(uint rva, IReadOnlyList<PeSection> sections)
		{
			foreach (PeSection section in sections)
			{
				if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.Size)
				{
					return section.RawPointer + rva - section.VirtualAddress;
				}
			}
			return -1L;
		}

		private static string ReadNullTerminatedAscii(BinaryReader reader, Stream stream, long offset)
		{
			stream.Position = offset;
			List<byte> list = new List<byte>();
			while (stream.Position < stream.Length)
			{
				byte b = reader.ReadByte();
				if (b == 0)
				{
					break;
				}
				list.Add(b);
			}
			return Encoding.ASCII.GetString(list.ToArray());
		}
	}

	private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2L);

	private static readonly string[] ImportProbeFileNames = new string[3] { "UnityPlayer.dll", "GameAssembly.dll", "UnrealEditor-Core.dll" };

	private ActiveProcessInfo? _cachedProcess;

	private RenderInfo _cachedInfo = RenderInfo.Empty;

	private DateTime _cachedUtc = DateTime.MinValue;

	private string? _lastDiagnosticSignature;

	private readonly HashSet<uint> _moduleProbeTimedOutPids = new HashSet<uint>();

	public RenderInfo GetRenderInfo(ActiveProcessInfo? processInfo, RenderApiHint? etwHint = null)
	{
		if (!processInfo.HasValue)
		{
			return RenderInfo.Empty;
		}
		DateTime utcNow = DateTime.UtcNow;
		if (_cachedProcess?.ProcessId == processInfo.Value.ProcessId && utcNow - _cachedUtc < CacheDuration)
		{
			return _cachedInfo;
		}
		_cachedProcess = processInfo;
		_cachedUtc = utcNow;
		_cachedInfo = ReadRenderInfo(processInfo.Value, etwHint);
		return _cachedInfo;
	}

	public RenderInfo GetFastRenderInfo(ActiveProcessInfo processInfo, RenderApiHint? etwHint)
	{
		NativeMethods.Rect largestClientRect = GetLargestClientRect(processInfo.ProcessId);
		return new RenderInfo
		{
			ApiText = etwHint?.Api,
			Width = ((largestClientRect.Width > 0) ? new int?(largestClientRect.Width) : ((int?)null)),
			Height = ((largestClientRect.Height > 0) ? new int?(largestClientRect.Height) : ((int?)null))
		};
	}

	private RenderInfo ReadRenderInfo(ActiveProcessInfo processInfo, RenderApiHint? etwHint)
	{
		string moduleApi = null;
		string importApi = null;
		string text = null;
		string moduleStatus = "not-read";
		string status = "not-read";
		string error = null;
		bool? flag = null;
		NativeMethods.Rect largestClientRect = GetLargestClientRect(processInfo.ProcessId);
		text = TryQueryProcessImagePath(processInfo.ProcessId);
		try
		{
			using Process process = Process.GetProcessById((int)processInfo.ProcessId);
			flag = Is64BitProcess(process);
			if (text == null)
			{
				text = TryGetMainModulePath(process);
			}
		}
		catch (Exception ex)
		{
			moduleStatus = "failed:" + ex.GetType().Name;
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			importApi = TryDetectRenderApiFromImports(text, out status);
		}
		HashSet<string> modules2;
		if (TryReadProcessModulesByToolHelp(processInfo.ProcessId, out HashSet<string> modules, out string error2))
		{
			moduleApi = DetectRenderApi(modules);
			moduleStatus = $"toolhelp:{modules.Count}";
		}
		else if (!_moduleProbeTimedOutPids.Contains(processInfo.ProcessId) && TryReadProcessModulesWithTimeout(processInfo.ProcessId, out modules2, out error))
		{
			moduleApi = DetectRenderApi(modules2);
			moduleStatus = $"process-modules:{modules2.Count}; toolhelp-failed:{error2}";
		}
		else if (error != null)
		{
			moduleStatus = "failed:" + error + "; toolhelp-failed:" + error2;
			if (error.Equals("timeout", StringComparison.OrdinalIgnoreCase))
			{
				_moduleProbeTimedOutPids.Add(processInfo.ProcessId);
			}
		}
		string etwApi = etwHint?.Api;
		string source;
		string text2 = ResolveRenderApi(moduleApi, importApi, etwApi, out source);
		LogDiagnostic(processInfo, text2, source, moduleApi, importApi, etwApi, moduleStatus, status, text, largestClientRect);
		return new RenderInfo
		{
			ApiText = ((text2 == null) ? null : ((!flag.HasValue) ? text2 : (text2 + " " + (flag.Value ? "x64" : "x86")))),
			Width = ((largestClientRect.Width > 0) ? new int?(largestClientRect.Width) : ((int?)null)),
			Height = ((largestClientRect.Height > 0) ? new int?(largestClientRect.Height) : ((int?)null))
		};
	}

	private static bool TryReadProcessModulesWithTimeout(uint processId, out HashSet<string> modules, out string? error)
	{
		try
		{
			Task<HashSet<string>> task = Task.Run(delegate
			{
				using Process process = Process.GetProcessById((int)processId);
				return ReadProcessModules(process);
			});
			if (!task.Wait(TimeSpan.FromMilliseconds(150L)))
			{
				modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				error = "timeout";
				return false;
			}
			modules = task.Result;
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			error = ex.GetType().Name;
			return false;
		}
	}

	private static HashSet<string> ReadProcessModules(Process process)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ProcessModule item in ((IEnumerable)process.Modules).Cast<ProcessModule>())
		{
			hashSet.Add(item.ModuleName);
		}
		return hashSet;
	}

	private static bool TryReadProcessModulesByToolHelp(uint processId, out HashSet<string> modules, out string? error)
	{
		modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		error = null;
		nint num = NativeMethods.CreateToolhelp32Snapshot(24u, processId);
		if (num == IntPtr.Zero || num == NativeMethods.InvalidHandleValue)
		{
			error = $"snapshot:{Marshal.GetLastWin32Error()}";
			return false;
		}
		try
		{
			NativeMethods.ModuleEntry32 moduleEntry = new NativeMethods.ModuleEntry32
			{
				dwSize = (uint)Marshal.SizeOf<NativeMethods.ModuleEntry32>()
			};
			if (!NativeMethods.Module32First(num, ref moduleEntry))
			{
				error = $"first:{Marshal.GetLastWin32Error()}";
				return false;
			}
			do
			{
				if (!string.IsNullOrWhiteSpace(moduleEntry.szModule))
				{
					modules.Add(moduleEntry.szModule);
				}
				moduleEntry.dwSize = (uint)Marshal.SizeOf<NativeMethods.ModuleEntry32>();
			}
			while (NativeMethods.Module32Next(num, ref moduleEntry));
			return modules.Count > 0;
		}
		finally
		{
			NativeMethods.CloseHandle(num);
		}
	}

	private static string? ResolveRenderApi(string? moduleApi, string? importApi, string? etwApi, out string source)
	{
		if (!string.IsNullOrWhiteSpace(etwApi) && ApiEquals(etwApi, moduleApi))
		{
			source = "etw+modules";
			return ChooseMoreSpecificApi(etwApi, moduleApi);
		}
		if (!string.IsNullOrWhiteSpace(etwApi) && ApiEquals(etwApi, importApi))
		{
			source = "etw+imports";
			return ChooseMoreSpecificApi(etwApi, importApi);
		}
		if (!string.IsNullOrWhiteSpace(moduleApi) && ApiEquals(moduleApi, importApi))
		{
			source = "modules+imports";
			return ChooseMoreSpecificApi(moduleApi, importApi);
		}
		if (!string.IsNullOrWhiteSpace(etwApi))
		{
			source = "etw";
			return etwApi;
		}
		if (!string.IsNullOrWhiteSpace(moduleApi))
		{
			source = "modules";
			return moduleApi;
		}
		if (!string.IsNullOrWhiteSpace(importApi))
		{
			source = "imports";
			return importApi;
		}
		source = "none";
		return null;
	}

	private static bool ApiEquals(string? left, string? right)
	{
		if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
		{
			if (!left.Equals(right, StringComparison.OrdinalIgnoreCase) && (!left.Equals("DirectX", StringComparison.OrdinalIgnoreCase) || !right.StartsWith("DirectX", StringComparison.OrdinalIgnoreCase)))
			{
				if (right.Equals("DirectX", StringComparison.OrdinalIgnoreCase))
				{
					return left.StartsWith("DirectX", StringComparison.OrdinalIgnoreCase);
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private static string ChooseMoreSpecificApi(string left, string? right)
	{
		if (string.IsNullOrWhiteSpace(right))
		{
			return left;
		}
		if (GetApiSpecificity(right) <= GetApiSpecificity(left))
		{
			return left;
		}
		return right;
	}

	private static int GetApiSpecificity(string api)
	{
		if (api.Contains("DirectX 12", StringComparison.OrdinalIgnoreCase) || api.Contains("DirectX 11", StringComparison.OrdinalIgnoreCase) || api.Contains("DirectX 10", StringComparison.OrdinalIgnoreCase) || api.Contains("DirectX 9", StringComparison.OrdinalIgnoreCase) || api.Contains("Vulkan", StringComparison.OrdinalIgnoreCase) || api.Contains("OpenGL", StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		return api.Contains("DirectX", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
	}

	private static string? DetectRenderApi(IReadOnlySet<string> dllNames)
	{
		if (dllNames.Contains("d3d12.dll"))
		{
			return "DirectX 12";
		}
		if (dllNames.Contains("vulkan-1.dll"))
		{
			return "Vulkan";
		}
		if (dllNames.Contains("d3d11.dll"))
		{
			return "DirectX 11";
		}
		if (dllNames.Contains("d3d10.dll") || dllNames.Contains("d3d10_1.dll"))
		{
			return "DirectX 10";
		}
		if (dllNames.Contains("d3d9.dll"))
		{
			return "DirectX 9";
		}
		if (dllNames.Contains("dxgi.dll"))
		{
			return "DirectX";
		}
		if (dllNames.Contains("opengl32.dll"))
		{
			return "OpenGL";
		}
		return null;
	}

	private static string? TryDetectRenderApiFromImports(string imagePath, out string status)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> list = new List<string>();
		foreach (string importProbeFile in GetImportProbeFiles(imagePath))
		{
			try
			{
				list.Add(Path.GetFileName(importProbeFile));
				foreach (string item in PeImportReader.ReadImportedDllNames(importProbeFile))
				{
					hashSet.Add(item);
				}
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is BadImageFormatException || ex is ArgumentException) ? 1 : 0) != 0)
			{
				list.Add(Path.GetFileName(importProbeFile) + ":" + ex.GetType().Name);
			}
		}
		status = ((hashSet.Count == 0) ? ("none:" + string.Join(",", list)) : $"ok:{hashSet.Count}:{string.Join(",", list)}");
		return DetectRenderApi(hashSet);
	}

	private static IEnumerable<string> GetImportProbeFiles(string imagePath)
	{
		if (File.Exists(imagePath))
		{
			yield return imagePath;
		}
		string directory = Path.GetDirectoryName(imagePath);
		if (string.IsNullOrWhiteSpace(directory))
		{
			yield break;
		}
		string[] importProbeFileNames = ImportProbeFileNames;
		foreach (string path in importProbeFileNames)
		{
			string text = Path.Combine(directory, path);
			if (File.Exists(text))
			{
				yield return text;
			}
		}
	}

	private static string? TryGetMainModulePath(Process process)
	{
		try
		{
			return process.MainModule?.FileName;
		}
		catch
		{
			return null;
		}
	}

	private static string? TryQueryProcessImagePath(uint processId)
	{
		nint num = NativeMethods.OpenProcess(4096u, inheritHandle: false, processId);
		if (num == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			int size = 32767;
			StringBuilder stringBuilder = new StringBuilder(size);
			return NativeMethods.QueryFullProcessImageName(num, 0, stringBuilder, ref size) ? stringBuilder.ToString() : null;
		}
		finally
		{
			NativeMethods.CloseHandle(num);
		}
	}

	private static bool Is64BitProcess(Process process)
	{
		if (!Environment.Is64BitOperatingSystem)
		{
			return false;
		}
		if (NativeMethods.IsWow64Process(process.Handle, out var wow64Process))
		{
			return !wow64Process;
		}
		return false;
	}

	private void LogDiagnostic(ActiveProcessInfo processInfo, string? api, string source, string? moduleApi, string? importApi, string? etwApi, string moduleStatus, string importStatus, string? imagePath, NativeMethods.Rect clientRect)
	{
		InlineArray10<object> buffer = default(InlineArray10<object>);
		buffer[0] = processInfo.ProcessId;
		buffer[1] = api ?? "none";
		buffer[2] = source;
		buffer[3] = moduleApi ?? "none";
		buffer[4] = importApi ?? "none";
		buffer[5] = etwApi ?? "none";
		buffer[6] = moduleStatus;
		buffer[7] = importStatus;
		buffer[8] = clientRect.Width;
		buffer[9] = clientRect.Height;
		string text = string.Join("|", (ReadOnlySpan<object?>)buffer);
		if (!text.Equals(_lastDiagnosticSignature, StringComparison.Ordinal))
		{
			_lastDiagnosticSignature = text;
			AppLogger.Info($"Render API detection: Process={processInfo.ProcessName}.exe PID={processInfo.ProcessId}; Api={api ?? "N/A"}; Source={source}; Modules={moduleApi ?? "N/A"} ({moduleStatus}); Imports={importApi ?? "N/A"} ({importStatus}); ETW={etwApi ?? "N/A"}; Resolution={((clientRect.Width > 0 && clientRect.Height > 0) ? $"{clientRect.Width}x{clientRect.Height}" : "N/A")}; Image={imagePath ?? "N/A"}");
		}
	}

	private static NativeMethods.Rect GetLargestClientRect(uint processId)
	{
		NativeMethods.Rect best = default(NativeMethods.Rect);
		int bestArea = 0;
		NativeMethods.EnumWindows(delegate(nint hwnd, nint _)
		{
			if (!NativeMethods.IsWindowVisible(hwnd))
			{
				return true;
			}
			NativeMethods.GetWindowThreadProcessId(hwnd, out var processId2);
			if (processId2 != processId)
			{
				return true;
			}
			if (!NativeMethods.GetClientRect(hwnd, out var lpRect))
			{
				return true;
			}
			int num = lpRect.Width * lpRect.Height;
			if (lpRect.Width > 0 && lpRect.Height > 0 && num > bestArea)
			{
				best = lpRect;
				bestArea = num;
			}
			return true;
		}, IntPtr.Zero);
		return best;
	}
}
