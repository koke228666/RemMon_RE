using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace RemMon;

internal sealed class HardwareMonitorService : IDisposable
{
	private sealed record SensorReading(string HardwareName, string HardwareType, string SensorName, string SensorType, string Identifier, double Value, string? Details);

	private sealed record SensorCandidate(SensorReading Reading, int Score)
	{
		public double Value => Reading.Value;
	}

	private sealed record CpuClockCandidate(SensorReading Reading, int Score, CpuCoreType CoreType, string? CoreNumber)
	{
		public double Value => Reading.Value;
	}

	private sealed record CpuRatioCandidate(string CoreNumber, double Ratio, CpuCoreType CoreType, int Score, SensorReading Reading);

	private sealed class CpuTopology
	{
		private readonly record struct RawCpuSetSlot(int LogicalIndex, int CoreIndex, byte EfficiencyClass);

		private readonly CpuLogicalProcessorSlot[] _logicalSlots;

		public int PhysicalCoreCount { get; }

		public int LogicalThreadCount { get; }

		public int PerformanceCoreCount { get; }

		public int EfficiencyCoreCount { get; }

		private CpuTopology(IReadOnlyList<CpuLogicalProcessorSlot> logicalSlots)
		{
			_logicalSlots = logicalSlots.OrderBy((CpuLogicalProcessorSlot slot) => slot.LogicalIndex).ToArray();
			LogicalThreadCount = _logicalSlots.Length;
			PhysicalCoreCount = _logicalSlots.Select((CpuLogicalProcessorSlot slot) => slot.CoreIndex).Distinct().Count();
			CpuCoreType[] source = (from slot in _logicalSlots
				group slot by slot.CoreIndex into @group
				select @group.Any((CpuLogicalProcessorSlot slot) => slot.CoreType == CpuCoreType.Efficiency) ? CpuCoreType.Efficiency : CpuCoreType.Performance).ToArray();
			PerformanceCoreCount = source.Count((CpuCoreType type) => type == CpuCoreType.Performance);
			EfficiencyCoreCount = source.Count((CpuCoreType type) => type == CpuCoreType.Efficiency);
		}

		public CpuCoreType GetCoreTypeByOneBasedCoreNumber(int coreNumber)
		{
			if (coreNumber <= 0 || _logicalSlots.Length == 0)
			{
				return CpuCoreType.Unknown;
			}
			int zeroBasedCoreNumber = coreNumber - 1;
			CpuLogicalProcessorSlot[] array = _logicalSlots.Where((CpuLogicalProcessorSlot slot) => slot.CoreIndex == zeroBasedCoreNumber).ToArray();
			if (array.Length == 0)
			{
				return CpuCoreType.Unknown;
			}
			if (!array.Any((CpuLogicalProcessorSlot slot) => slot.CoreType == CpuCoreType.Efficiency))
			{
				return CpuCoreType.Performance;
			}
			return CpuCoreType.Efficiency;
		}

		public static CpuTopology Detect(int fallbackPhysicalCoreCount)
		{
			CpuLogicalProcessorSlot[] array = TryReadCpuSetTopology();
			if (array != null && array.Length > 0)
			{
				int num = array.Select((CpuLogicalProcessorSlot slot) => slot.CoreIndex).Distinct().Count();
				bool flag = array.Select((CpuLogicalProcessorSlot slot) => slot.CoreType).Distinct().Count() > 1;
				if (flag || fallbackPhysicalCoreCount <= 0 || num <= fallbackPhysicalCoreCount)
				{
					return new CpuTopology(array);
				}
				AppLogger.Info($"CPU Sets topology ignored: Cores={num}, WmiCores={fallbackPhysicalCoreCount}, Hybrid={flag}");
			}
			return CreateFallbackTopology(fallbackPhysicalCoreCount);
		}

		private static CpuTopology CreateFallbackTopology(int fallbackPhysicalCoreCount)
		{
			int logicalCount = Math.Max(1, Environment.ProcessorCount);
			int physicalCount = ((fallbackPhysicalCoreCount > 0) ? Math.Min(fallbackPhysicalCoreCount, logicalCount) : logicalCount);
			return new CpuTopology((from index in Enumerable.Range(0, logicalCount)
				select new
				{
					LogicalIndex = index,
					CoreIndex = MapLogicalIndexToFallbackCore(index, logicalCount, physicalCount)
				} into slot
				group slot by slot.CoreIndex into @group
				orderby @group.Key
				select @group).SelectMany(group => group.OrderBy(slot => slot.LogicalIndex).Select((slot, threadIndex) => new CpuLogicalProcessorSlot(slot.LogicalIndex, slot.CoreIndex, threadIndex + 1, CpuCoreType.Unknown))).ToArray());
		}

		public IReadOnlyList<CpuCoreLoadGraphBar> BuildGraphBars(IReadOnlyList<double> loads, string sensorName = "")
		{
			if (_logicalSlots.Length == 0)
			{
				return Array.Empty<CpuCoreLoadGraphBar>();
			}
			CpuCoreLoadGraphBar[] array = new CpuCoreLoadGraphBar[_logicalSlots.Length];
			for (int i = 0; i < _logicalSlots.Length; i++)
			{
				CpuLogicalProcessorSlot cpuLogicalProcessorSlot = _logicalSlots[i];
				double value = ((cpuLogicalProcessorSlot.LogicalIndex >= 0 && cpuLogicalProcessorSlot.LogicalIndex < loads.Count) ? loads[cpuLogicalProcessorSlot.LogicalIndex] : 0.0);
				array[i] = new CpuCoreLoadGraphBar(Math.Clamp(value, 0.0, 100.0), cpuLogicalProcessorSlot.CoreType, cpuLogicalProcessorSlot.CoreIndex + 1, cpuLogicalProcessorSlot.ThreadNumber, string.IsNullOrWhiteSpace(sensorName) ? $"Logical Processor #{cpuLogicalProcessorSlot.LogicalIndex}" : $"{sensorName} #{cpuLogicalProcessorSlot.LogicalIndex}");
			}
			return array;
		}

		private static int MapLogicalIndexToFallbackCore(int logicalIndex, int logicalCount, int physicalCount)
		{
			if (physicalCount <= 0 || physicalCount >= logicalCount)
			{
				return logicalIndex;
			}
			return Math.Min(physicalCount - 1, (int)Math.Floor((double)(logicalIndex * physicalCount) / (double)logicalCount));
		}

		private static CpuLogicalProcessorSlot[]? TryReadCpuSetTopology()
		{
			try
			{
				NativeMethods.GetSystemCpuSetInformation(IntPtr.Zero, 0u, out var returnedLength, IntPtr.Zero, 0u);
				if (returnedLength == 0)
				{
					return null;
				}
				nint num = Marshal.AllocHGlobal((int)returnedLength);
				try
				{
					if (!NativeMethods.GetSystemCpuSetInformation(num, returnedLength, out var returnedLength2, IntPtr.Zero, 0u) || returnedLength2 == 0)
					{
						return null;
					}
					List<RawCpuSetSlot> list = new List<RawCpuSetSlot>();
					int num2;
					for (int i = 0; i + 24 <= returnedLength2; i += num2)
					{
						nint ptr = IntPtr.Add(num, i);
						num2 = Marshal.ReadInt32(ptr, 0);
						int num3 = Marshal.ReadInt32(ptr, 4);
						if (num2 <= 0 || i + num2 > returnedLength2)
						{
							break;
						}
						if (num3 == 0)
						{
							byte logicalIndex = Marshal.ReadByte(ptr, 13);
							byte coreIndex = Marshal.ReadByte(ptr, 14);
							byte efficiencyClass = Marshal.ReadByte(ptr, 17);
							list.Add(new RawCpuSetSlot(logicalIndex, coreIndex, efficiencyClass));
						}
					}
					if (list.Count == 0)
					{
						return null;
					}
					AppLogger.Info($"CPU Sets topology: Logical={list.Count}, Cores={list.Select((RawCpuSetSlot slot) => slot.CoreIndex).Distinct().Count()}, EfficiencyClasses={string.Join(",", from value in list.Select((RawCpuSetSlot slot) => slot.EfficiencyClass).Distinct()
						orderby value
						select value)}");
					bool isHybrid = list.Select((RawCpuSetSlot slot) => slot.EfficiencyClass).Distinct().Count() > 1;
					byte minEfficiency = list.Min((RawCpuSetSlot slot) => slot.EfficiencyClass);
					return (from slot in list
						orderby slot.LogicalIndex
						group slot by slot.CoreIndex into @group
						orderby @group.Key
						select @group).SelectMany((IGrouping<int, RawCpuSetSlot> group) => group.OrderBy((RawCpuSetSlot slot) => slot.LogicalIndex).Select((RawCpuSetSlot slot, int index) => new CpuLogicalProcessorSlot(slot.LogicalIndex, slot.CoreIndex, index + 1, (isHybrid && slot.EfficiencyClass == minEfficiency) ? CpuCoreType.Efficiency : CpuCoreType.Performance))).ToArray();
				}
				finally
				{
					Marshal.FreeHGlobal(num);
				}
			}
			catch (Exception ex)
			{
				AppLogger.Info("CPU topology detection through CPU Sets failed: " + ex.Message);
				return null;
			}
		}
	}

	private sealed record CpuLogicalProcessorSlot(int LogicalIndex, int CoreIndex, int ThreadNumber, CpuCoreType CoreType);

	private readonly record struct RamInfo(double UsedGb, double TotalGb, double LoadPercent);

	private readonly record struct GpuMemoryInfo(double? UsedGb, double? TotalGb, double? LoadPercent);

	private readonly record struct GpuMemoryCandidate(double ValueMb, int Priority);

	private readonly record struct CpuClockResolution(double? AverageMhz, double? PerformanceAverageMhz, double? EfficiencyAverageMhz, SensorReading? Reading, IReadOnlyList<CpuCoreClockReading> CoreClocks);

	private readonly record struct CpuLoadResolution(IReadOnlyList<double> LogicalThreadLoads, IReadOnlyList<CpuCoreLoadGraphBar> GraphBars);

	private readonly record struct ParsedCpuLoadSensor(int CoreNumber, int? ThreadNumber, string SensorName, double LoadPercent);

	private readonly record struct DriverGpuSnapshot(double? LoadPercent, double? TemperatureC, double? HotspotTemperatureC, double? VramTemperatureC, double? CoreClockMhz, double? MemoryClockMhz, double? VoltageV, double? PowerW, double? FanRpm, double? FanPercent, double? VramUsedGb, double? VramTotalGb, double? VramLoadPercent)
	{
		public static DriverGpuSnapshot Empty { get; } = new DriverGpuSnapshot(null, null, null, null, null, null, null, null, null, null, null, null, null);
	}

	private static class WindowsGpuPerformanceReader
	{
		private readonly record struct GpuAdapterMemoryInfo(double? UsedGb);

		private static readonly TimeSpan CacheLifetime = TimeSpan.FromMilliseconds(750L);

		private static DateTime _lastReadUtc = DateTime.MinValue;

		private static DriverGpuSnapshot _cached = DriverGpuSnapshot.Empty;

		public static DriverGpuSnapshot TryGetIntelSnapshot(IHardware gpu)
		{
			DateTime utcNow = DateTime.UtcNow;
			if (utcNow - _lastReadUtc < CacheLifetime)
			{
				return _cached;
			}
			_lastReadUtc = utcNow;
			try
			{
				double? loadPercent = ReadGpuLoadPercent();
				GpuAdapterMemoryInfo gpuAdapterMemoryInfo = ReadGpuAdapterMemory();
				double? vramTotalGb = ReadIntelAdapterRamGb(gpu.Name);
				double? usedGb = gpuAdapterMemoryInfo.UsedGb;
				double? vramLoadPercent = ((usedGb.HasValue && usedGb.GetValueOrDefault() > 0.0 && vramTotalGb.HasValue && vramTotalGb.GetValueOrDefault() > 0.0) ? new double?(Math.Clamp(usedGb.Value / vramTotalGb.Value * 100.0, 0.0, 100.0)) : ((double?)null));
				_cached = new DriverGpuSnapshot(loadPercent, null, null, null, null, null, null, null, null, null, usedGb, vramTotalGb, vramLoadPercent);
			}
			catch
			{
				_cached = DriverGpuSnapshot.Empty;
			}
			return _cached;
		}

		private static double? ReadGpuLoadPercent()
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
			List<double> list = new List<double>();
			List<double> list2 = new List<double>();
			foreach (ManagementObject item2 in managementObjectSearcher.Get())
			{
				double? num = TryReadDouble(item2, "UtilizationPercentage");
				if (num.HasValue && num.GetValueOrDefault() >= 0.0)
				{
					double item = Math.Clamp(num.Value, 0.0, 100.0);
					string text = TryReadString(item2, "Name");
					list2.Add(item);
					if (ContainsAny(text, "engtype_3D", "engtype_Compute", "engtype_VideoDecode", "engtype_VideoEncode", "engtype_VideoProcessing"))
					{
						list.Add(item);
					}
				}
			}
			if (list.Count > 0)
			{
				return list.Max();
			}
			return (list2.Count > 0) ? new double?(list2.Max()) : ((double?)null);
		}

		private static GpuAdapterMemoryInfo ReadGpuAdapterMemory()
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
			ulong num = 0uL;
			ulong num2 = 0uL;
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
				num += TryReadUInt64(item, "DedicatedUsage") ?? TryReadUInt64(item, "LocalUsage").GetValueOrDefault();
				num2 += TryReadUInt64(item, "SharedUsage") ?? TryReadUInt64(item, "NonLocalUsage").GetValueOrDefault();
			}
			double? num3 = ((num != 0) ? new double?(BytesToGb(num)) : ((double?)null));
			double? num4 = ((num2 != 0) ? new double?(BytesToGb(num2)) : ((double?)null));
			double? usedGb = ((num3.HasValue && num3.GetValueOrDefault() > 0.0) ? ((!num4.HasValue || !(num4.GetValueOrDefault() > 0.0)) ? num3 : (num3 + num4)) : ((!num4.HasValue || !(num4.GetValueOrDefault() > 0.0)) ? ((double?)null) : num4));
			return new GpuAdapterMemoryInfo(usedGb);
		}

		private static double? ReadIntelAdapterRamGb(string selectedGpuName)
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, AdapterRAM FROM Win32_VideoController");
			List<double> list = new List<double>();
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
				string name = TryReadString(item, "Name");
				string vendor = TryReadString(item, "AdapterCompatibility");
				if (IsIntelVideoAdapter(name, vendor, selectedGpuName))
				{
					double num = BytesToGb(TryReadUInt64(item, "AdapterRAM").GetValueOrDefault());
					if (num > 0.0 && num < 128.0)
					{
						list.Add(num);
					}
				}
			}
			return (list.Count > 0) ? new double?(list.Max()) : ((double?)null);
		}

		private static bool IsIntelVideoAdapter(string name, string vendor, string selectedGpuName)
		{
			if (ContainsAny(vendor, "Intel") || ContainsAny(name, "Intel", "UHD", "Iris", "Xe Graphics"))
			{
				return true;
			}
			if (!string.IsNullOrWhiteSpace(selectedGpuName))
			{
				return name.Contains(selectedGpuName, StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}

		private static string TryReadString(ManagementBaseObject source, string propertyName)
		{
			try
			{
				return source.Properties[propertyName]?.Value?.ToString() ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}

		private static double? TryReadDouble(ManagementBaseObject source, string propertyName)
		{
			try
			{
				object obj = source.Properties[propertyName]?.Value;
				return (obj == null) ? ((double?)null) : new double?(Convert.ToDouble(obj, CultureInfo.InvariantCulture));
			}
			catch
			{
				return null;
			}
		}

		private static ulong? TryReadUInt64(ManagementBaseObject source, string propertyName)
		{
			try
			{
				object obj = source.Properties[propertyName]?.Value;
				return (obj == null) ? ((ulong?)null) : new ulong?(Convert.ToUInt64(obj, CultureInfo.InvariantCulture));
			}
			catch
			{
				return null;
			}
		}

		private static double BytesToGb(ulong bytes)
		{
			return (double)bytes / 1024.0 / 1024.0 / 1024.0;
		}
	}

	private static class NvidiaNvmlReader
	{
		private struct NvmlUtilization
		{
			public uint Gpu;

			public uint Memory;
		}

		private struct NvmlMemory
		{
			public ulong Total;

			public ulong Free;

			public ulong Used;
		}

		private const int NvmlSuccess = 0;

		private const int NvmlTemperatureGpu = 0;

		private const int NvmlClockGraphics = 0;

		private const int NvmlClockMem = 2;

		private static bool _initAttempted;

		private static bool _available;

		private static nint _device;

		public static DriverGpuSnapshot TryGetSnapshot()
		{
			if (!EnsureInitialized())
			{
				return DriverGpuSnapshot.Empty;
			}
			double? loadPercent = null;
			double? temperatureC = null;
			double? coreClockMhz = null;
			double? memoryClockMhz = null;
			double? powerW = null;
			double? fanPercent = null;
			double? num = null;
			double? num2 = null;
			double? vramLoadPercent = null;
			try
			{
				if (nvmlDeviceGetUtilizationRates(_device, out var utilization) == 0)
				{
					loadPercent = (IsValidPercent(utilization.Gpu) ? new double?(utilization.Gpu) : ((double?)null));
				}
				if (nvmlDeviceGetTemperature(_device, 0, out var temperature) == 0)
				{
					temperatureC = ((temperature != 0 && temperature < 130) ? new double?(temperature) : ((double?)null));
				}
				if (nvmlDeviceGetClockInfo(_device, 0, out var clock) == 0)
				{
					coreClockMhz = ((clock >= 300 && clock <= 8000) ? new double?(clock) : ((double?)null));
				}
				if (nvmlDeviceGetClockInfo(_device, 2, out var clock2) == 0)
				{
					memoryClockMhz = ((clock2 >= 300 && clock2 <= 30000) ? new double?(clock2) : ((double?)null));
				}
				if (nvmlDeviceGetPowerUsage(_device, out var power) == 0)
				{
					double num3 = (double)power / 1000.0;
					powerW = ((num3 > 0.0 && num3 < 1000.0) ? new double?(num3) : ((double?)null));
				}
				if (nvmlDeviceGetFanSpeed(_device, out var speed) == 0)
				{
					fanPercent = (IsValidPercent(speed) ? new double?(speed) : ((double?)null));
				}
				if (nvmlDeviceGetMemoryInfo(_device, out var memory) == 0)
				{
					num = (double)memory.Used / 1024.0 / 1024.0 / 1024.0;
					num2 = (double)memory.Total / 1024.0 / 1024.0 / 1024.0;
					if (num2.HasValue && num2.GetValueOrDefault() > 0.0)
					{
						vramLoadPercent = num / num2 * 100.0;
					}
				}
				return new DriverGpuSnapshot(loadPercent, temperatureC, null, null, coreClockMhz, memoryClockMhz, null, powerW, null, fanPercent, num, num2, vramLoadPercent);
			}
			catch
			{
				return DriverGpuSnapshot.Empty;
			}
		}

		private static bool EnsureInitialized()
		{
			if (_initAttempted)
			{
				return _available;
			}
			_initAttempted = true;
			TryLoadNvmlLibrary();
			try
			{
				if (nvmlInit_v2() != 0)
				{
					return false;
				}
				if (nvmlDeviceGetHandleByIndex_v2(0u, out _device) != 0 || _device == IntPtr.Zero)
				{
					return false;
				}
				_available = true;
				return true;
			}
			catch
			{
				_available = false;
				return false;
			}
		}

		private static void TryLoadNvmlLibrary()
		{
			try
			{
				if (!NativeLibrary.TryLoad("nvml.dll", out var handle))
				{
					string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll");
					if (File.Exists(text))
					{
						NativeLibrary.TryLoad(text, out handle);
					}
				}
			}
			catch
			{
			}
		}

		private static bool IsValidPercent(uint value)
		{
			return value <= 100;
		}

		[DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern int nvmlInit_v2();

		[DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out nint device);

		[DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern int nvmlDeviceGetUtilizationRates(nint device, out NvmlUtilization utilization);

		[DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern int nvmlDeviceGetTemperature(nint device, int sensorType, out uint temperature);

		[DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern int nvmlDeviceGetClockInfo(nint device, int clockType, out uint clock);

		[DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern int nvmlDeviceGetPowerUsage(nint device, out uint power);

		[DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern int nvmlDeviceGetFanSpeed(nint device, out uint speed);

		[DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern int nvmlDeviceGetMemoryInfo(nint device, out NvmlMemory memory);
	}

	private static class AmdAdlReader
	{
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate nint AdlMainMemoryAllocDelegate(int size);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int AdlMainControlCreateDelegate(AdlMainMemoryAllocDelegate callback, int enumConnectedAdapters);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int AdlAdapterNumberOfAdaptersGetDelegate(out int numberOfAdapters);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int AdlAdapterAdapterInfoGetDelegate(nint info, int inputSize);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int AdlAdapterActiveGetDelegate(int adapterIndex, out int status);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int Adl2MainControlCreateDelegate(AdlMainMemoryAllocDelegate callback, int enumConnectedAdapters, out nint context);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int Adl2AdapterNumberOfAdaptersGetDelegate(nint context, ref int numberOfAdapters);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int Adl2AdapterAdapterInfoGetDelegate(nint context, nint info, int inputSize);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int Adl2AdapterActiveGetDelegate(nint context, int adapterIndex, out int status);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int Adl2DevicePmLogDeviceCreateDelegate(nint context, int adapterIndex, ref uint device);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int Adl2AdapterPmLogSupportGetDelegate(nint context, int adapterIndex, ref AdlPmLogSupportInfo supportInfo);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int Adl2AdapterPmLogStartDelegate(nint context, int adapterIndex, ref AdlPmLogStartInput input, ref AdlPmLogStartOutput output, uint device);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int Adl2AdapterPmLogStopDelegate(nint context, int adapterIndex, uint device);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int Adl2NewQueryPmLogDataGetDelegate(nint context, int adapterIndex, ref AdlPmLogDataOutput output);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int AdlOverdrive5CurrentActivityGetDelegate(int adapterIndex, ref AdlPmActivity activity);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int AdlOverdrive5TemperatureGetDelegate(int adapterIndex, int thermalControllerIndex, ref AdlTemperature temperature);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int AdlOverdrive6TemperatureGetDelegate(int adapterIndex, out int temperature);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int AdlOverdrive6CurrentPowerGetDelegate(int adapterIndex, int powerType, out int currentValue);

		private enum AdlPmLogSensor
		{
			MaxTypes = 0,
			GfxClock = 1,
			MemoryClock = 2,
			SocClock = 3,
			TemperatureEdge = 8,
			TemperatureMem = 9,
			SocVoltage = 16,
			SocPower = 17,
			GfxActivity = 19,
			MemoryActivity = 20,
			GfxVoltage = 21,
			MemoryVoltage = 22,
			AsicPower = 23,
			TemperatureVrSoc = 24,
			TemperatureHotspot = 27,
			TemperatureGfx = 28,
			TemperatureSoc = 29,
			GfxPower = 30,
			BoardPower = 73
		}

		private struct AdlSingleSensorData
		{
			public int Supported;

			public int Value;
		}

		private struct AdlPmLogDataOutput
		{
			public int Size;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
			public AdlSingleSensorData[] Sensors;
		}

		private struct AdlPmLogSupportInfo
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
			public ushort[] Sensors;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
			public int[] Reserved;
		}

		private struct AdlPmLogStartInput
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
			public ushort[] Sensors;

			public uint SampleRate;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)]
			public int[] Reserved;
		}

		[StructLayout(LayoutKind.Explicit)]
		private struct AdlPmLogStartOutput
		{
			[FieldOffset(0)]
			public nint LoggingAddress;

			[FieldOffset(0)]
			public ulong RawLoggingAddress;
		}

		private struct AdlPmLogData
		{
			public uint Version;

			public uint ActiveSampleRate;

			public ulong LastUpdated;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
			public uint[] Values;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
			public uint[] Reserved;
		}

		private struct AdlAdapterInfo
		{
			public int Size;

			public int AdapterIndex;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string Udid;

			public int BusNumber;

			public int DeviceNumber;

			public int FunctionNumber;

			public int VendorId;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string AdapterName;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string DisplayName;

			public int Present;

			public int Exist;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string DriverPath;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string DriverPathExt;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string PnpString;

			public int OsDisplayIndex;
		}

		private struct AdlTemperature
		{
			public int Size;

			public int Temperature;
		}

		private struct AdlPmActivity
		{
			public int Size;

			public int EngineClock;

			public int MemoryClock;

			public int Vddc;

			public int ActivityPercent;

			public int CurrentPerformanceLevel;

			public int CurrentBusSpeed;

			public int CurrentBusLanes;

			public int MaximumBusLanes;

			public int Reserved;
		}

		private const int AdlOk = 0;

		private const int AmdVendorId = 1002;

		private const int AdlPmLogMaxSensors = 256;

		private static readonly AdlMainMemoryAllocDelegate AllocCallback = (int size) => Marshal.AllocHGlobal(size);

		private static bool _initAttempted;

		private static bool _available;

		private static nint _library;

		private static nint _context = IntPtr.Zero;

		private static int _adapterIndex = -1;

		private static uint _pmLogDevice;

		private static int _pmLogSampleRateMs;

		private static bool _pmLogStarted;

		private static AdlPmLogSupportInfo _pmLogSupportInfo;

		private static AdlPmLogStartOutput _pmLogStartOutput;

		private static AdlMainControlCreateDelegate? _mainControlCreate;

		private static AdlAdapterNumberOfAdaptersGetDelegate? _adapterNumberOfAdaptersGet;

		private static AdlAdapterAdapterInfoGetDelegate? _adapterAdapterInfoGet;

		private static AdlAdapterActiveGetDelegate? _adapterActiveGet;

		private static Adl2MainControlCreateDelegate? _adl2MainControlCreate;

		private static Adl2AdapterNumberOfAdaptersGetDelegate? _adl2AdapterNumberOfAdaptersGet;

		private static Adl2AdapterAdapterInfoGetDelegate? _adl2AdapterAdapterInfoGet;

		private static Adl2AdapterActiveGetDelegate? _adl2AdapterActiveGet;

		private static Adl2DevicePmLogDeviceCreateDelegate? _adl2DevicePmLogDeviceCreate;

		private static Adl2AdapterPmLogSupportGetDelegate? _adl2AdapterPmLogSupportGet;

		private static Adl2AdapterPmLogStartDelegate? _adl2AdapterPmLogStart;

		private static Adl2AdapterPmLogStopDelegate? _adl2AdapterPmLogStop;

		private static Adl2NewQueryPmLogDataGetDelegate? _adl2NewQueryPmLogDataGet;

		private static AdlOverdrive5CurrentActivityGetDelegate? _overdrive5CurrentActivityGet;

		private static AdlOverdrive5TemperatureGetDelegate? _overdrive5TemperatureGet;

		private static AdlOverdrive6TemperatureGetDelegate? _overdrive6TemperatureGet;

		private static AdlOverdrive6CurrentPowerGetDelegate? _overdrive6CurrentPowerGet;

		public static DriverGpuSnapshot TryGetSnapshot(int pollIntervalMs)
		{
			if (!EnsureInitialized())
			{
				return DriverGpuSnapshot.Empty;
			}
			double? num = null;
			double? num2 = null;
			double? num3 = null;
			double? num4 = null;
			double? num5 = null;
			double? num6 = null;
			try
			{
				DriverGpuSnapshot driverGpuSnapshot = TryGetPmLogSnapshot(pollIntervalMs);
				num = driverGpuSnapshot.LoadPercent;
				num2 = driverGpuSnapshot.TemperatureC;
				num3 = driverGpuSnapshot.CoreClockMhz;
				num4 = driverGpuSnapshot.MemoryClockMhz;
				num5 = driverGpuSnapshot.VoltageV;
				num6 = driverGpuSnapshot.PowerW;
				if (_overdrive5CurrentActivityGet != null)
				{
					AdlPmActivity activity = new AdlPmActivity
					{
						Size = Marshal.SizeOf<AdlPmActivity>()
					};
					if (_overdrive5CurrentActivityGet(_adapterIndex, ref activity) == 0)
					{
						double? num7 = num;
						if (!num7.HasValue)
						{
							int activityPercent = activity.ActivityPercent;
							num = ((activityPercent >= 0 && activityPercent <= 100) ? new double?(activity.ActivityPercent) : ((double?)null));
						}
						num7 = num3;
						if (!num7.HasValue)
						{
							num3 = NormalizeAdlClockMhz(activity.EngineClock);
						}
						num7 = num4;
						if (!num7.HasValue)
						{
							num4 = NormalizeAdlClockMhz(activity.MemoryClock);
						}
					}
				}
				if (!num2.HasValue && _overdrive6TemperatureGet != null && _overdrive6TemperatureGet(_adapterIndex, out var temperature) == 0)
				{
					num2 = NormalizeAdlTemperature(temperature);
				}
				if (!num2.HasValue && _overdrive5TemperatureGet != null)
				{
					AdlTemperature temperature2 = new AdlTemperature
					{
						Size = Marshal.SizeOf<AdlTemperature>()
					};
					if (_overdrive5TemperatureGet(_adapterIndex, 0, ref temperature2) == 0)
					{
						num2 = NormalizeAdlTemperature(temperature2.Temperature);
					}
				}
				if (!num6.HasValue && _overdrive6CurrentPowerGet != null && _overdrive6CurrentPowerGet(_adapterIndex, 0, out var currentValue) == 0)
				{
					double num8 = (double)currentValue / 256.0;
					num6 = ((num8 > 0.0 && num8 < 1000.0) ? new double?(num8) : ((double?)null));
				}
				return new DriverGpuSnapshot(num, num2, null, null, num3, num4, num5, num6, null, null, null, null, null);
			}
			catch
			{
				return DriverGpuSnapshot.Empty;
			}
		}

		private static bool EnsureInitialized()
		{
			if (_initAttempted)
			{
				return _available;
			}
			_initAttempted = true;
			try
			{
				if (!NativeLibrary.TryLoad("atiadlxx.dll", out _library) && !NativeLibrary.TryLoad("atiadlxy.dll", out _library))
				{
					return false;
				}
				_mainControlCreate = GetDelegate<AdlMainControlCreateDelegate>("ADL_Main_Control_Create");
				_adapterNumberOfAdaptersGet = GetDelegate<AdlAdapterNumberOfAdaptersGetDelegate>("ADL_Adapter_NumberOfAdapters_Get");
				_adapterAdapterInfoGet = GetDelegate<AdlAdapterAdapterInfoGetDelegate>("ADL_Adapter_AdapterInfo_Get");
				_adapterActiveGet = GetDelegate<AdlAdapterActiveGetDelegate>("ADL_Adapter_Active_Get");
				_adl2MainControlCreate = GetDelegate<Adl2MainControlCreateDelegate>("ADL2_Main_Control_Create");
				_adl2AdapterNumberOfAdaptersGet = GetDelegate<Adl2AdapterNumberOfAdaptersGetDelegate>("ADL2_Adapter_NumberOfAdapters_Get");
				_adl2AdapterAdapterInfoGet = GetDelegate<Adl2AdapterAdapterInfoGetDelegate>("ADL2_Adapter_AdapterInfo_Get");
				_adl2AdapterActiveGet = GetDelegate<Adl2AdapterActiveGetDelegate>("ADL2_Adapter_Active_Get");
				_adl2DevicePmLogDeviceCreate = GetDelegate<Adl2DevicePmLogDeviceCreateDelegate>("ADL2_Device_PMLog_Device_Create");
				_adl2AdapterPmLogSupportGet = GetDelegate<Adl2AdapterPmLogSupportGetDelegate>("ADL2_Adapter_PMLog_Support_Get");
				_adl2AdapterPmLogStart = GetDelegate<Adl2AdapterPmLogStartDelegate>("ADL2_Adapter_PMLog_Start");
				_adl2AdapterPmLogStop = GetDelegate<Adl2AdapterPmLogStopDelegate>("ADL2_Adapter_PMLog_Stop");
				_adl2NewQueryPmLogDataGet = GetDelegate<Adl2NewQueryPmLogDataGetDelegate>("ADL2_New_QueryPMLogData_Get");
				_overdrive5CurrentActivityGet = GetDelegate<AdlOverdrive5CurrentActivityGetDelegate>("ADL_Overdrive5_CurrentActivity_Get");
				_overdrive5TemperatureGet = GetDelegate<AdlOverdrive5TemperatureGetDelegate>("ADL_Overdrive5_Temperature_Get");
				_overdrive6TemperatureGet = GetDelegate<AdlOverdrive6TemperatureGetDelegate>("ADL_Overdrive6_Temperature_Get");
				_overdrive6CurrentPowerGet = GetDelegate<AdlOverdrive6CurrentPowerGetDelegate>("ADL_Overdrive6_CurrentPower_Get");
				bool num = _mainControlCreate != null && _adapterNumberOfAdaptersGet != null && _adapterAdapterInfoGet != null && _mainControlCreate(AllocCallback, 1) == 0;
				bool flag = _adl2MainControlCreate != null && _adl2AdapterNumberOfAdaptersGet != null && _adl2AdapterAdapterInfoGet != null && _adl2MainControlCreate(AllocCallback, 1, out _context) == 0 && _context != IntPtr.Zero;
				if (!num && !flag)
				{
					return false;
				}
				_adapterIndex = FindPrimaryAmdAdapterIndex();
				_available = _adapterIndex >= 0;
				return _available;
			}
			catch
			{
				_available = false;
				return false;
			}
		}

		private static int FindPrimaryAmdAdapterIndex()
		{
			int num = FindPrimaryAmdAdapterIndexAdl2();
			if (num >= 0)
			{
				return num;
			}
			return FindPrimaryAmdAdapterIndexLegacy();
		}

		private static int FindPrimaryAmdAdapterIndexAdl2()
		{
			if (_context == IntPtr.Zero || _adl2AdapterNumberOfAdaptersGet == null || _adl2AdapterAdapterInfoGet == null)
			{
				return -1;
			}
			int numberOfAdapters = 0;
			if (_adl2AdapterNumberOfAdaptersGet(_context, ref numberOfAdapters) != 0 || numberOfAdapters <= 0)
			{
				return -1;
			}
			int num = Marshal.SizeOf<AdlAdapterInfo>();
			nint num2 = Marshal.AllocHGlobal(num * numberOfAdapters);
			try
			{
				if (_adl2AdapterAdapterInfoGet(_context, num2, num * numberOfAdapters) != 0)
				{
					return -1;
				}
				int num3 = -1;
				for (int i = 0; i < numberOfAdapters; i++)
				{
					AdlAdapterInfo adapter = Marshal.PtrToStructure<AdlAdapterInfo>(IntPtr.Add(num2, i * num));
					if (IsAmdAdapter(adapter) && adapter.Exist != 0)
					{
						num3 = ((num3 < 0) ? adapter.AdapterIndex : num3);
						if (_adl2AdapterActiveGet == null || (_adl2AdapterActiveGet(_context, adapter.AdapterIndex, out var status) == 0 && status != 0))
						{
							return adapter.AdapterIndex;
						}
					}
				}
				return num3;
			}
			finally
			{
				Marshal.FreeHGlobal(num2);
			}
		}

		private static int FindPrimaryAmdAdapterIndexLegacy()
		{
			if (_adapterNumberOfAdaptersGet == null || _adapterAdapterInfoGet == null)
			{
				return -1;
			}
			if (_adapterNumberOfAdaptersGet(out var numberOfAdapters) != 0 || numberOfAdapters <= 0)
			{
				return -1;
			}
			int num = Marshal.SizeOf<AdlAdapterInfo>();
			nint num2 = Marshal.AllocHGlobal(num * numberOfAdapters);
			try
			{
				if (_adapterAdapterInfoGet(num2, num * numberOfAdapters) != 0)
				{
					return -1;
				}
				int num3 = -1;
				for (int i = 0; i < numberOfAdapters; i++)
				{
					AdlAdapterInfo adapter = Marshal.PtrToStructure<AdlAdapterInfo>(IntPtr.Add(num2, i * num));
					if (IsAmdAdapter(adapter) && adapter.Exist != 0)
					{
						num3 = ((num3 < 0) ? adapter.AdapterIndex : num3);
						if (_adapterActiveGet == null || (_adapterActiveGet(adapter.AdapterIndex, out var status) == 0 && status != 0))
						{
							return adapter.AdapterIndex;
						}
					}
				}
				return num3;
			}
			finally
			{
				Marshal.FreeHGlobal(num2);
			}
		}

		private static bool IsAmdAdapter(AdlAdapterInfo adapter)
		{
			if (adapter.VendorId == 1002)
			{
				return true;
			}
			if (!ContainsAny(adapter.Udid, "VEN_1002", "PCI_VEN_1002") && !ContainsAny(adapter.PnpString, "VEN_1002", "PCI\\VEN_1002"))
			{
				return ContainsAny(adapter.AdapterName, "AMD", "Radeon");
			}
			return true;
		}

		private static DriverGpuSnapshot TryGetPmLogSnapshot(int pollIntervalMs)
		{
			if (_context == IntPtr.Zero || _adapterIndex < 0)
			{
				return DriverGpuSnapshot.Empty;
			}
			EnsurePmLogStarted(Math.Clamp(pollIntervalMs, 100, 1000));
			double? num = null;
			double? num2 = null;
			double? num3 = null;
			double? num4 = null;
			double? num5 = null;
			double? num6 = null;
			if (_pmLogStarted && _pmLogStartOutput.LoggingAddress != IntPtr.Zero)
			{
				AdlPmLogData data = Marshal.PtrToStructure<AdlPmLogData>(_pmLogStartOutput.LoggingAddress);
				num3 = NormalizeAdlClockMhz(TryReadPmLogDataValue(data, AdlPmLogSensor.GfxClock));
				num4 = NormalizeAdlClockMhz(TryReadPmLogDataValue(data, AdlPmLogSensor.MemoryClock));
				num2 = NormalizeAdlTemperature(TryReadPmLogDataValue(data, AdlPmLogSensor.TemperatureEdge) ?? TryReadPmLogDataValue(data, AdlPmLogSensor.TemperatureGfx) ?? TryReadPmLogDataValue(data, AdlPmLogSensor.TemperatureSoc) ?? TryReadPmLogDataValue(data, AdlPmLogSensor.TemperatureVrSoc));
				num5 = NormalizeAdlVoltage(TryReadPmLogDataValue(data, AdlPmLogSensor.GfxVoltage));
				num6 = NormalizeAdlPower(TryReadPmLogDataValue(data, AdlPmLogSensor.GfxPower) ?? TryReadPmLogDataValue(data, AdlPmLogSensor.AsicPower) ?? TryReadPmLogDataValue(data, AdlPmLogSensor.BoardPower) ?? TryReadPmLogDataValue(data, AdlPmLogSensor.SocPower));
				num = NormalizeAdlPercent(TryReadPmLogDataValue(data, AdlPmLogSensor.GfxActivity));
			}
			if (_adl2NewQueryPmLogDataGet != null)
			{
				AdlPmLogDataOutput output = new AdlPmLogDataOutput
				{
					Size = Marshal.SizeOf<AdlPmLogDataOutput>(),
					Sensors = new AdlSingleSensorData[256]
				};
				if (_adl2NewQueryPmLogDataGet(_context, _adapterIndex, ref output) == 0 && output.Sensors != null)
				{
					double? num7 = num3;
					if (!num7.HasValue)
					{
						num3 = NormalizeAdlClockMhz(TryReadPmLogOutputValue(output, AdlPmLogSensor.GfxClock));
					}
					num7 = num4;
					if (!num7.HasValue)
					{
						num4 = NormalizeAdlClockMhz(TryReadPmLogOutputValue(output, AdlPmLogSensor.MemoryClock));
					}
					num7 = num2;
					if (!num7.HasValue)
					{
						num2 = NormalizeAdlTemperature(TryReadPmLogOutputValue(output, AdlPmLogSensor.TemperatureEdge) ?? TryReadPmLogOutputValue(output, AdlPmLogSensor.TemperatureGfx) ?? TryReadPmLogOutputValue(output, AdlPmLogSensor.TemperatureSoc) ?? TryReadPmLogOutputValue(output, AdlPmLogSensor.TemperatureVrSoc));
					}
					num7 = num5;
					if (!num7.HasValue)
					{
						num5 = NormalizeAdlVoltage(TryReadPmLogOutputValue(output, AdlPmLogSensor.GfxVoltage));
					}
					num7 = num6;
					if (!num7.HasValue)
					{
						num6 = NormalizeAdlPower(TryReadPmLogOutputValue(output, AdlPmLogSensor.GfxPower) ?? TryReadPmLogOutputValue(output, AdlPmLogSensor.AsicPower) ?? TryReadPmLogOutputValue(output, AdlPmLogSensor.BoardPower) ?? TryReadPmLogOutputValue(output, AdlPmLogSensor.SocPower));
					}
					num7 = num;
					if (!num7.HasValue)
					{
						num = NormalizeAdlPercent(TryReadPmLogOutputValue(output, AdlPmLogSensor.GfxActivity));
					}
				}
			}
			return new DriverGpuSnapshot(num, num2, null, null, num3, num4, num5, num6, null, null, null, null, null);
		}

		private static void EnsurePmLogStarted(int sampleRateMs)
		{
			if (_context == IntPtr.Zero || _adapterIndex < 0 || _adl2DevicePmLogDeviceCreate == null || _adl2AdapterPmLogSupportGet == null || _adl2AdapterPmLogStart == null || (_pmLogStarted && _pmLogSampleRateMs == sampleRateMs))
			{
				return;
			}
			try
			{
				if (_pmLogStarted && _adl2AdapterPmLogStop != null && _pmLogDevice != 0)
				{
					_adl2AdapterPmLogStop(_context, _adapterIndex, _pmLogDevice);
				}
			}
			catch
			{
			}
			_pmLogStarted = false;
			_pmLogSampleRateMs = sampleRateMs;
			if (_pmLogDevice == 0 && _adl2DevicePmLogDeviceCreate(_context, _adapterIndex, ref _pmLogDevice) != 0)
			{
				return;
			}
			_pmLogSupportInfo = new AdlPmLogSupportInfo
			{
				Sensors = new ushort[256],
				Reserved = new int[16]
			};
			if (_adl2AdapterPmLogSupportGet(_context, _adapterIndex, ref _pmLogSupportInfo) != 0 || _pmLogSupportInfo.Sensors == null)
			{
				return;
			}
			AdlPmLogStartInput input = new AdlPmLogStartInput
			{
				Sensors = new ushort[256],
				SampleRate = (uint)sampleRateMs,
				Reserved = new int[15]
			};
			for (int i = 0; i < 256; i++)
			{
				ushort num = _pmLogSupportInfo.Sensors[i];
				input.Sensors[i] = num;
				if (num == 0)
				{
					break;
				}
			}
			if (_pmLogSupportInfo.Sensors.All((ushort sensor) => sensor != 0))
			{
				input.Sensors[255] = 0;
			}
			_pmLogStartOutput = default(AdlPmLogStartOutput);
			_pmLogStarted = _adl2AdapterPmLogStart(_context, _adapterIndex, ref input, ref _pmLogStartOutput, _pmLogDevice) == 0;
		}

		private static int? TryReadPmLogDataValue(AdlPmLogData data, AdlPmLogSensor sensor)
		{
			if (!_pmLogStarted || _pmLogSupportInfo.Sensors == null || data.Values == null)
			{
				return null;
			}
			for (int i = 0; i < Math.Min(_pmLogSupportInfo.Sensors.Length, 256); i++)
			{
				ushort num = _pmLogSupportInfo.Sensors[i];
				if (num == 0)
				{
					return null;
				}
				if (num == (ushort)sensor)
				{
					int num2 = i * 2 + 1;
					if (num2 >= data.Values.Length)
					{
						return null;
					}
					uint num3 = data.Values[num2];
					if (num3 > int.MaxValue)
					{
						return null;
					}
					return (int)num3;
				}
			}
			return null;
		}

		private static int? TryReadPmLogOutputValue(AdlPmLogDataOutput output, AdlPmLogSensor sensor)
		{
			if (output.Sensors == null || sensor <= AdlPmLogSensor.MaxTypes || (int)sensor >= output.Sensors.Length)
			{
				return null;
			}
			AdlSingleSensorData adlSingleSensorData = output.Sensors[(int)sensor];
			if (adlSingleSensorData.Supported == 0)
			{
				return null;
			}
			return adlSingleSensorData.Value;
		}

		private static double? NormalizeAdlPercent(int? rawValue)
		{
			if (rawValue.HasValue)
			{
				int valueOrDefault = rawValue.GetValueOrDefault();
				if (valueOrDefault < 0 || valueOrDefault > 100)
				{
					return null;
				}
				return valueOrDefault;
			}
			return null;
		}

		private static double? NormalizeAdlVoltage(int? rawValue)
		{
			if (rawValue.HasValue)
			{
				int valueOrDefault = rawValue.GetValueOrDefault();
				if (valueOrDefault > 0)
				{
					double num = ((valueOrDefault > 10) ? ((double)valueOrDefault / 1000.0) : ((double)valueOrDefault));
					if (!(num >= 0.2) || !(num <= 2.5))
					{
						return null;
					}
					return num;
				}
			}
			return null;
		}

		private static double? NormalizeAdlPower(int? rawValue)
		{
			if (rawValue.HasValue)
			{
				int valueOrDefault = rawValue.GetValueOrDefault();
				if (valueOrDefault > 0)
				{
					if (valueOrDefault <= 0 || valueOrDefault >= 1000)
					{
						return null;
					}
					return valueOrDefault;
				}
			}
			return null;
		}

		private static T? GetDelegate<T>(string exportName) where T : Delegate
		{
			try
			{
				if (_library == IntPtr.Zero || !NativeLibrary.TryGetExport(_library, exportName, out var address))
				{
					return null;
				}
				return Marshal.GetDelegateForFunctionPointer<T>(address);
			}
			catch
			{
				return null;
			}
		}

		private static double? NormalizeAdlClockMhz(int? rawClock)
		{
			if (rawClock.HasValue)
			{
				int valueOrDefault = rawClock.GetValueOrDefault();
				if (valueOrDefault > 0)
				{
					double num = ((valueOrDefault > 30000) ? ((double)valueOrDefault / 100.0) : ((double)valueOrDefault));
					if (!(num > 0.0) || !(num <= 30000.0))
					{
						return null;
					}
					return num;
				}
			}
			return null;
		}

		private static double? NormalizeAdlTemperature(int? rawTemperature)
		{
			if (rawTemperature.HasValue)
			{
				int valueOrDefault = rawTemperature.GetValueOrDefault();
				if (valueOrDefault > 0)
				{
					double num = ((valueOrDefault > 1000) ? ((double)valueOrDefault / 1000.0) : ((double)valueOrDefault));
					if (!(num > 0.0) || !(num < 130.0))
					{
						return null;
					}
					return num;
				}
			}
			return null;
		}
	}

	private readonly Computer _computer = new Computer
	{
		IsCpuEnabled = true,
		IsGpuEnabled = true,
		IsMemoryEnabled = true,
		IsMotherboardEnabled = true,
		IsControllerEnabled = false
	};

	private readonly string _staticRamClockText;

	private readonly int _physicalCoreCount;

	private readonly CpuTopology _cpuTopology;

	private int _cpuSensorDumpDelayTicks = 12;

	private bool _cpuSensorDumpHandled;

	private int _diagnosticLogTick;

	private int _pollIntervalMs = 250;

	private long _lastRamTemperatureUpdateTimestamp;

	private IReadOnlyList<RamModuleTemperature> _cachedRamModuleTemperatures = Array.Empty<RamModuleTemperature>();

	private bool _disposed;

	public int PollIntervalMs
	{
		get
		{
			return Math.Clamp(_pollIntervalMs, 100, 1000);
		}
		set
		{
			_pollIntervalMs = Math.Clamp(value, 100, 1000);
		}
	}

	public HardwareMonitorService()
	{
		_computer.Open();
		_staticRamClockText = GetStaticRamClockMhz();
		_physicalCoreCount = GetPhysicalCoreCount();
		_cpuTopology = CpuTopology.Detect(_physicalCoreCount);
	}

	public HardwareSnapshot GetSnapshot()
	{
		bool flag = ShouldUpdateRamTemperatures();
		UpdatePrimaryHardware(flag);
		IReadOnlyList<IHardware> readOnlyList = GetAllHardware().ToArray();
		IReadOnlyList<ISensor> readOnlyList2 = readOnlyList.SelectMany(GetSensorsRecursive).ToArray();
		IHardware hardware = readOnlyList.FirstOrDefault((IHardware hardware2) => hardware2.HardwareType == HardwareType.Cpu);
		IHardware preferredGpu = GetPreferredGpu();
		IReadOnlyList<ISensor> sensors = readOnlyList2;
		IReadOnlyList<ISensor> sensors2 = ((preferredGpu == null) ? Array.Empty<ISensor>() : GetSensorsRecursive(preferredGpu).ToArray());
		RamInfo ramInfo = GetRamInfo();
		IReadOnlyList<RamModuleTemperature> ramModuleTemperatures = GetRamModuleTemperatures(readOnlyList, flag);
		GpuMemoryInfo gpuMemoryInfo = GetGpuMemoryInfo(sensors2, preferredGpu);
		bool flag2 = IsIntegratedAmdGpu(preferredGpu);
		double? num = ((!flag2) ? FindGpuLoad(sensors2) : (FindIntegratedAmdGpuLoad(sensors2) ?? FindGpuLoad(sensors2)));
		double? num2 = FindGpuTemperature(sensors2);
		double? num3 = FindGpuHotspotTemperature(sensors2);
		double? num4 = FindGpuVramTemperature(sensors2);
		double? num5 = FindGpuClock(sensors2);
		double? num6 = FindGpuMemoryClock(sensors2);
		double? num7 = FindGpuPower(sensors2);
		double? num8 = FindGpuFanRpm(sensors2);
		double? num9 = FindGpuFanPercent(sensors2);
		double? num10 = FindGpuVoltage(sensors2);
		DriverGpuSnapshot driverGpu = ResolveDriverGpuSnapshot(preferredGpu, flag2, num, num2, num5, num6, num7, gpuMemoryInfo, PollIntervalMs);
		SensorReading sensorReading = FindCpuTemperature(sensors);
		CpuClockResolution cpuClockResolution = ResolveCpuClock(sensors, _cpuTopology);
		SensorReading reading = cpuClockResolution.Reading;
		SensorReading sensorReading2 = FindCpuPower(sensors);
		double? averageMhz = cpuClockResolution.AverageMhz;
		CpuLoadResolution cpuLoadResolution = ResolveCpuLoads(sensors, _cpuTopology);
		HandleCpuSensorDiagnostics(_computer.Hardware.ToArray(), sensorReading, reading, sensorReading2, averageMhz);
		HardwareSnapshot hardwareSnapshot = new HardwareSnapshot
		{
			CpuName = CleanModelName(hardware?.Name ?? "CPU"),
			CpuLoadPercent = FindCpuLoad(sensors),
			CpuTemperatureC = sensorReading?.Value,
			CpuClockMhz = averageMhz,
			CpuPerformanceClockMhz = cpuClockResolution.PerformanceAverageMhz,
			CpuEfficiencyClockMhz = cpuClockResolution.EfficiencyAverageMhz,
			CpuPowerW = sensorReading2?.Value,
			CpuLogicalThreadLoads = cpuLoadResolution.LogicalThreadLoads,
			CpuPhysicalCoreLoads = FindCpuPhysicalCoreLoads(sensors, _physicalCoreCount),
			CpuCoreLoadGraphBars = cpuLoadResolution.GraphBars,
			CpuCoreClocks = cpuClockResolution.CoreClocks,
			CpuPhysicalCoreCount = _cpuTopology.PhysicalCoreCount,
			CpuLogicalThreadCount = _cpuTopology.LogicalThreadCount,
			CpuPerformanceCoreCount = _cpuTopology.PerformanceCoreCount,
			CpuEfficiencyCoreCount = _cpuTopology.EfficiencyCoreCount,
			GpuName = CleanModelName(preferredGpu?.Name ?? "GPU"),
			GpuLoadPercent = ((!flag2) ? (driverGpu.LoadPercent ?? num) : (num ?? driverGpu.LoadPercent)),
			GpuTemperatureC = ((!flag2) ? (driverGpu.TemperatureC ?? num2) : (driverGpu.TemperatureC ?? num2)),
			GpuHotspotTemperatureC = ((!flag2) ? (driverGpu.HotspotTemperatureC ?? num3) : (num3 ?? driverGpu.HotspotTemperatureC)),
			GpuVramTemperatureC = ((!flag2) ? (driverGpu.VramTemperatureC ?? num4) : (num4 ?? driverGpu.VramTemperatureC)),
			GpuClockMhz = ((!flag2) ? (driverGpu.CoreClockMhz ?? num5) : (driverGpu.CoreClockMhz ?? num5)),
			GpuMemoryClockMhz = ((!flag2) ? (driverGpu.MemoryClockMhz ?? num6) : (driverGpu.MemoryClockMhz ?? num6)),
			GpuVoltageV = ((!flag2) ? num10 : (driverGpu.VoltageV ?? num10)),
			GpuPowerW = ((!flag2) ? (driverGpu.PowerW ?? num7) : (driverGpu.PowerW ?? num7)),
			GpuFanRpm = ((!flag2) ? (driverGpu.FanRpm ?? num8) : (num8 ?? driverGpu.FanRpm)),
			GpuFanPercent = ((!flag2) ? (driverGpu.FanPercent ?? num9) : (num9 ?? driverGpu.FanPercent)),
			VramUsedGb = ((!flag2) ? (driverGpu.VramUsedGb ?? gpuMemoryInfo.UsedGb) : (gpuMemoryInfo.UsedGb ?? driverGpu.VramUsedGb)),
			VramTotalGb = ((!flag2) ? (driverGpu.VramTotalGb ?? gpuMemoryInfo.TotalGb) : (gpuMemoryInfo.TotalGb ?? driverGpu.VramTotalGb)),
			VramLoadPercent = ((!flag2) ? (driverGpu.VramLoadPercent ?? gpuMemoryInfo.LoadPercent) : (gpuMemoryInfo.LoadPercent ?? driverGpu.VramLoadPercent)),
			RamUsedGb = ramInfo.UsedGb,
			RamTotalGb = ramInfo.TotalGb,
			RamLoadPercent = ramInfo.LoadPercent,
			RamSpeedText = _staticRamClockText,
			RamModuleTemperatures = ramModuleTemperatures
		};
		LogExtendedSensorDiagnostics(hardwareSnapshot, readOnlyList, readOnlyList2, hardware, preferredGpu, sensorReading, reading, sensorReading2, averageMhz, driverGpu);
		return hardwareSnapshot;
	}

	private bool ShouldUpdateRamTemperatures()
	{
		long timestamp = Stopwatch.GetTimestamp();
		if (_lastRamTemperatureUpdateTimestamp == 0L)
		{
			return true;
		}
		return (double)(timestamp - _lastRamTemperatureUpdateTimestamp) * 1000.0 / (double)Stopwatch.Frequency >= 1500.0;
	}

	private IReadOnlyList<RamModuleTemperature> GetRamModuleTemperatures(IReadOnlyList<IHardware> allHardware, bool refresh)
	{
		if (!refresh)
		{
			return _cachedRamModuleTemperatures;
		}
		_cachedRamModuleTemperatures = FindRamModuleTemperatures(allHardware);
		_lastRamTemperatureUpdateTimestamp = Stopwatch.GetTimestamp();
		return _cachedRamModuleTemperatures;
	}

	private void UpdatePrimaryHardware(bool updateMemory)
	{
		foreach (IHardware item in _computer.Hardware)
		{
			if (item.HardwareType == HardwareType.Memory)
			{
				if (updateMemory)
				{
					UpdateHardwareRecursive(item);
				}
				continue;
			}
			HardwareType hardwareType = item.HardwareType;
			if (((uint)hardwareType <= 2u || (uint)(hardwareType - 4) <= 2u) ? true : false)
			{
				UpdateHardwareRecursive(item);
			}
		}
	}

	private static void UpdateHardwareRecursive(IHardware hardware)
	{
		hardware.Update();
		IHardware[] subHardware = hardware.SubHardware;
		for (int i = 0; i < subHardware.Length; i++)
		{
			UpdateHardwareRecursive(subHardware[i]);
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_computer.Close();
		}
	}

	private void LogExtendedSensorDiagnostics(HardwareSnapshot snapshot, IReadOnlyList<IHardware> allHardware, IReadOnlyList<ISensor> allSensors, IHardware? cpu, IHardware? gpu, SensorReading? cpuTemperature, SensorReading? cpuClock, SensorReading? cpuPower, double? displayedCpuClockMhz, DriverGpuSnapshot driverGpu)
	{
		if (!AppLogger.SensorDiagnosticsEnabled)
		{
			return;
		}
		try
		{
			int num = ++_diagnosticLogTick;
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("Hardware sensor snapshot:");
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(113, 13, stringBuilder2);
			handler.AppendLiteral("CPU: Name=");
			handler.AppendFormatted(snapshot.CpuName);
			handler.AppendLiteral("; Hardware=");
			handler.AppendFormatted(cpu?.HardwareType);
			handler.AppendLiteral("/");
			handler.AppendFormatted(cpu?.Name);
			handler.AppendLiteral("; Load=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.CpuLoadPercent));
			handler.AppendLiteral("%; Temp=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.CpuTemperatureC));
			handler.AppendLiteral(" C; Clock=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.CpuClockMhz));
			handler.AppendLiteral(" MHz; PClock=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.CpuPerformanceClockMhz));
			handler.AppendLiteral(" MHz; EClock=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.CpuEfficiencyClockMhz));
			handler.AppendLiteral(" MHz; Power=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.CpuPowerW));
			handler.AppendLiteral(" W; Threads=");
			handler.AppendFormatted(snapshot.CpuLogicalThreadCount);
			handler.AppendLiteral("; Cores=");
			handler.AppendFormatted(snapshot.CpuPhysicalCoreCount);
			handler.AppendLiteral("; P=");
			handler.AppendFormatted(snapshot.CpuPerformanceCoreCount);
			handler.AppendLiteral("; E=");
			handler.AppendFormatted(snapshot.CpuEfficiencyCoreCount);
			stringBuilder3.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(64, 4, stringBuilder2);
			handler.AppendLiteral("CPU selected sensors: Temp=");
			handler.AppendFormatted(FormatSensorReading(cpuTemperature));
			handler.AppendLiteral("; Clock=");
			handler.AppendFormatted(FormatSensorReading(cpuClock));
			handler.AppendLiteral("; DisplayedClock=");
			handler.AppendFormatted(FormatNullableDouble(displayedCpuClockMhz));
			handler.AppendLiteral(" MHz; Power=");
			handler.AppendFormatted(FormatSensorReading(cpuPower));
			stringBuilder4.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(152, 16, stringBuilder2);
			handler.AppendLiteral("GPU: Name=");
			handler.AppendFormatted(snapshot.GpuName);
			handler.AppendLiteral("; Hardware=");
			handler.AppendFormatted(gpu?.HardwareType);
			handler.AppendLiteral("/");
			handler.AppendFormatted(gpu?.Name);
			handler.AppendLiteral("; Load=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.GpuLoadPercent));
			handler.AppendLiteral("%; Temp=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.GpuTemperatureC));
			handler.AppendLiteral(" C; Hotspot=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.GpuHotspotTemperatureC));
			handler.AppendLiteral(" C; VRAMTemp=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.GpuVramTemperatureC));
			handler.AppendLiteral(" C; CoreClock=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.GpuClockMhz));
			handler.AppendLiteral(" MHz; MemClock=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.GpuMemoryClockMhz));
			handler.AppendLiteral(" MHz; Voltage=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.GpuVoltageV));
			handler.AppendLiteral(" V; Power=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.GpuPowerW));
			handler.AppendLiteral(" W; Fan=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.GpuFanRpm));
			handler.AppendLiteral(" RPM/");
			handler.AppendFormatted(FormatNullableDouble(snapshot.GpuFanPercent));
			handler.AppendLiteral("%; VRAM=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.VramUsedGb));
			handler.AppendLiteral("/");
			handler.AppendFormatted(FormatNullableDouble(snapshot.VramTotalGb));
			handler.AppendLiteral(" GB; VRAMLoad=");
			handler.AppendFormatted(FormatNullableDouble(snapshot.VramLoadPercent));
			handler.AppendLiteral("%");
			stringBuilder5.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(104, 8, stringBuilder2);
			handler.AppendLiteral("GPU source mode: IntegratedAmd=");
			handler.AppendFormatted(IsIntegratedAmdGpu(gpu));
			handler.AppendLiteral("; Load=");
			handler.AppendFormatted(IsIntegratedAmdGpu(gpu) ? "D3D/LHM" : "DriverFirst");
			handler.AppendLiteral("; Temp=");
			handler.AppendFormatted(driverGpu.TemperatureC.HasValue ? "ADL/PMLog" : "LHM");
			handler.AppendLiteral("; CoreClock=");
			handler.AppendFormatted(driverGpu.CoreClockMhz.HasValue ? "ADL/PMLog" : "LHM");
			handler.AppendLiteral("; MemClock=");
			handler.AppendFormatted(driverGpu.MemoryClockMhz.HasValue ? "ADL/PMLog" : "LHM");
			handler.AppendLiteral("; Voltage=");
			handler.AppendFormatted(driverGpu.VoltageV.HasValue ? "ADL/PMLog" : "LHM");
			handler.AppendLiteral("; Power=");
			handler.AppendFormatted(driverGpu.PowerW.HasValue ? "ADL/PMLog" : "LHM");
			handler.AppendLiteral("; PollInterval=");
			handler.AppendFormatted(PollIntervalMs);
			handler.AppendLiteral(" ms");
			stringBuilder6.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(149, 13, stringBuilder2);
			handler.AppendLiteral("Driver GPU fallback: Load=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.LoadPercent));
			handler.AppendLiteral("%; Temp=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.TemperatureC));
			handler.AppendLiteral(" C; Hotspot=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.HotspotTemperatureC));
			handler.AppendLiteral(" C; VRAMTemp=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.VramTemperatureC));
			handler.AppendLiteral(" C; CoreClock=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.CoreClockMhz));
			handler.AppendLiteral(" MHz; MemClock=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.MemoryClockMhz));
			handler.AppendLiteral(" MHz; Voltage=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.VoltageV));
			handler.AppendLiteral(" V; Power=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.PowerW));
			handler.AppendLiteral(" W; Fan=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.FanRpm));
			handler.AppendLiteral(" RPM/");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.FanPercent));
			handler.AppendLiteral("%; VRAM=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.VramUsedGb));
			handler.AppendLiteral("/");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.VramTotalGb));
			handler.AppendLiteral(" GB; VRAMLoad=");
			handler.AppendFormatted(FormatNullableDouble(driverGpu.VramLoadPercent));
			handler.AppendLiteral("%");
			stringBuilder7.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(58, 5, stringBuilder2);
			handler.AppendLiteral("RAM: Used=");
			handler.AppendFormatted(snapshot.RamUsedGb, "0.###");
			handler.AppendLiteral(" GB; Total=");
			handler.AppendFormatted(snapshot.RamTotalGb, "0.###");
			handler.AppendLiteral(" GB; Load=");
			handler.AppendFormatted(snapshot.RamLoadPercent, "0.###");
			handler.AppendLiteral("%; SpeedText=");
			handler.AppendFormatted(snapshot.RamSpeedText);
			handler.AppendLiteral("; ModuleTemps=");
			handler.AppendFormatted(FormatRamModuleTemperatures(snapshot.RamModuleTemperatures));
			stringBuilder8.AppendLine(ref handler);
			if (num % 20 == 1)
			{
				stringBuilder.AppendLine("Hardware tree:");
				foreach (IHardware item in allHardware)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder9 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(14, 3, stringBuilder2);
					handler.AppendLiteral("- ");
					handler.AppendFormatted(item.HardwareType);
					handler.AppendLiteral(": ");
					handler.AppendFormatted(item.Name);
					handler.AppendLiteral("; Sensors=");
					handler.AppendFormatted(GetSensorsRecursive(item).Count());
					stringBuilder9.AppendLine(ref handler);
				}
				stringBuilder.AppendLine("Critical raw sensors:");
				foreach (ISensor item2 in from sensor in allSensors.Where(IsCriticalDiagnosticSensor)
					orderby sensor.Hardware.HardwareType, sensor.Hardware.Name, sensor.SensorType, sensor.Name
					select sensor)
				{
					string value = (item2.Value.HasValue ? item2.Value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "N/A");
					string value2 = (item2.Min.HasValue ? item2.Min.Value.ToString("0.###", CultureInfo.InvariantCulture) : "N/A");
					string value3 = (item2.Max.HasValue ? item2.Max.Value.ToString("0.###", CultureInfo.InvariantCulture) : "N/A");
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder10 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(38, 8, stringBuilder2);
					handler.AppendLiteral("- ");
					handler.AppendFormatted(item2.Hardware.HardwareType);
					handler.AppendLiteral("/");
					handler.AppendFormatted(item2.Hardware.Name);
					handler.AppendLiteral("/");
					handler.AppendFormatted(item2.SensorType);
					handler.AppendLiteral("/");
					handler.AppendFormatted(item2.Name);
					handler.AppendLiteral(": Value=");
					handler.AppendFormatted(value);
					handler.AppendLiteral("; Min=");
					handler.AppendFormatted(value2);
					handler.AppendLiteral("; Max=");
					handler.AppendFormatted(value3);
					handler.AppendLiteral("; Identifier=");
					handler.AppendFormatted(item2.Identifier);
					stringBuilder10.AppendLine(ref handler);
				}
			}
			AppLogger.SensorDiagnostics(stringBuilder.ToString().TrimEnd());
		}
		catch (Exception ex)
		{
			AppLogger.Info("Extended sensor diagnostics failed: " + ex.Message);
		}
	}

	private static bool IsCriticalDiagnosticSensor(ISensor sensor)
	{
		switch (sensor.SensorType)
		{
		case SensorType.Voltage:
		case SensorType.Power:
		case SensorType.Clock:
		case SensorType.Temperature:
		case SensorType.Load:
		case SensorType.Fan:
		case SensorType.Factor:
		case SensorType.Data:
		case SensorType.SmallData:
			return true;
		default:
			return false;
		}
	}

	private IEnumerable<IHardware> GetAllHardware()
	{
		foreach (IHardware hardware in _computer.Hardware)
		{
			yield return hardware;
			foreach (IHardware item in GetSubHardwareRecursive(hardware))
			{
				yield return item;
			}
		}
	}

	private static IEnumerable<IHardware> GetSubHardwareRecursive(IHardware hardware)
	{
		IHardware[] subHardware = hardware.SubHardware;
		foreach (IHardware subHardware2 in subHardware)
		{
			yield return subHardware2;
			foreach (IHardware item in GetSubHardwareRecursive(subHardware2))
			{
				yield return item;
			}
		}
	}

	private static IEnumerable<ISensor> GetSensorsRecursive(IHardware hardware)
	{
		ISensor[] sensors = hardware.Sensors;
		for (int i = 0; i < sensors.Length; i++)
		{
			yield return sensors[i];
		}
		IHardware[] subHardware = hardware.SubHardware;
		foreach (IHardware hardware2 in subHardware)
		{
			foreach (ISensor item in GetSensorsRecursive(hardware2))
			{
				yield return item;
			}
		}
	}

	private IHardware? GetPreferredGpu()
	{
		IHardware[] array = GetAllHardware().Where(IsGpuHardware).ToArray();
		IEnumerable<IHardware> source;
		if (!array.Any(IsDiscreteGpuHardware))
		{
			IEnumerable<IHardware> enumerable = array;
			source = enumerable;
		}
		else
		{
			source = array.Where(IsDiscreteGpuHardware);
		}
		return source.OrderByDescending(GetGpuPriority).ThenBy((IHardware hardware) => hardware.Name).FirstOrDefault();
	}

	private static bool IsGpuHardware(IHardware hardware)
	{
		HardwareType hardwareType = hardware.HardwareType;
		if ((uint)(hardwareType - 4) <= 2u)
		{
			return true;
		}
		return false;
	}

	private static int GetGpuPriority(IHardware hardware)
	{
		string name = hardware.Name;
		if (hardware.HardwareType == HardwareType.GpuNvidia)
		{
			return 300;
		}
		if (hardware.HardwareType == HardwareType.GpuAmd)
		{
			if (name.Contains("780M", StringComparison.OrdinalIgnoreCase) || name.Contains("760M", StringComparison.OrdinalIgnoreCase) || name.Contains("Graphics", StringComparison.OrdinalIgnoreCase))
			{
				return 150;
			}
			return 250;
		}
		if (hardware.HardwareType == HardwareType.GpuIntel)
		{
			return 100;
		}
		return 0;
	}

	private static double? FindCpuLoad(IReadOnlyList<ISensor> sensors)
	{
		return PickValue(sensors, SensorType.Load, delegate(ISensor sensor)
		{
			string name = sensor.Name;
			if (name.Equals("CPU Total", StringComparison.OrdinalIgnoreCase))
			{
				return 100;
			}
			if (name.Contains("Total", StringComparison.OrdinalIgnoreCase))
			{
				return 90;
			}
			return name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ? 50 : 0;
		});
	}

	private static SensorReading? FindCpuTemperature(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensor(sensors, SensorType.Temperature, delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (IsClearlyNonCpuSensor(sensorSearchText))
			{
				return 0;
			}
			if (IsDistanceToTjMaxSensor(sensorSearchText))
			{
				return 0;
			}
			int num = 0;
			if (IsCpuIdentifier(sensor))
			{
				num = 300;
			}
			if (sensor.Hardware.HardwareType == HardwareType.Cpu)
			{
				num = Math.Max(num, 280);
			}
			if (ContainsAny(sensorSearchText, "CPU Package", "Package"))
			{
				num = 700;
			}
			else if (ContainsAny(sensorSearchText, "Tctl/Tdie", "Tctl", "Tdie"))
			{
				num = 690;
			}
			else if (ContainsAny(sensorSearchText, "Core Max", "CPU Core Max"))
			{
				num = 650;
			}
			else if (ContainsAny(sensorSearchText, "CPU Core"))
			{
				num = 620;
			}
			else if (ContainsAny(sensorSearchText, "P-Core Max", "E-Core Max", "P Core Max", "E Core Max"))
			{
				num = 610;
			}
			else if (ContainsAny(sensorSearchText, "P-Core", "E-Core", "P Core", "E Core", "Core #", "Core", "DTS", "PECI", "Tjunction", "TjMax"))
			{
				num = 500;
			}
			else if (ContainsAny(sensorSearchText, "CPU Die", "Core Average", "CCD", "CCD1", "CCD2"))
			{
				num = 420;
			}
			else if (ContainsAny(sensorSearchText, "Motherboard CPU", "Socket", "CPU", "APU", "SoC"))
			{
				num = 120;
			}
			return AddCpuHardwareBonus(sensor, num);
		}, (double value) => value > 0.0 && value < 130.0);
	}

	private static SensorReading? FindCpuClock(IReadOnlyList<ISensor> sensors)
	{
		CpuClockCandidate[] array = (from candidate in GetCpuClockCandidates(sensors)
			where candidate.Score >= 700
			select candidate).ToArray();
		if (array.Length != 0)
		{
			SensorReading reading = array.OrderByDescending((CpuClockCandidate candidate) => candidate.Value).First().Reading;
			string details = string.Join(", ", from candidate in array.OrderBy<CpuClockCandidate, string>((CpuClockCandidate candidate) => candidate.Reading.SensorName, StringComparer.OrdinalIgnoreCase)
				select candidate.Reading.SensorName);
			return reading with
			{
				SensorName = "Max active core clock (" + reading.SensorName + ")",
				Details = details
			};
		}
		return (from candidate in GetCpuClockCandidates(sensors)
			orderby candidate.Score descending, candidate.Value descending
			select candidate.Reading).FirstOrDefault();
	}

	private static CpuClockResolution ResolveCpuClock(IReadOnlyList<ISensor> sensors, CpuTopology topology)
	{
		CpuClockResolution result = ResolveCpuClockFromRatioAndBus(sensors, topology);
		double? averageMhz = result.AverageMhz;
		if (averageMhz.HasValue && averageMhz.GetValueOrDefault() > 0.0)
		{
			return result;
		}
		CpuClockResolution result2 = ResolveCpuClockFromCoreClockSensors(sensors, topology);
		averageMhz = result2.AverageMhz;
		if (averageMhz.HasValue && averageMhz.GetValueOrDefault() > 0.0)
		{
			return result2;
		}
		SensorReading sensorReading = FindCpuClock(sensors);
		if (!(sensorReading == null))
		{
			return new CpuClockResolution(sensorReading.Value, null, null, sensorReading, Array.Empty<CpuCoreClockReading>());
		}
		return new CpuClockResolution(null, null, null, null, Array.Empty<CpuCoreClockReading>());
	}

	private static CpuClockResolution ResolveCpuClockFromRatioAndBus(IReadOnlyList<ISensor> sensors, CpuTopology topology)
	{
		SensorReading busClock = FindCpuBusClock(sensors);
		double? num = busClock?.Value;
		if (!num.HasValue || !(num.GetValueOrDefault() > 0.0))
		{
			return new CpuClockResolution(null, null, null, null, Array.Empty<CpuCoreClockReading>());
		}
		CpuRatioCandidate[] array = (from @group in (from candidate in GetCpuRatioCandidates(sensors)
				select candidate with
				{
					CoreType = ResolveCpuRatioCoreType(candidate, topology)
				}).GroupBy<CpuRatioCandidate, string>((CpuRatioCandidate candidate) => GetCpuCoreClockGroupKey(candidate.CoreType, candidate.CoreNumber, candidate.Reading.SensorName), StringComparer.OrdinalIgnoreCase)
			select (from candidate in @group
				orderby candidate.Score descending, candidate.Ratio descending
				select candidate).First() into candidate
			orderby GetCpuCoreTypeDisplayOrder(candidate.CoreType), ExtractFirstNumber(candidate.CoreNumber) ?? int.MaxValue
			select candidate).ThenBy<CpuRatioCandidate, string>((CpuRatioCandidate candidate) => candidate.CoreNumber, StringComparer.OrdinalIgnoreCase).ToArray();
		if (array.Length == 0)
		{
			return new CpuClockResolution(null, null, null, null, Array.Empty<CpuCoreClockReading>());
		}
		double value = array.Select((CpuRatioCandidate candidate) => candidate.Ratio * busClock.Value).ToArray().Average();
		double? performanceAverageMhz = AverageRatioClockByType(array, CpuCoreType.Performance, busClock.Value);
		double? efficiencyAverageMhz = AverageRatioClockByType(array, CpuCoreType.Efficiency, busClock.Value);
		IReadOnlyList<CpuCoreClockReading> coreClocks = BuildCpuCoreClocksFromRatios(array, busClock.Value);
		string details = $"BCLK={busClock.Value:0.###} MHz; " + string.Join(", ", array.Select((CpuRatioCandidate candidate) => $"{candidate.CoreNumber}={candidate.Ratio:0.###}x/{candidate.Ratio * busClock.Value:0.###} MHz"));
		SensorReading reading = new SensorReading(busClock.HardwareName, busClock.HardwareType, "Core Ratio x Bus Clock", "Clock", busClock.Identifier, value, details);
		return new CpuClockResolution(value, performanceAverageMhz, efficiencyAverageMhz, reading, coreClocks);
	}

	private static CpuClockResolution ResolveCpuClockFromCoreClockSensors(IReadOnlyList<ISensor> sensors, CpuTopology topology)
	{
		CpuClockCandidate[] array = (from @group in (from candidate in GetCpuClockCandidates(sensors)
				where candidate.Score >= 700
				select candidate with
				{
					CoreType = ResolveCpuClockCoreType(candidate, topology)
				}).GroupBy<CpuClockCandidate, string>((CpuClockCandidate candidate) => GetCpuCoreClockGroupKey(candidate.CoreType, candidate.CoreNumber, candidate.Reading.SensorName), StringComparer.OrdinalIgnoreCase)
			select (from candidate in @group
				orderby candidate.Score descending, candidate.Value descending
				select candidate).First()).ToArray();
		if (array.Length == 0)
		{
			return new CpuClockResolution(null, null, null, null, Array.Empty<CpuCoreClockReading>());
		}
		double value = array.Average((CpuClockCandidate candidate) => candidate.Value);
		double? performanceAverageMhz = AverageCoreClockByType(array, CpuCoreType.Performance);
		double? efficiencyAverageMhz = AverageCoreClockByType(array, CpuCoreType.Efficiency);
		IReadOnlyList<CpuCoreClockReading> coreClocks = BuildCpuCoreClocksFromSensors(array);
		string details = string.Join(", ", from candidate in array
			orderby ExtractFirstNumber(candidate.CoreNumber ?? candidate.Reading.SensorName) ?? int.MaxValue
			select $"{candidate.Reading.SensorName}={candidate.Value:0.###} MHz");
		IOrderedEnumerable<CpuClockCandidate> source = array.OrderByDescending((CpuClockCandidate candidate) => candidate.Score);
		Func<CpuClockCandidate, double> keySelector = (CpuClockCandidate candidate) => candidate.Value;
		SensorReading reading = source.ThenByDescending(keySelector).First().Reading with
		{
			SensorName = "Average core clock",
			Value = value,
			Details = details
		};
		return new CpuClockResolution(value, performanceAverageMhz, efficiencyAverageMhz, reading, coreClocks);
	}

	private static IReadOnlyList<CpuCoreClockReading> BuildCpuCoreClocksFromRatios(IReadOnlyList<CpuRatioCandidate> ratios, double busClockMhz)
	{
		return (from candidate in (from candidate in ratios
				orderby GetCpuCoreTypeDisplayOrder(candidate.CoreType), ExtractFirstNumber(candidate.CoreNumber) ?? int.MaxValue
				select candidate).ThenBy<CpuRatioCandidate, string>((CpuRatioCandidate candidate) => candidate.CoreNumber, StringComparer.OrdinalIgnoreCase)
			group candidate by candidate.CoreType).SelectMany((IGrouping<CpuCoreType, CpuRatioCandidate> group) => group.Select((CpuRatioCandidate candidate, int index) => new CpuCoreClockReading(index, candidate.Ratio * busClockMhz, candidate.CoreType))).ToArray();
	}

	private static IReadOnlyList<CpuCoreClockReading> BuildCpuCoreClocksFromSensors(IReadOnlyList<CpuClockCandidate> candidates)
	{
		return (from candidate in (from candidate in candidates
				orderby GetCpuCoreTypeDisplayOrder(candidate.CoreType), ExtractFirstNumber(candidate.CoreNumber ?? candidate.Reading.SensorName) ?? int.MaxValue
				select candidate).ThenBy<CpuClockCandidate, string>((CpuClockCandidate candidate) => candidate.CoreNumber ?? candidate.Reading.SensorName, StringComparer.OrdinalIgnoreCase)
			group candidate by candidate.CoreType).SelectMany((IGrouping<CpuCoreType, CpuClockCandidate> group) => group.Select((CpuClockCandidate candidate, int index) => new CpuCoreClockReading(index, candidate.Value, candidate.CoreType))).ToArray();
	}

	private static string GetCpuCoreClockGroupKey(CpuCoreType coreType, string? coreNumber, string fallback)
	{
		string value = (string.IsNullOrWhiteSpace(coreNumber) ? fallback : coreNumber);
		return $"{coreType}:{value}";
	}

	private static int GetCpuCoreTypeDisplayOrder(CpuCoreType coreType)
	{
		return coreType switch
		{
			CpuCoreType.Performance => 0, 
			CpuCoreType.Efficiency => 1, 
			_ => 2, 
		};
	}

	private static SensorReading? FindCpuPower(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensor(sensors, SensorType.Power, delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (IsClearlyNonCpuSensor(sensorSearchText))
			{
				return 0;
			}
			int num = 0;
			if (IsCpuIdentifier(sensor))
			{
				num = 300;
			}
			if (sensor.Hardware.HardwareType == HardwareType.Cpu)
			{
				num = Math.Max(num, 280);
			}
			if (ContainsAny(sensorSearchText, "CPU Package", "Package Power", "Power Package", "Package"))
			{
				num = 700;
			}
			else if (ContainsAny(sensorSearchText, "CPU Total", "Processor"))
			{
				num = 650;
			}
			else if (ContainsAny(sensorSearchText, "IA Cores"))
			{
				num = 620;
			}
			else if (ContainsAny(sensorSearchText, "CPU Cores", "CPU Core", "Cores"))
			{
				num = 600;
			}
			else if (ContainsAny(sensorSearchText, "CPU PPT", "PPT"))
			{
				num = 560;
			}
			else if (ContainsAny(sensorSearchText, "SVI2 TFN", "CPU Core SVI2 TFN", "SoC SVI2 TFN"))
			{
				num = 500;
			}
			else if (ContainsAny(sensorSearchText, "SoC", "APU", "GT", "DRAM", "PP0", "PP1", "RAPL"))
			{
				num = 250;
			}
			else if (ContainsAny(sensorSearchText, "CPU", "Core"))
			{
				num = 120;
			}
			if (ContainsAny(sensorSearchText, "Power Limit", "PL1", "PL2"))
			{
				num = 0;
			}
			return AddCpuHardwareBonus(sensor, num);
		}, (double value) => value > 0.0 && value < 1000.0);
	}

	private static IReadOnlyList<double> FindCpuLogicalThreadLoads(IReadOnlyList<ISensor> sensors, int expectedLogicalThreadCount = 0)
	{
		double[] array = (from value in (from sensor in GetCpuLoadSensors(sensors)
				where ContainsAny(GetSensorSearchText(sensor), "Thread", "Tread")
				orderby ExtractFirstNumber(sensor.Name) ?? int.MaxValue, ExtractSecondNumber(sensor.Name) ?? int.MaxValue
				select sensor).Select(GetLoadValue)
			where value.HasValue
			select value.Value).ToArray();
		if (array.Length != 0)
		{
			return NormalizeCpuLoadCount(array, expectedLogicalThreadCount);
		}
		double[] array2 = (from value in (from sensor in GetCpuLoadSensors(sensors)
				where !IsAggregateCpuLoadSensor(sensor)
				orderby ExtractFirstNumber(sensor.Name) ?? int.MaxValue
				select sensor).Select(GetLoadValue)
			where value.HasValue
			select value.Value).ToArray();
		if (array2.Length != 0 && (expectedLogicalThreadCount <= 0 || array2.Length >= expectedLogicalThreadCount))
		{
			return NormalizeCpuLoadCount(array2, expectedLogicalThreadCount);
		}
		double[] array3 = GetWindowsLogicalProcessorLoads().ToArray();
		if (array3.Length != 0)
		{
			return NormalizeCpuLoadCount(array3, expectedLogicalThreadCount);
		}
		return NormalizeCpuLoadCount(array2, expectedLogicalThreadCount);
	}

	private static CpuLoadResolution ResolveCpuLoads(IReadOnlyList<ISensor> sensors, CpuTopology topology)
	{
		IReadOnlyList<ParsedCpuLoadSensor> readOnlyList = ParseCpuLoadSensors(sensors);
		if (readOnlyList.Count > 0)
		{
			return BuildCpuLoadResolutionFromParsedSensors(readOnlyList, topology);
		}
		IReadOnlyList<double> readOnlyList2 = FindCpuLogicalThreadLoads(sensors, topology.LogicalThreadCount);
		return new CpuLoadResolution(readOnlyList2, topology.BuildGraphBars(readOnlyList2));
	}

	private static CpuLoadResolution BuildCpuLoadResolutionFromParsedSensors(IReadOnlyList<ParsedCpuLoadSensor> parsedSensors, CpuTopology topology)
	{
		if (topology.LogicalThreadCount > 0 && parsedSensors.Count < topology.LogicalThreadCount)
		{
			double[] array = GetWindowsLogicalProcessorLoads().ToArray();
			if (array.Length >= topology.LogicalThreadCount)
			{
				IReadOnlyList<double> readOnlyList = NormalizeCpuLoadCount(array, topology.LogicalThreadCount);
				return new CpuLoadResolution(readOnlyList, topology.BuildGraphBars(readOnlyList, "Win32_PerfFormattedData_PerfOS_Processor"));
			}
		}
		ParsedCpuLoadSensor[] source = (from sensor in parsedSensors
			orderby sensor.CoreNumber, sensor.ThreadNumber ?? 1
			select sensor).ToArray();
		int parsedPThreads = source.Count((ParsedCpuLoadSensor sensor) => sensor.ThreadNumber.HasValue);
		int parsedEThreads = source.Count((ParsedCpuLoadSensor sensor) => !sensor.ThreadNumber.HasValue && sensor.CoreNumber > 0);
		CpuCoreLoadGraphBar[] array2 = source.Select((ParsedCpuLoadSensor sensor) => new CpuCoreLoadGraphBar(sensor.LoadPercent, InferCpuCoreType(sensor, parsedPThreads, parsedEThreads, topology), sensor.CoreNumber, sensor.ThreadNumber, sensor.SensorName)).ToArray();
		double[] array3 = new double[Math.Max(topology.LogicalThreadCount, array2.Length)];
		for (int num = 0; num < Math.Min(array3.Length, array2.Length); num++)
		{
			array3[num] = array2[num].LoadPercent;
		}
		return new CpuLoadResolution(array3, array2);
	}

	private static CpuCoreType InferCpuCoreType(ParsedCpuLoadSensor sensor, int parsedPThreads, int parsedEThreads, CpuTopology topology)
	{
		if (topology.EfficiencyCoreCount > 0)
		{
			return topology.GetCoreTypeByOneBasedCoreNumber(sensor.CoreNumber);
		}
		if (sensor.ThreadNumber.HasValue)
		{
			return CpuCoreType.Performance;
		}
		if (parsedPThreads > 0 && parsedEThreads > 0)
		{
			return CpuCoreType.Efficiency;
		}
		return CpuCoreType.Unknown;
	}

	private static IReadOnlyList<ParsedCpuLoadSensor> ParseCpuLoadSensors(IReadOnlyList<ISensor> sensors)
	{
		List<ParsedCpuLoadSensor> list = new List<ParsedCpuLoadSensor>();
		Regex regex = new Regex("CPU\\s+Core\\s+#(?<core>\\d+)(?:\\s+Thread\\s+#(?<thread>\\d+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		foreach (ISensor cpuLoadSensor in GetCpuLoadSensors(sensors))
		{
			double? loadValue = GetLoadValue(cpuLoadSensor);
			if (loadValue.HasValue)
			{
				string text = cpuLoadSensor.Name.Trim();
				Match match = regex.Match(text);
				if (match.Success)
				{
					int coreNumber = int.Parse(match.Groups["core"].Value, CultureInfo.InvariantCulture);
					int? threadNumber = (match.Groups["thread"].Success ? new int?(int.Parse(match.Groups["thread"].Value, CultureInfo.InvariantCulture)) : ((int?)null));
					list.Add(new ParsedCpuLoadSensor(coreNumber, threadNumber, text, loadValue.Value));
				}
			}
		}
		return list;
	}

	private static IReadOnlyList<double> NormalizeCpuLoadCount(IReadOnlyList<double> loads, int expectedCount)
	{
		if (expectedCount <= 0)
		{
			return loads.ToArray();
		}
		double[] array = new double[expectedCount];
		int num = Math.Min(loads.Count, expectedCount);
		for (int i = 0; i < num; i++)
		{
			array[i] = Math.Clamp(loads[i], 0.0, 100.0);
		}
		return array;
	}

	private static IReadOnlyList<double> FindCpuPhysicalCoreLoads(IReadOnlyList<ISensor> sensors, int physicalCoreCount)
	{
		double[] array = (from value in (from sensor in GetCpuLoadSensors(sensors).Where(delegate(ISensor sensor)
				{
					string sensorSearchText = GetSensorSearchText(sensor);
					return !IsAggregateCpuLoadSensor(sensor) && !ContainsAny(sensorSearchText, "Thread", "Tread") && ContainsAny(sensorSearchText, "Core", "P-Core", "E-Core");
				})
				orderby ExtractFirstNumber(sensor.Name) ?? int.MaxValue
				select sensor).Select(GetLoadValue)
			where value.HasValue
			select value.Value).ToArray();
		if (array.Length != 0)
		{
			return array;
		}
		double[] array2 = (from sensor in GetCpuLoadSensors(sensors)
			where ContainsAny(GetSensorSearchText(sensor), "Thread", "Tread")
			select new
			{
				Core = ExtractFirstNumber(sensor.Name),
				Value = GetLoadValue(sensor)
			} into item
			where item.Core.HasValue && item.Value.HasValue
			group item by item.Core.Value into @group
			orderby @group.Key
			select @group.Average(item => item.Value.Value)).ToArray();
		if (array2.Length != 0)
		{
			return array2;
		}
		double[] array3 = FindCpuLogicalThreadLoads(sensors).ToArray();
		if (array3.Length == 0)
		{
			double? num = FindCpuLoad(sensors);
			if (!num.HasValue || !(num.GetValueOrDefault() >= 0.0))
			{
				return Array.Empty<double>();
			}
			int count = ((physicalCoreCount <= 0) ? 1 : physicalCoreCount);
			return Enumerable.Repeat(num.Value, count).ToArray();
		}
		int num2 = ((physicalCoreCount > 0) ? Math.Min(physicalCoreCount, array3.Length) : Math.Max(1, array3.Length / 2));
		if (num2 >= array3.Length)
		{
			return array3;
		}
		double[] array4 = new double[num2];
		for (int num3 = 0; num3 < num2; num3++)
		{
			int num4 = (int)Math.Round((double)(num3 * array3.Length) / (double)num2);
			int val = (int)Math.Round((double)((num3 + 1) * array3.Length) / (double)num2);
			val = Math.Max(num4 + 1, Math.Min(val, array3.Length));
			array4[num3] = array3.Skip(num4).Take(val - num4).Average();
		}
		return array4;
	}

	private static IReadOnlyList<double> GetWindowsLogicalProcessorLoads()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name <> '_Total'");
			return (from item in (from ManagementObject processor in managementObjectSearcher.Get()
					select new
					{
						Index = (int.TryParse(Convert.ToString(processor["Name"], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : int.MaxValue),
						Load = TryReadInt(processor["PercentProcessorTime"])
					}).Where(item =>
				{
					int load = item.Load;
					return load >= 0 && load <= 100;
				})
				orderby item.Index
				select (double)item.Load).ToArray();
		}
		catch
		{
			return Array.Empty<double>();
		}
	}

	private static IEnumerable<ISensor> GetCpuLoadSensors(IReadOnlyList<ISensor> sensors)
	{
		return sensors.Where(delegate(ISensor sensor)
		{
			if (sensor.SensorType == SensorType.Load)
			{
				float? value = sensor.Value;
				if (value.HasValue)
				{
					float valueOrDefault = value.GetValueOrDefault();
					if (valueOrDefault >= 0f && valueOrDefault <= 100f)
					{
						if (sensor.Hardware.HardwareType != HardwareType.Cpu && !IsCpuIdentifier(sensor))
						{
							return ContainsAny(GetSensorSearchText(sensor), "CPU", "Core", "Thread");
						}
						return true;
					}
				}
			}
			return false;
		});
	}

	private static bool IsAggregateCpuLoadSensor(ISensor sensor)
	{
		return ContainsAny(GetSensorSearchText(sensor), "Total", "Package", "Average", "Max", "Memory", "GPU", "VRAM", "GDDR");
	}

	private static double? GetLoadValue(ISensor sensor)
	{
		float? value = sensor.Value;
		if (value.HasValue)
		{
			float valueOrDefault = value.GetValueOrDefault();
			if (valueOrDefault >= 0f && valueOrDefault <= 100f)
			{
				return Math.Clamp(sensor.Value.Value, 0.0, 100.0);
			}
		}
		return null;
	}

	private static double? FindGpuLoad(IReadOnlyList<ISensor> sensors)
	{
		return PickValue(sensors, SensorType.Load, delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (ContainsAny(sensorSearchText, "Memory", "VRAM", "Dedicated", "Shared"))
			{
				return 0;
			}
			if (sensor.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
			{
				return 130;
			}
			if (ContainsAny(sensorSearchText, "GPU Core"))
			{
				return 120;
			}
			if (sensor.Name.Equals("D3D 3D", StringComparison.OrdinalIgnoreCase))
			{
				return 110;
			}
			if (ContainsAny(sensorSearchText, "3D", "Render", "Compute", "Video Processing", "Video Decode", "Video Encode"))
			{
				return 95;
			}
			if (ContainsAny(sensorSearchText, "Graphics", "GT Cores", "GT Core", "Engine"))
			{
				return 80;
			}
			return ContainsAny(sensorSearchText, "GPU") ? 60 : 0;
		});
	}

	private static double? FindIntegratedAmdGpuLoad(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensorValue(sensors, SensorType.Load, delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (ContainsAny(sensorSearchText, "Memory", "VRAM", "Dedicated", "Shared"))
			{
				return 0;
			}
			if (ContainsAny(sensorSearchText, "D3D 3D", "High Priority 3D", "D3D Compute", "D3D Copy", "D3D Video", "Video Decode", "Video Encode"))
			{
				return 500;
			}
			if (ContainsAny(sensorSearchText, "3D", "Render", "Compute", "Video Processing"))
			{
				return 360;
			}
			if (ContainsAny(sensorSearchText, "GPU Core"))
			{
				return 120;
			}
			return ContainsAny(sensorSearchText, "GPU") ? 80 : 0;
		}, (double value) => value >= 0.0 && value <= 100.0);
	}

	private static double? FindGpuTemperature(IReadOnlyList<ISensor> sensors)
	{
		return PickValue(sensors, SensorType.Temperature, delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (sensor.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
			{
				return 120;
			}
			if (ContainsAny(sensorSearchText, "GPU Core"))
			{
				return 115;
			}
			if (ContainsAny(sensorSearchText, "GPU Temperature"))
			{
				return 110;
			}
			if (ContainsAny(sensorSearchText, "Graphics", "GT Cores", "GT Core"))
			{
				return 95;
			}
			if (ContainsAny(sensorSearchText, "Hot Spot", "Hotspot"))
			{
				return 50;
			}
			return ContainsAny(sensorSearchText, "GPU") ? 40 : 0;
		}, preferMaximumWhenSameScore: true);
	}

	private static double? FindGpuHotspotTemperature(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensorValue(sensors, SensorType.Temperature, delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (ContainsAny(sensorSearchText, "Memory", "VRAM", "GDDR", "HBM"))
			{
				return 0;
			}
			return ContainsAny(sensorSearchText, "Hot Spot", "Hotspot", "Junction", "Tjunction") ? 300 : 0;
		}, (double value) => value > 0.0 && value < 140.0);
	}

	private static double? FindGpuVramTemperature(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensorValue(sensors, SensorType.Temperature, delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (ContainsAny(sensorSearchText, "Memory Junction", "VRAM Junction", "GDDR", "HBM"))
			{
				return 300;
			}
			return ContainsAny(sensorSearchText, "Memory Temperature", "VRAM Temperature", "GPU Memory") ? 220 : 0;
		}, (double value) => value > 0.0 && value < 140.0);
	}

	private static double? FindGpuClock(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensorValue(sensors, SensorType.Clock, delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (ContainsAny(sensorSearchText, "Memory", "VRAM", "HBM", "GDDR", "Video", "Shader", "SoC"))
			{
				return 0;
			}
			if (ContainsAny(sensorSearchText, "GPU Core", "Core Clock", "Graphics Clock", "Engine Clock"))
			{
				return 120;
			}
			if (ContainsAny(sensorSearchText, "Graphics", "Render", "GT Cores", "GT Core"))
			{
				return 100;
			}
			return ContainsAny(sensorSearchText, "Core") ? 60 : 0;
		}, (double value) => value > 0.0 && value <= 8000.0);
	}

	private static double? FindGpuMemoryClock(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensorValue(sensors, SensorType.Clock, delegate(ISensor sensor)
		{
			string name = sensor.Name;
			if (name.Equals("GPU Memory", StringComparison.OrdinalIgnoreCase))
			{
				return 120;
			}
			if (name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
			{
				return 100;
			}
			return name.Contains("VRAM", StringComparison.OrdinalIgnoreCase) ? 90 : 0;
		}, (double value) => value > 0.0 && value <= 30000.0);
	}

	private static double? FindGpuPower(IReadOnlyList<ISensor> sensors)
	{
		return PickValue(sensors, SensorType.Power, delegate(ISensor sensor)
		{
			string name = sensor.Name;
			if (name.Equals("GPU Package", StringComparison.OrdinalIgnoreCase))
			{
				return 130;
			}
			if (name.Equals("GPU Power", StringComparison.OrdinalIgnoreCase))
			{
				return 120;
			}
			if (name.Contains("Total", StringComparison.OrdinalIgnoreCase))
			{
				return 110;
			}
			if (name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
			{
				return 100;
			}
			return name.Contains("Board", StringComparison.OrdinalIgnoreCase) ? 90 : 0;
		}, preferMaximumWhenSameScore: true);
	}

	private static double? FindGpuVoltage(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensorValue(sensors, SensorType.Voltage, delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (ContainsAny(sensorSearchText, "Memory", "VRAM", "HBM", "GDDR", "Video", "PCIe", "12V", "5V", "3.3V"))
			{
				return 0;
			}
			if (ContainsAny(sensorSearchText, "GPU Core Voltage", "GPU Voltage", "VDDC", "Core Voltage"))
			{
				return 300;
			}
			if (ContainsAny(sensorSearchText, "GPU Core", "GPU"))
			{
				return 220;
			}
			return ContainsAny(sensorSearchText, "Core") ? 120 : 0;
		}, IsValidVoltage, preferMaximumWhenSameScore: false);
	}

	private static double? FindGpuFanRpm(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensorValue(sensors, SensorType.Fan, (ISensor sensor) => ContainsAny(GetSensorSearchText(sensor), "GPU Fan", "Fan #", "Fan 1", "Fan") ? 200 : 0, (double value) => value > 0.0 && value < 10000.0);
	}

	private static double? FindGpuFanPercent(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensorValue(sensors, SensorType.Control, (ISensor sensor) => ContainsAny(GetSensorSearchText(sensor), "GPU Fan", "Fan #", "Fan 1", "Fan") ? 200 : 0, (double value) => value >= 0.0 && value <= 100.0);
	}

	private static GpuMemoryInfo GetGpuMemoryInfo(IReadOnlyList<ISensor> sensors, IHardware? gpu)
	{
		bool flag = gpu != null && IsIntegratedGpuHardware(gpu);
		GpuMemoryCandidate? gpuMemoryCandidate = null;
		GpuMemoryCandidate? gpuMemoryCandidate2 = null;
		GpuMemoryCandidate? gpuMemoryCandidate3 = null;
		double? loadPercent = null;
		foreach (ISensor sensor in sensors)
		{
			if (!sensor.Value.HasValue)
			{
				continue;
			}
			string name = sensor.Name;
			double value = sensor.Value.Value;
			if (sensor.SensorType == SensorType.Load && name.Contains("Memory", StringComparison.OrdinalIgnoreCase) && (flag || !IsSharedGpuMemorySensor(name)))
			{
				if (!loadPercent.HasValue || name.Contains("GPU Memory", StringComparison.OrdinalIgnoreCase))
				{
					loadPercent = value;
				}
				continue;
			}
			SensorType sensorType = sensor.SensorType;
			bool flag2 = (uint)(sensorType - 12) <= 1u;
			if (!flag2 || (!flag && IsSharedGpuMemorySensor(name)))
			{
				continue;
			}
			int dedicatedGpuMemoryPriority = GetDedicatedGpuMemoryPriority(name);
			if (dedicatedGpuMemoryPriority > 0)
			{
				GpuMemoryCandidate candidate = new GpuMemoryCandidate(NormalizeGpuMemoryToMb(value), dedicatedGpuMemoryPriority);
				if (IsGpuMemoryUsedSensor(name))
				{
					gpuMemoryCandidate = PickBetterGpuMemoryCandidate(gpuMemoryCandidate, candidate);
				}
				else if (IsGpuMemoryFreeSensor(name))
				{
					gpuMemoryCandidate2 = PickBetterGpuMemoryCandidate(gpuMemoryCandidate2, candidate);
				}
				else if (IsGpuMemoryTotalSensor(name))
				{
					gpuMemoryCandidate3 = PickBetterGpuMemoryCandidate(gpuMemoryCandidate3, candidate);
				}
			}
		}
		double? num = gpuMemoryCandidate?.ValueMb;
		double? num2 = gpuMemoryCandidate2?.ValueMb;
		double? num3 = gpuMemoryCandidate3?.ValueMb;
		if (!num3.HasValue && num.HasValue && num2.HasValue && gpuMemoryCandidate?.Priority == gpuMemoryCandidate2?.Priority)
		{
			num3 = num + num2;
		}
		if (!num3.HasValue && num.HasValue && loadPercent.HasValue)
		{
			double valueOrDefault = loadPercent.GetValueOrDefault();
			if (valueOrDefault > 0.0 && valueOrDefault <= 100.0)
			{
				num3 = num.Value / (loadPercent.Value / 100.0);
			}
		}
		if (!loadPercent.HasValue && num.HasValue && num3.HasValue && num3.GetValueOrDefault() > 0.0)
		{
			loadPercent = num.Value / num3.Value * 100.0;
		}
		return new GpuMemoryInfo((!num.HasValue) ? ((double?)null) : (num / 1024.0), (!num3.HasValue) ? ((double?)null) : (num3 / 1024.0), loadPercent);
	}

	private static bool IsSharedGpuMemorySensor(string name)
	{
		if (!name.Contains("Shared", StringComparison.OrdinalIgnoreCase) && !name.Contains("System", StringComparison.OrdinalIgnoreCase) && !name.Contains("Host", StringComparison.OrdinalIgnoreCase))
		{
			return name.Contains("Aperture", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static int GetDedicatedGpuMemoryPriority(string name)
	{
		if (name.Contains("D3D Dedicated", StringComparison.OrdinalIgnoreCase))
		{
			return 400;
		}
		if (name.Contains("Dedicated", StringComparison.OrdinalIgnoreCase))
		{
			return 350;
		}
		if (name.Contains("VRAM", StringComparison.OrdinalIgnoreCase))
		{
			return 300;
		}
		if (name.Contains("Local", StringComparison.OrdinalIgnoreCase))
		{
			return 250;
		}
		if (name.Contains("Shared", StringComparison.OrdinalIgnoreCase))
		{
			return 225;
		}
		if (name.Contains("GPU Memory", StringComparison.OrdinalIgnoreCase))
		{
			return 200;
		}
		if (name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
		{
			return 100;
		}
		return 0;
	}

	private static bool IsDiscreteGpuHardware(IHardware hardware)
	{
		return !IsIntegratedGpuHardware(hardware);
	}

	private static bool IsIntegratedGpuHardware(IHardware hardware)
	{
		string name = hardware.Name;
		if (hardware.HardwareType == HardwareType.GpuIntel)
		{
			return !name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
		}
		if (hardware.HardwareType == HardwareType.GpuAmd)
		{
			return ContainsAny(name, "780M", "760M", "740M", "680M", "660M", "610M", "Vega", "Radeon Graphics", "AMD Graphics");
		}
		return false;
	}

	private static bool IsIntegratedAmdGpu(IHardware? hardware)
	{
		if (hardware != null && hardware.HardwareType == HardwareType.GpuAmd)
		{
			return IsIntegratedGpuHardware(hardware);
		}
		return false;
	}

	private static DriverGpuSnapshot ResolveDriverGpuSnapshot(IHardware? gpu, bool preferLiveGpuSensors, double? gpuLoad, double? gpuTemperature, double? gpuClock, double? gpuMemoryClock, double? gpuPower, GpuMemoryInfo vram, int pollIntervalMs)
	{
		if (gpu == null)
		{
			return DriverGpuSnapshot.Empty;
		}
		return gpu.HardwareType switch
		{
			HardwareType.GpuNvidia => NvidiaNvmlReader.TryGetSnapshot(), 
			HardwareType.GpuAmd => AmdAdlReader.TryGetSnapshot(pollIntervalMs), 
			HardwareType.GpuIntel => WindowsGpuPerformanceReader.TryGetIntelSnapshot(gpu), 
			_ => DriverGpuSnapshot.Empty, 
		};
	}

	private static bool IsGpuMemoryUsedSensor(string name)
	{
		if (!name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase) && !name.Contains("Dedicated Memory Used", StringComparison.OrdinalIgnoreCase) && !name.Contains("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase) && !name.Contains("Shared Memory Used", StringComparison.OrdinalIgnoreCase) && !name.Contains("D3D Shared Memory Used", StringComparison.OrdinalIgnoreCase) && !name.Contains("VRAM Used", StringComparison.OrdinalIgnoreCase))
		{
			return name.Contains("Local Memory Used", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsGpuMemoryFreeSensor(string name)
	{
		if (!name.Contains("Memory Free", StringComparison.OrdinalIgnoreCase) && !name.Contains("Dedicated Memory Free", StringComparison.OrdinalIgnoreCase) && !name.Contains("D3D Dedicated Memory Free", StringComparison.OrdinalIgnoreCase) && !name.Contains("Shared Memory Free", StringComparison.OrdinalIgnoreCase) && !name.Contains("D3D Shared Memory Free", StringComparison.OrdinalIgnoreCase) && !name.Contains("VRAM Free", StringComparison.OrdinalIgnoreCase))
		{
			return name.Contains("Local Memory Free", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsGpuMemoryTotalSensor(string name)
	{
		if (!name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase) && !name.Contains("Dedicated Memory Total", StringComparison.OrdinalIgnoreCase) && !name.Contains("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase) && !name.Contains("Shared Memory Total", StringComparison.OrdinalIgnoreCase) && !name.Contains("D3D Shared Memory Total", StringComparison.OrdinalIgnoreCase) && !name.Contains("VRAM Total", StringComparison.OrdinalIgnoreCase))
		{
			return name.Contains("Local Memory Total", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static GpuMemoryCandidate PickBetterGpuMemoryCandidate(GpuMemoryCandidate? current, GpuMemoryCandidate candidate)
	{
		if (!current.HasValue || candidate.Priority > current.Value.Priority)
		{
			return candidate;
		}
		return current.Value;
	}

	private static double NormalizeGpuMemoryToMb(double value)
	{
		if (!(value > 1048576.0))
		{
			return value;
		}
		return value / 1024.0 / 1024.0;
	}

	private static double? PickValue(IReadOnlyList<ISensor> sensors, SensorType sensorType, Func<ISensor, int> scoreSelector, bool preferMaximumWhenSameScore = false)
	{
		return PickBestSensorValue(sensors, sensorType, scoreSelector, (double _) => true, preferMaximumWhenSameScore);
	}

	private static double? PickBestSensorValue(IReadOnlyList<ISensor> sensors, SensorType type, Func<ISensor, int> scoreSelector, Func<double, bool> valueValidator, bool preferMaximumWhenSameScore = true)
	{
		var list = (from sensor in sensors
			where sensor != null && sensor.SensorType == type && sensor.Value.HasValue
			select new
			{
				Sensor = sensor,
				Score = scoreSelector(sensor),
				Value = (double)sensor.Value.Value
			} into item
			where item.Score > 0 && !double.IsNaN(item.Value) && !double.IsInfinity(item.Value) && valueValidator(item.Value)
			select item).ToList();
		if (list.Count == 0)
		{
			return null;
		}
		int bestScore = list.Max(item => item.Score);
		IEnumerable<double> source = from item in list
			where item.Score == bestScore
			select item.Value;
		return preferMaximumWhenSameScore ? source.Max() : source.First();
	}

	private static bool IsValidVoltage(double value)
	{
		if (value >= 0.2)
		{
			return value <= 2.5;
		}
		return false;
	}

	private static SensorReading? PickBestSensor(IReadOnlyList<ISensor> sensors, SensorType type, Func<ISensor, int> scoreSelector, Func<double, bool> valueValidator, bool preferMaximumWhenSameScore = true)
	{
		List<SensorCandidate> list = (from sensor in sensors
			where sensor != null && sensor.SensorType == type && sensor.Value.HasValue
			select new SensorCandidate(CreateSensorReading(sensor, sensor.Value.Value), scoreSelector(sensor)) into candidate
			where candidate.Score > 0 && !double.IsNaN(candidate.Value) && !double.IsInfinity(candidate.Value) && valueValidator(candidate.Value)
			select candidate).ToList();
		if (list.Count == 0)
		{
			return null;
		}
		int bestScore = list.Max((SensorCandidate candidate) => candidate.Score);
		IEnumerable<SensorCandidate> source = list.Where((SensorCandidate candidate) => candidate.Score == bestScore);
		if (!preferMaximumWhenSameScore)
		{
			return source.First().Reading;
		}
		return source.OrderByDescending((SensorCandidate candidate) => candidate.Value).First().Reading;
	}

	private static IReadOnlyList<CpuClockCandidate> GetCpuClockCandidates(IReadOnlyList<ISensor> sensors)
	{
		return sensors.Where((ISensor sensor) => sensor.SensorType == SensorType.Clock && sensor.Value.HasValue).Select(delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (IsClearlyNonCpuSensor(sensorSearchText) || ContainsAny(sensorSearchText, "Bus Speed", "BCLK", "Reference Clock", "Base Clock", "Memory", "GPU", "VRAM"))
			{
				return new CpuClockCandidate(CreateSensorReading(sensor, sensor.Value.Value), 0, CpuCoreType.Unknown, null);
			}
			if (ContainsAny(sensorSearchText, "Effective Clock", "Effective Clocks", "Average", "Total", "CCD", "CCX", "IA Cores"))
			{
				return new CpuClockCandidate(CreateSensorReading(sensor, sensor.Value.Value), 0, CpuCoreType.Unknown, null);
			}
			int num = 0;
			if (IsCpuIdentifier(sensor))
			{
				num = 300;
			}
			if (sensor.Hardware.HardwareType == HardwareType.Cpu)
			{
				num = Math.Max(num, 280);
			}
			if (ContainsAny(sensorSearchText, "P-Core #", "E-Core #", "P Core #", "E Core #"))
			{
				num = 760;
			}
			else if (ContainsAny(sensorSearchText, "P-Core", "E-Core", "P Core", "E Core"))
			{
				num = 740;
			}
			else if (IsPerCoreClockSensor(sensorSearchText))
			{
				num = 720;
			}
			else if (ContainsAny(sensorSearchText, "CPU Core", "Core Clock"))
			{
				num = 700;
			}
			return new CpuClockCandidate(CreateSensorReading(sensor, sensor.Value.Value), AddCpuHardwareBonus(sensor, num), GetCpuCoreTypeFromText(sensorSearchText), ExtractCpuCoreKey(sensorSearchText));
		}).Where(delegate(CpuClockCandidate candidate)
		{
			if (candidate.Score > 0 && !double.IsNaN(candidate.Value) && !double.IsInfinity(candidate.Value))
			{
				double value = candidate.Value;
				if (value >= 300.0)
				{
					return value <= 8000.0;
				}
				return false;
			}
			return false;
		})
			.ToArray();
	}

	private static SensorReading? FindCpuBusClock(IReadOnlyList<ISensor> sensors)
	{
		return PickBestSensor(sensors, SensorType.Clock, delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (IsClearlyNonCpuSensor(sensorSearchText))
			{
				return 0;
			}
			if (ContainsAny(sensorSearchText, "Bus Speed", "BCLK"))
			{
				return 500;
			}
			return ContainsAny(sensorSearchText, "Reference Clock", "Base Clock") ? 350 : 0;
		}, (double value) => value >= 90.0 && value <= 110.0, preferMaximumWhenSameScore: false);
	}

	private static IReadOnlyList<CpuRatioCandidate> GetCpuRatioCandidates(IReadOnlyList<ISensor> sensors)
	{
		return sensors.Where(delegate(ISensor sensor)
		{
			if (sensor.SensorType == SensorType.Factor)
			{
				float? value = sensor.Value;
				if (value.HasValue)
				{
					return value.GetValueOrDefault() > 0f;
				}
				return false;
			}
			return false;
		}).Select(delegate(ISensor sensor)
		{
			string sensorSearchText = GetSensorSearchText(sensor);
			if (IsClearlyNonCpuSensor(sensorSearchText) || ContainsAny(sensorSearchText, "Effective", "Average", "Total", "Max", "Bus", "BCLK", "Reference", "Base"))
			{
				return new CpuRatioCandidate("N/A", 0.0, CpuCoreType.Unknown, 0, CreateSensorReading(sensor, sensor.Value.Value));
			}
			string text = ExtractCpuCoreKey(sensorSearchText);
			if (string.IsNullOrWhiteSpace(text))
			{
				return new CpuRatioCandidate("N/A", 0.0, CpuCoreType.Unknown, 0, CreateSensorReading(sensor, sensor.Value.Value));
			}
			int num = 0;
			if (IsCpuIdentifier(sensor))
			{
				num = 300;
			}
			if (sensor.Hardware.HardwareType == HardwareType.Cpu)
			{
				num = Math.Max(num, 280);
			}
			if (ContainsAny(sensorSearchText, "P-Core", "E-Core", "P Core", "E Core"))
			{
				num = 760;
			}
			else if (IsPerCoreClockSensor(sensorSearchText) || ContainsAny(sensorSearchText, "Core"))
			{
				num = 720;
			}
			return new CpuRatioCandidate(text, sensor.Value.Value, GetCpuCoreTypeFromText(sensorSearchText), AddCpuHardwareBonus(sensor, num), CreateSensorReading(sensor, sensor.Value.Value));
		}).Where(delegate(CpuRatioCandidate candidate)
		{
			if (candidate.Score > 0 && !double.IsNaN(candidate.Ratio) && !double.IsInfinity(candidate.Ratio))
			{
				double ratio = candidate.Ratio;
				if (ratio >= 1.0)
				{
					return ratio <= 100.0;
				}
				return false;
			}
			return false;
		})
			.ToArray();
	}

	private static bool IsPerCoreClockSensor(string text)
	{
		if (!Regex.IsMatch(text, "(?:CPU\\s+)?Core\\s+#?\\d+\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) && !Regex.IsMatch(text, "(?:P|E)[-\\s]?Core\\s+#?\\d+\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			return Regex.IsMatch(text, "/(?:clock|factor)/\\d+\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		}
		return true;
	}

	private static string? ExtractCpuCoreKey(string text)
	{
		Match match = Regex.Match(text, "(?:P|E)[-\\s]?Core\\s+#?(?<core>\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if (match.Success)
		{
			return match.Groups["core"].Value;
		}
		Match match2 = Regex.Match(text, "(?:CPU\\s+)?Core\\s+#?(?<core>\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if (match2.Success)
		{
			return match2.Groups["core"].Value;
		}
		Match match3 = Regex.Match(text, "/(?:clock|factor)/(?<core>\\d+)\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if (!match3.Success)
		{
			return null;
		}
		return match3.Groups["core"].Value;
	}

	private static CpuCoreType GetCpuCoreTypeFromText(string text)
	{
		if (ContainsAny(text, "E-Core", "E Core", "Efficiency"))
		{
			return CpuCoreType.Efficiency;
		}
		if (ContainsAny(text, "P-Core", "P Core", "Performance"))
		{
			return CpuCoreType.Performance;
		}
		return CpuCoreType.Unknown;
	}

	private static CpuCoreType ResolveCpuClockCoreType(CpuClockCandidate candidate, CpuTopology topology)
	{
		if (candidate.CoreType != CpuCoreType.Unknown)
		{
			return candidate.CoreType;
		}
		if (int.TryParse(candidate.CoreNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && topology.EfficiencyCoreCount > 0)
		{
			return topology.GetCoreTypeByOneBasedCoreNumber(result);
		}
		return CpuCoreType.Unknown;
	}

	private static CpuCoreType ResolveCpuRatioCoreType(CpuRatioCandidate candidate, CpuTopology topology)
	{
		if (candidate.CoreType != CpuCoreType.Unknown)
		{
			return candidate.CoreType;
		}
		if (int.TryParse(candidate.CoreNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && topology.EfficiencyCoreCount > 0)
		{
			return topology.GetCoreTypeByOneBasedCoreNumber(result);
		}
		return CpuCoreType.Unknown;
	}

	private static double? AverageCoreClockByType(IReadOnlyList<CpuClockCandidate> candidates, CpuCoreType coreType)
	{
		double[] array = (from candidate in candidates
			where candidate.CoreType == coreType
			select candidate.Value).ToArray();
		if (array.Length != 0)
		{
			return array.Average();
		}
		return null;
	}

	private static double? AverageRatioClockByType(IReadOnlyList<CpuRatioCandidate> candidates, CpuCoreType coreType, double busClockMhz)
	{
		double[] array = (from candidate in candidates
			where candidate.CoreType == coreType
			select candidate.Ratio * busClockMhz).ToArray();
		if (array.Length != 0)
		{
			return array.Average();
		}
		return null;
	}

	private void HandleCpuSensorDiagnostics(IReadOnlyList<IHardware> allHardware, SensorReading? cpuTemperature, SensorReading? cpuClock, SensorReading? cpuPower, double? displayedCpuClockMhz)
	{
		if (_cpuSensorDumpHandled)
		{
			DeleteCpuSensorDumpIfAllFound(cpuTemperature, displayedCpuClockMhz, cpuPower);
			return;
		}
		if (_cpuSensorDumpDelayTicks > 0)
		{
			_cpuSensorDumpDelayTicks--;
			return;
		}
		_cpuSensorDumpHandled = true;
		bool num = cpuTemperature != null && displayedCpuClockMhz.HasValue && cpuPower != null;
		LogSelectedCpuSensors(cpuTemperature, cpuClock, cpuPower, displayedCpuClockMhz);
		if (num)
		{
			DeleteCpuSensorDumpIfAllFound(cpuTemperature, displayedCpuClockMhz, cpuPower);
			return;
		}
		string path = Path.Combine(AppContext.BaseDirectory, "sensors_dump.txt");
		try
		{
			DumpSensorsToFile(allHardware, path, cpuTemperature, cpuClock, cpuPower, displayedCpuClockMhz);
		}
		catch
		{
		}
	}

	private static void LogSelectedCpuSensors(SensorReading? cpuTemperature, SensorReading? cpuClock, SensorReading? cpuPower, double? displayedCpuClockMhz)
	{
		AppLogger.Info("CPU sensor resolver:");
		AppLogger.Info("CPU temperature sensor: " + FormatSensorReading(cpuTemperature));
		AppLogger.Info($"CPU clock sensor: {FormatSensorReading(cpuClock)}; Displayed={FormatNullableDouble(displayedCpuClockMhz)} MHz");
		AppLogger.Info("CPU power sensor: " + FormatSensorReading(cpuPower));
		AppLogger.Info("PawnIO: " + PawnIoInstaller.GetStatus());
	}

	private static string FormatSensorReading(SensorReading? reading)
	{
		if (reading == null)
		{
			return "N/A";
		}
		string value = (string.IsNullOrWhiteSpace(reading.Details) ? string.Empty : ("; Sources=" + reading.Details));
		return $"{reading.HardwareType} / {reading.HardwareName} / {reading.SensorType} / {reading.SensorName} = {reading.Value:0.###}; Identifier={reading.Identifier}{value}";
	}

	private static string FormatNullableDouble(double? value)
	{
		if (value.HasValue)
		{
			return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
		}
		return "N/A";
	}

	private static string FormatRamModuleTemperatures(IReadOnlyList<RamModuleTemperature> values)
	{
		if (values.Count == 0)
		{
			return "N/A";
		}
		return string.Join("; ", values.Select((RamModuleTemperature value) => value.Name + "=" + FormatNullableDouble(value.TemperatureC) + " C"));
	}

	private static void DeleteCpuSensorDumpIfAllFound(SensorReading? cpuTemperature, double? cpuClockMhz, SensorReading? cpuPower)
	{
		if (cpuTemperature == null || !cpuClockMhz.HasValue || cpuPower == null)
		{
			return;
		}
		string path = Path.Combine(AppContext.BaseDirectory, "sensors_dump.txt");
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private static void DumpSensorsToFile(IReadOnlyList<IHardware> hardware, string path, SensorReading? cpuTemperature, SensorReading? cpuClock, SensorReading? cpuPower, double? displayedCpuClockMhz)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("CPU sensor diagnostics:");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("- Temperature: ");
		handler.AppendFormatted((cpuTemperature != null) ? "FOUND" : "MISSING");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
		handler.AppendLiteral("- Clock: ");
		handler.AppendFormatted(displayedCpuClockMhz.HasValue ? "FOUND" : "MISSING");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder.AppendLine("- Voltage: DISABLED");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
		handler.AppendLiteral("- Power: ");
		handler.AppendFormatted((cpuPower != null) ? "FOUND" : "MISSING");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder2);
		handler.AppendLiteral("- Selected temperature: ");
		handler.AppendFormatted(FormatSensorReading(cpuTemperature));
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(35, 2, stringBuilder2);
		handler.AppendLiteral("- Selected clock: ");
		handler.AppendFormatted(FormatSensorReading(cpuClock));
		handler.AppendLiteral("; Displayed: ");
		handler.AppendFormatted(FormatNullableDouble(displayedCpuClockMhz));
		handler.AppendLiteral(" MHz");
		stringBuilder7.AppendLine(ref handler);
		stringBuilder.AppendLine("- Selected voltage: DISABLED");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
		handler.AppendLiteral("- Selected power: ");
		handler.AppendFormatted(FormatSensorReading(cpuPower));
		stringBuilder8.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(26, 1, stringBuilder2);
		handler.AppendLiteral("Running as administrator: ");
		handler.AppendFormatted(IsRunningAsAdministrator() ? "YES" : "NO");
		stringBuilder9.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder2);
		handler.AppendLiteral("PawnIO: ");
		handler.AppendFormatted(PawnIoInstaller.GetStatus());
		stringBuilder10.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Note: CPU sensors with Value 0 or null are visible, but ignored because LibreHardwareMonitor did not return a valid reading.");
		stringBuilder.AppendLine("If CPU temperature/power are missing while running as administrator, install or repair the PawnIO driver used by current LibreHardwareMonitor builds.");
		stringBuilder.AppendLine();
		foreach (IHardware item in hardware)
		{
			AppendHardwareDump(stringBuilder, item, 0);
		}
		File.WriteAllText(path, stringBuilder.ToString(), Encoding.UTF8);
	}

	private static void AppendHardwareDump(StringBuilder builder, IHardware hardware, int depth)
	{
		string value = new string(' ', depth * 2);
		StringBuilder stringBuilder = builder;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 3, stringBuilder);
		handler.AppendFormatted(value);
		handler.AppendLiteral("Hardware: ");
		handler.AppendFormatted(hardware.HardwareType);
		handler.AppendLiteral(" - ");
		handler.AppendFormatted(hardware.Name);
		stringBuilder2.AppendLine(ref handler);
		ISensor[] sensors = hardware.Sensors;
		foreach (ISensor sensor in sensors)
		{
			stringBuilder = builder;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(38, 5, stringBuilder);
			handler.AppendFormatted(value);
			handler.AppendLiteral("  Sensor: ");
			handler.AppendFormatted(sensor.SensorType);
			handler.AppendLiteral(" - ");
			handler.AppendFormatted(sensor.Name);
			handler.AppendLiteral(" - Value: ");
			handler.AppendFormatted(FormatSensorValue(sensor.Value));
			handler.AppendLiteral(" - Identifier: ");
			handler.AppendFormatted(sensor.Identifier);
			stringBuilder3.AppendLine(ref handler);
		}
		IHardware[] subHardware = hardware.SubHardware;
		foreach (IHardware hardware2 in subHardware)
		{
			AppendHardwareDump(builder, hardware2, depth + 1);
		}
	}

	private static string FormatSensorValue(float? value)
	{
		if (value.HasValue)
		{
			return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
		}
		return "null";
	}

	private static bool IsRunningAsAdministrator()
	{
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	private static int AddCpuHardwareBonus(ISensor sensor, int score)
	{
		if (score <= 0)
		{
			return 0;
		}
		switch (sensor.Hardware.HardwareType)
		{
		case HardwareType.Cpu:
			return score + 20;
		case HardwareType.Motherboard:
		case HardwareType.SuperIO:
			return score + 5;
		default:
			return score;
		}
	}

	private static string GetSensorSearchText(ISensor sensor)
	{
		InlineArray4<string> buffer = default(InlineArray4<string>);
		buffer[0] = sensor.Name;
		buffer[1] = sensor.Identifier?.ToString() ?? string.Empty;
		buffer[2] = sensor.Hardware.Name;
		buffer[3] = sensor.Hardware.HardwareType.ToString();
		return string.Join(" ", (ReadOnlySpan<string?>)buffer);
	}

	private static bool IsCpuIdentifier(ISensor sensor)
	{
		string text = sensor.Identifier?.ToString() ?? string.Empty;
		if (!text.StartsWith("/amdcpu/", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("/intelcpu/", StringComparison.OrdinalIgnoreCase))
		{
			return text.StartsWith("/cpu/", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsDistanceToTjMaxSensor(string text)
	{
		if (!text.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase))
		{
			return text.Contains("Distance to TJMax", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool ContainsAny(string text, params string[] needles)
	{
		return needles.Any((string needle) => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
	}

	private static int? ExtractFirstNumber(string text)
	{
		Match match = Regex.Match(text, "\\d+");
		if (!match.Success)
		{
			return null;
		}
		return int.Parse(match.Value, CultureInfo.InvariantCulture);
	}

	private static int? ExtractSecondNumber(string text)
	{
		MatchCollection matchCollection = Regex.Matches(text, "\\d+");
		if (matchCollection.Count < 2)
		{
			return null;
		}
		return int.Parse(matchCollection[1].Value, CultureInfo.InvariantCulture);
	}

	private static bool IsClearlyNonCpuSensor(string text)
	{
		return ContainsAny(text, "GPU", "VRAM", "GDDR", "Memory", "DRAM Used", "Drive", "SSD", "HDD", "Fan", "Battery", "Network");
	}

	private static RamInfo GetRamInfo()
	{
		NativeMethods.MemoryStatusEx buffer = new NativeMethods.MemoryStatusEx
		{
			dwLength = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>()
		};
		if (!NativeMethods.GlobalMemoryStatusEx(ref buffer))
		{
			return new RamInfo(0.0, 0.0, 0.0);
		}
		double num = (double)buffer.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
		double num2 = (double)buffer.ullAvailPhys / 1024.0 / 1024.0 / 1024.0;
		return new RamInfo(num - num2, num, buffer.dwMemoryLoad);
	}

	private static string GetStaticRamClockMhz()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
			List<int> list = new List<int>();
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
				int num = TryReadInt(item["ConfiguredClockSpeed"]);
				int num2 = TryReadInt(item["Speed"]);
				int num3 = ((num > 0) ? num : num2);
				if (num3 > 0)
				{
					list.Add(num3);
				}
			}
			return (list.Count > 0) ? $"{list.Min()} МГц" : "N/A";
		}
		catch
		{
			return "N/A";
		}
	}

	private static int TryReadInt(object? value)
	{
		if (value == null)
		{
			return 0;
		}
		try
		{
			return Convert.ToInt32(value);
		}
		catch
		{
			return 0;
		}
	}

	private static double? GetWindowsCpuClockMhz()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor");
			List<int> list = new List<int>();
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
				int num = TryReadInt(item["CurrentClockSpeed"]);
				int num2 = TryReadInt(item["MaxClockSpeed"]);
				int num3 = ((num > 0) ? num : num2);
				if (num3 >= 300 && num3 <= 8000)
				{
					list.Add(num3);
				}
			}
			return (list.Count > 0) ? new double?(list.Max()) : ((double?)null);
		}
		catch
		{
			return null;
		}
	}

	private static IReadOnlyList<RamModuleTemperature> FindRamModuleTemperatures(IReadOnlyList<IHardware> allHardware)
	{
		List<(string, double)> list = new List<(string, double)>();
		foreach (IHardware item in allHardware.Where((IHardware hardware) => hardware.HardwareType == HardwareType.Memory))
		{
			ISensor sensor = SelectRamModuleTemperatureSensor(GetSensorsRecursive(item).Where(IsUsableRamTemperatureSensor).ToArray());
			if (sensor != null)
			{
				float? value = sensor.Value;
				if (value.HasValue && value.GetValueOrDefault() > 0f && !double.IsNaN(sensor.Value.Value) && !double.IsInfinity(sensor.Value.Value))
				{
					list.Add((GetRamTemperatureSortKey(sensor), sensor.Value.Value));
				}
			}
		}
		return list.OrderBy<(string, double), string>(((string SortKey, double Temperature) item) => item.SortKey, StringComparer.OrdinalIgnoreCase).Select<(string, double), RamModuleTemperature>(((string SortKey, double Temperature) item, int index) => new RamModuleTemperature($"Температура DIMM {index + 1}", item.Temperature)).ToArray();
	}

	private static bool IsUsableRamTemperatureSensor(ISensor sensor)
	{
		if (sensor.SensorType != SensorType.Temperature)
		{
			return false;
		}
		string text = NormalizeSensorText($"{sensor.Hardware.Name} {sensor.Name} {sensor.Identifier}");
		if (!text.Contains("virtual memory") && !text.Contains("total memory") && !text.Contains("memory load"))
		{
			return !text.Contains("used memory");
		}
		return false;
	}

	private static ISensor? SelectRamModuleTemperatureSensor(IReadOnlyList<ISensor> sensors)
	{
		if (sensors.Count == 0)
		{
			return null;
		}
		ISensor sensor = sensors.Where(IsDdr5SpdTemperatureSensor).OrderBy<ISensor, string>(GetRamTemperatureSortKey, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
		if (sensor != null)
		{
			return sensor;
		}
		return sensors.OrderBy<ISensor, string>(GetRamTemperatureSortKey, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
	}

	private static bool IsDdr5SpdTemperatureSensor(ISensor sensor)
	{
		string text = NormalizeSensorText($"{sensor.Hardware.Name} {sensor.Name} {sensor.Identifier}");
		if (!text.Contains("spd hub") && !text.Contains("spd temperature"))
		{
			return text.Contains("spd temp");
		}
		return true;
	}

	private static string GetRamTemperatureSortKey(ISensor sensor)
	{
		string text = sensor.Identifier.ToString();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return sensor.Hardware.Name + "/" + sensor.Name;
	}

	private static string NormalizeSensorText(string value)
	{
		return value.Replace("_", " ", StringComparison.Ordinal).Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant();
	}

	private static int GetPhysicalCoreCount()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
			int num = 0;
			foreach (ManagementObject item in managementObjectSearcher.Get().Cast<ManagementObject>())
			{
				int num2 = TryReadInt(item["NumberOfCores"]);
				if (num2 > 0)
				{
					num += num2;
				}
			}
			return num;
		}
		catch
		{
			return 0;
		}
	}

	private static string CleanModelName(string name)
	{
		return name.Replace("AMD ", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("NVIDIA ", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("Intel(R) ", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace("Intel ", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Trim();
	}

	private static SensorReading CreateSensorReading(ISensor sensor, double value)
	{
		return new SensorReading(sensor.Hardware.Name, sensor.Hardware.HardwareType.ToString(), sensor.Name, sensor.SensorType.ToString(), sensor.Identifier?.ToString() ?? string.Empty, value, null);
	}
}
