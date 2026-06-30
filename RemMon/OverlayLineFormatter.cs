using System;
using System.Collections.Generic;
using System.Linq;

namespace RemMon;

internal static class OverlayLineFormatter
{
	public static IReadOnlyList<OverlayLineGroup> FormatGroups(OverlaySettings settings, FpsStats fps, HardwareSnapshot hardware)
	{
		List<OverlayLineGroup> list = new List<OverlayLineGroup>();
		int num;
		if (settings.Fps.HideUnavailableFpsMetrics)
		{
			double? currentFps = fps.CurrentFps;
			num = ((currentFps.HasValue && currentFps.GetValueOrDefault() > 0.0) ? 1 : 0);
		}
		else
		{
			num = 1;
		}
		bool flag = (byte)num != 0;
		if (settings.Sections.Fps)
		{
			List<OverlayLineItem> list2 = new List<OverlayLineItem>();
			if (settings.Fps.ShowFps && flag)
			{
				list2.Add(Item("FPS", fps.CurrentFpsText, 34.0, settings.Fps.TextColor, settings.Fps.ValueColor, OverlayLineValueAlignment.Right, 0.0, 5.0));
			}
			if (settings.Fps.ShowAverage && flag)
			{
				list2.Add(Item("AVG", fps.AverageFpsText, 34.0, settings.Fps.TextColor, settings.Fps.ValueColor, OverlayLineValueAlignment.Right, 0.0, 5.0));
			}
			if (settings.Fps.ShowOnePercentLow && flag)
			{
				list2.Add(Item("1% Low", fps.OnePercentLowText, 34.0, settings.Fps.TextColor, settings.Fps.ValueColor, OverlayLineValueAlignment.Right, 0.0, 5.0));
			}
			if (settings.Fps.ShowPointOnePercentLow && flag)
			{
				list2.Add(Item("0.1% Low", fps.PointOnePercentLowText, 34.0, settings.Fps.TextColor, settings.Fps.ValueColor));
			}
			if (list2.Count > 0)
			{
				list.Add(new OverlayLineGroup(string.Empty, settings.Fps.TextColor, list2));
			}
			if (settings.FrameTimeGraph.ShowMsLabel && flag)
			{
				list.Add(new OverlayLineGroup(string.Empty, settings.FrameTimeGraph.Color, new OverlayLineItem[1] { Item("MS", FormatMs(fps.FrameTimeMs), 48.0, settings.FrameTimeGraph.Color, settings.FrameTimeGraph.Color) }));
			}
		}
		if (settings.Sections.Gpu && settings.Gpu.ShowBlock)
		{
			IReadOnlyList<OverlayLineItem> readOnlyList = FormatHardwareItems(settings.Gpu, settings.MemoryUnit, hardware.GpuLoadPercent, hardware.GpuTemperatureC, hardware.GpuClockMhz, settings.TemperatureUnit, settings.ClockUnit, hardware.GpuVoltageV, hardware.GpuPowerW, hardware.GpuFanRpm, hardware.GpuFanPercent, hardware.VramUsedGb, hardware.VramTotalGb, settings.Gpu.ShowVram, includeVoltage: true, isGpu: true, includeFans: true);
			if (readOnlyList.Count > 0)
			{
				list.Add(new OverlayLineGroup(GetHardwareDisplayName(settings.Gpu, "GPU"), settings.Gpu.TitleColor, readOnlyList));
			}
		}
		if (settings.Sections.Cpu && settings.Cpu.ShowBlock)
		{
			IReadOnlyList<OverlayLineItem> readOnlyList2 = FormatHardwareItems(settings.Cpu, settings.MemoryUnit, hardware.CpuLoadPercent, hardware.CpuTemperatureC, hardware.CpuClockMhz, settings.TemperatureUnit, settings.ClockUnit, null, hardware.CpuPowerW, null, null, null, null, includeMemory: false, includeVoltage: false, isGpu: false, includeFans: false);
			if (readOnlyList2.Count > 0)
			{
				list.Add(new OverlayLineGroup(GetHardwareDisplayName(settings.Cpu, "CPU"), settings.Cpu.TitleColor, readOnlyList2));
			}
		}
		if (settings.Sections.Ram && settings.Ram.ShowBlock)
		{
			List<OverlayLineItem> list3 = new List<OverlayLineItem>();
			if (settings.Ram.ShowUsed)
			{
				list3.Add(Item(string.Empty, FormatRamUsed(hardware.RamUsedGb, hardware.RamTotalGb, settings.Ram.MemoryFormat, settings.MemoryUnit), 104.0, settings.Ram.LabelColor, settings.Ram.ValueColor));
			}
			if (settings.Ram.ShowLoad)
			{
				list3.Add(Item(string.Empty, FormatPercent(hardware.RamLoadPercent), 34.0, settings.Ram.LabelColor, settings.Ram.ValueColor));
			}
			if (settings.Ram.ShowSpeed)
			{
				list3.Add(Item(string.Empty, FormatRamSpeed(hardware.RamSpeedText), 87.0, settings.Ram.LabelColor, settings.Ram.ValueColor, OverlayLineValueAlignment.Left, 5.0));
			}
			if (settings.Ram.ShowTemperatures)
			{
				foreach (RamModuleTemperature ramModuleTemperature in hardware.RamModuleTemperatures)
				{
					list3.Add(Item(ShortenRamName(ramModuleTemperature.Name), FormatTemperature(ramModuleTemperature.TemperatureC, settings.TemperatureUnit), 48.0, settings.Ram.LabelColor, settings.Ram.ValueColor, OverlayLineValueAlignment.Right, 0.0, 5.0));
				}
			}
			if (list3.Count > 0)
			{
				list.Add(new OverlayLineGroup("RAM", settings.Ram.TitleColor, list3));
			}
		}
		if (list.Count == 0)
		{
			list.Add(new OverlayLineGroup(string.Empty, settings.Appearance.TextColor, new OverlayLineItem[1] { Item(string.Empty, "RemMon", 92.0, settings.Appearance.TextColor, settings.Appearance.TextColor) }));
		}
		return list;
	}

	public static string Format(OverlaySettings settings, FpsStats fps, HardwareSnapshot hardware)
	{
		return string.Join("  |  ", from @group in FormatGroups(settings, fps, hardware)
			select string.Join("  ", @group.Items.Select((OverlayLineItem item) => (!string.IsNullOrWhiteSpace(item.Label)) ? (item.Label + " " + item.Value) : item.Value)));
	}

	private static IReadOnlyList<OverlayLineItem> FormatHardwareItems(HardwareBlockSettings settings, string memoryUnit, double? load, double? temperature, double? clock, string temperatureUnit, string clockUnit, double? voltage, double? power, double? fanRpm, double? fanPercent, double? memoryUsed, double? memoryTotal, bool includeMemory, bool includeVoltage, bool isGpu, bool includeFans)
	{
		List<OverlayLineItem> list = new List<OverlayLineItem>();
		if (settings.ShowTemperature)
		{
			list.Add(Item(string.Empty, FormatTemperature(temperature, temperatureUnit), 48.0, settings.LabelColor, GetTemperatureColor(temperature, settings)));
		}
		if (settings.ShowLoad)
		{
			list.Add(Item(string.Empty, FormatPercent(load), 34.0, settings.LabelColor, GetLoadColor(load, settings, isGpu)));
		}
		if (settings.ShowClock)
		{
			list.Add(Item(string.Empty, FormatClock(clock, clockUnit), 72.0, settings.LabelColor, settings.ValueColor));
		}
		if (includeVoltage && settings.ShowVoltage)
		{
			list.Add(Item(string.Empty, FormatVoltage(voltage), 58.0, settings.LabelColor, settings.ValueColor));
		}
		if (settings.ShowPower)
		{
			list.Add(Item(string.Empty, FormatPower(power), 58.0, settings.LabelColor, settings.ValueColor));
		}
		if (includeFans && settings.ShowFanRpm)
		{
			list.Add(Item(string.Empty, FormatRpm(fanRpm), 100.0, settings.LabelColor, settings.ValueColor));
		}
		if (includeFans && settings.ShowFanPercent)
		{
			list.Add(Item(string.Empty, FormatPercent(fanPercent), 34.0, settings.LabelColor, settings.ValueColor));
		}
		if (includeMemory)
		{
			list.Add(Item(string.Empty, FormatMemory(memoryUsed, memoryTotal, memoryUnit), 104.0, settings.LabelColor, settings.ValueColor));
		}
		return list;
	}

	private static OverlayLineItem Item(string label, string value, double width, string labelColor, string valueColor, OverlayLineValueAlignment alignment = OverlayLineValueAlignment.Right, double leftMargin = 0.0, double rightMargin = 0.0)
	{
		return new OverlayLineItem(label, string.IsNullOrWhiteSpace(value) ? "N/A" : value, width, labelColor, valueColor, alignment, leftMargin, rightMargin);
	}

	private static string FormatMs(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		return $"{value.Value:0.0}";
	}

	private static string FormatPercent(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() >= 0.0))
		{
			return "N/A";
		}
		return $"{value.Value:0}%";
	}

	private static string FormatRpm(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		return $"{value.Value:0} Об./мин.";
	}

	private static string FormatVoltage(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		return $"{value.Value:0.000} В";
	}

	private static string FormatPower(double? value)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		return $"{value.Value:0.0} Вт";
	}

	private static string FormatTemperature(double? value, string unit)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		if (!unit.Equals("F", StringComparison.OrdinalIgnoreCase))
		{
			return $"{value.Value:0} °C";
		}
		return $"{value.Value * 9.0 / 5.0 + 32.0:0} °F";
	}

	private static string FormatClock(double? value, string unit)
	{
		if (!value.HasValue || !(value.GetValueOrDefault() > 0.0))
		{
			return "N/A";
		}
		if (!unit.Equals("ГГц", StringComparison.OrdinalIgnoreCase) && !unit.Equals("GHz", StringComparison.OrdinalIgnoreCase))
		{
			return $"{value.Value:0} МГц";
		}
		return $"{value.Value / 1000.0:0.0} ГГц";
	}

	private static string FormatMemory(double? usedGb, double? totalGb, string memoryUnit)
	{
		if (usedGb.HasValue && usedGb.GetValueOrDefault() > 0.0 && totalGb.HasValue && totalGb.GetValueOrDefault() > 0.0)
		{
			return $"{FormatMemoryAmountValue(usedGb.Value, memoryUnit)}/{FormatMemoryAmountValue(totalGb.Value, memoryUnit)} {GetMemoryUnit(memoryUnit)}";
		}
		if (usedGb.HasValue && usedGb.GetValueOrDefault() > 0.0)
		{
			return FormatMemoryAmount(usedGb.Value, memoryUnit);
		}
		return "N/A";
	}

	private static string FormatRamUsed(double usedGb, double totalGb, string memoryFormat, string memoryUnit)
	{
		if (memoryFormat == "UsedOnly")
		{
			return FormatMemoryAmount(usedGb, memoryUnit);
		}
		return $"{FormatMemoryAmountValue(usedGb, memoryUnit)}/{FormatMemoryAmountValue(totalGb, memoryUnit)} {GetMemoryUnit(memoryUnit)}";
	}

	private static string FormatMemoryAmount(double valueGb, string unit)
	{
		return FormatMemoryAmountValue(valueGb, unit) + " " + GetMemoryUnit(unit);
	}

	private static string FormatMemoryAmountValue(double valueGb, string unit)
	{
		if (!(unit == "МБ") && !unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
		{
			return $"{valueGb:0.0}";
		}
		return $"{valueGb * 1024.0:0}";
	}

	private static string GetMemoryUnit(string unit)
	{
		if (!(unit == "МБ") && !unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
		{
			return "ГБ";
		}
		return "МБ";
	}

	private static string FormatRamSpeed(string value)
	{
		if (!string.IsNullOrWhiteSpace(value) && !(value == "N/A"))
		{
			return value;
		}
		return "N/A";
	}

	private static string ShortenRamName(string name)
	{
		string text = name.Trim();
		if (text.Length <= 8)
		{
			return text;
		}
		return text.Substring(0, 8) + "...";
	}

	private static string GetHardwareDisplayName(HardwareBlockSettings settings, string fallbackName)
	{
		if (!string.IsNullOrWhiteSpace(settings.CustomName))
		{
			return settings.CustomName.Trim();
		}
		return fallbackName;
	}

	private static string GetTemperatureColor(double? value, HardwareBlockSettings settings)
	{
		if (value >= (double)settings.CriticalTemperatureC)
		{
			return "#FFFF4C4C";
		}
		if (value >= (double)settings.WarningTemperatureC)
		{
			return "#FFFFA64D";
		}
		return settings.ValueColor;
	}

	private static string GetLoadColor(double? value, HardwareBlockSettings settings, bool isGpu)
	{
		if (!settings.UseLoadGradient || !value.HasValue || !(value.GetValueOrDefault() >= 0.0))
		{
			return settings.ValueColor;
		}
		ColorRgb target = (isGpu ? new ColorRgb(53, 212, 99) : new ColorRgb(byte.MaxValue, 76, 76));
		ColorRgb colorRgb = InterpolateLoadColor(value.Value, target);
		return $"#FF{colorRgb.R:X2}{colorRgb.G:X2}{colorRgb.B:X2}";
	}

	private static ColorRgb InterpolateLoadColor(double loadPercent, ColorRgb target)
	{
		ColorRgb colorRgb = new ColorRgb(242, 242, 242);
		double num = Math.Clamp((loadPercent - 50.0) / 50.0, 0.0, 1.0);
		return new ColorRgb((byte)Math.Round((double)(int)colorRgb.R + (double)(target.R - colorRgb.R) * num), (byte)Math.Round((double)(int)colorRgb.G + (double)(target.G - colorRgb.G) * num), (byte)Math.Round((double)(int)colorRgb.B + (double)(target.B - colorRgb.B) * num));
	}
}
