using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RemMon;

internal static class OverlayFontApplicator
{
	private static readonly DependencyProperty BaseFontSizeProperty = DependencyProperty.RegisterAttached("BaseFontSize", typeof(double), typeof(OverlayFontApplicator), new PropertyMetadata(double.NaN));

	public static void Apply(DependencyObject root, FontRenderInfo font, double userScale = 1.0)
	{
		ApplyRecursive(root, font, Math.Clamp(userScale, 0.5, 3.0));
	}

	private static void ApplyRecursive(DependencyObject root, FontRenderInfo font, double userScale)
	{
		if (root is TextBlock textBlock)
		{
			double num = Math.Round(GetOrSetBaseFontSize(textBlock) * font.VisualScale * userScale, 2);
			textBlock.FontFamily = font.Family;
			textBlock.FontSize = num;
			textBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			textBlock.LineHeight = Math.Max(1.0, Math.Ceiling(num * 1.12));
		}
		else if (root is Control control)
		{
			double orSetBaseFontSize = GetOrSetBaseFontSize(control);
			control.FontFamily = font.Family;
			control.FontSize = Math.Round(orSetBaseFontSize * font.VisualScale * userScale, 2);
		}
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			ApplyRecursive(VisualTreeHelper.GetChild(root, i), font, userScale);
		}
	}

	private static double GetOrSetBaseFontSize(Control control)
	{
		double num = (double)control.GetValue(BaseFontSizeProperty);
		if (!double.IsNaN(num) && num > 0.0)
		{
			return num;
		}
		num = ((control.FontSize > 0.0) ? control.FontSize : SystemFonts.MessageFontSize);
		control.SetValue(BaseFontSizeProperty, num);
		return num;
	}

	private static double GetOrSetBaseFontSize(TextBlock textBlock)
	{
		double num = (double)textBlock.GetValue(BaseFontSizeProperty);
		if (!double.IsNaN(num) && num > 0.0)
		{
			return num;
		}
		num = ((textBlock.FontSize > 0.0) ? textBlock.FontSize : SystemFonts.MessageFontSize);
		textBlock.SetValue(BaseFontSizeProperty, num);
		return num;
	}
}
