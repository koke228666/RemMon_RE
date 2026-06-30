using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RemMon;

internal static class NativeMethods
{
	public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	public struct Rect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;

		public int Width => Right - Left;

		public int Height => Bottom - Top;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	public struct MonitorInfo
	{
		public int cbSize;

		public Rect rcMonitor;

		public Rect rcWork;

		public uint dwFlags;
	}

	public struct MemoryStatusEx
	{
		public uint dwLength;

		public uint dwMemoryLoad;

		public ulong ullTotalPhys;

		public ulong ullAvailPhys;

		public ulong ullTotalPageFile;

		public ulong ullAvailPageFile;

		public ulong ullTotalVirtual;

		public ulong ullAvailVirtual;

		public ulong ullAvailExtendedVirtual;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct ModuleEntry32
	{
		public uint dwSize;

		public uint th32ModuleID;

		public uint th32ProcessID;

		public uint GlblcntUsage;

		public uint ProccntUsage;

		public nint modBaseAddr;

		public uint modBaseSize;

		public nint hModule;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string szModule;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string szExePath;
	}

	public const int GwlExStyle = -20;

	public const int WsExTransparent = 32;

	public const int WsExLayered = 524288;

	public const int WsExToolWindow = 128;

	public const int WmHotKey = 786;

	public const uint ModNone = 0u;

	public const uint ModAlt = 1u;

	public const uint ModControl = 2u;

	public const uint ModShift = 4u;

	public const uint ModWin = 8u;

	public const uint SwpNoSize = 1u;

	public const uint SwpNoMove = 2u;

	public const uint SwpNoActivate = 16u;

	public const uint SwpShowWindow = 64u;

	public const uint MonitorDefaultToNull = 0u;

	public const uint MonitorDefaultToNearest = 2u;

	public const int DwmwaExtendedFrameBounds = 9;

	public const uint ProcessQueryLimitedInformation = 4096u;

	public const uint Th32csSnapModule = 8u;

	public const uint Th32csSnapModule32 = 16u;

	public static readonly nint InvalidHandleValue = new IntPtr(-1);

	public static readonly nint HwndTopmost = new IntPtr(-1);

	[DllImport("user32.dll")]
	public static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	public static extern nint GetDesktopWindow();

	[DllImport("user32.dll")]
	public static extern nint GetShellWindow();

	[DllImport("user32.dll")]
	public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

	[DllImport("user32.dll")]
	public static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll")]
	public static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	public static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll")]
	public static extern bool GetClientRect(nint hWnd, out Rect lpRect);

	[DllImport("user32.dll")]
	public static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

	[DllImport("user32.dll")]
	public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

	[DllImport("user32.dll")]
	public static extern nint MonitorFromRect(ref Rect lprc, uint dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	public static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

	[DllImport("user32.dll")]
	public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int GetWindowLong(nint hWnd, int index);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int SetWindowLong(nint hWnd, int index, int newStyle);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool UnregisterHotKey(nint hWnd, int id);

	[DllImport("dwmapi.dll")]
	public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out Rect pvAttribute, int cbAttribute);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool IsWow64Process(nint hProcess, out bool wow64Process);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool GetSystemCpuSetInformation(nint information, uint bufferLength, out uint returnedLength, nint process, uint flags);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder executablePath, ref int size);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool CloseHandle(nint handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern bool Module32First(nint snapshot, ref ModuleEntry32 moduleEntry);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern bool Module32Next(nint snapshot, ref ModuleEntry32 moduleEntry);
}
