using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RemMon;

internal static class AppDialog
{
	public static AppDialogResult Show(Window? owner, string title, string message, AppDialogKind kind = AppDialogKind.Info, params (string Label, AppDialogResult Result, bool Primary)[] buttons)
	{
		if (buttons.Length == 0)
		{
			buttons = new(string, AppDialogResult, bool)[1] { ("OK", AppDialogResult.Ok, true) };
		}
		AppDialogResult result = AppDialogResult.None;
		Window dialog = CreateWindow(owner, title, 460.0, double.NaN);
		Border border = CreateDialogShell();
		StackPanel stackPanel = CreateRootPanel();
		stackPanel.Children.Add(CreateTitle(title, kind));
		TextBlock content = new TextBlock
		{
			Text = message,
			TextWrapping = TextWrapping.Wrap,
			Foreground = TextBrush(),
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		};
		stackPanel.Children.Add(new ScrollViewer
		{
			Content = content,
			MaxHeight = ((message.Length > 220) ? 260 : 120),
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		});
		stackPanel.Children.Add(CreateButtonRow(buttons, delegate(AppDialogResult value)
		{
			result = value;
			dialog.Close();
		}));
		border.Child = stackPanel;
		dialog.Content = border;
		dialog.ShowDialog();
		return result;
	}

	public static AppDialogResult ShowLoggingRestartWarning(Window? owner)
	{
		AppDialogResult result = AppDialogResult.None;
		Window dialog = CreateWindow(owner, "Логирование датчиков", 520.0, double.NaN);
		Border border = CreateDialogShell();
		StackPanel stackPanel = CreateRootPanel();
		stackPanel.Children.Add(CreateTitle("Логирование датчиков", AppDialogKind.Warning));
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Для применения параметров расширенного логирования приложение будет перезапущено.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = TextBrush(),
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Внимание: расширенное логирование датчиков может заметно увеличить нагрузку на диск и быстро накапливать log-файл. Включайте этот режим только на время диагностики и отключайте сразу после сбора данных.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 76, 76)),
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		stackPanel.Children.Add(CreateButtonRow(new(string, AppDialogResult, bool)[2]
		{
			("Применить", AppDialogResult.Yes, true),
			("Отмена", AppDialogResult.No, false)
		}, delegate(AppDialogResult value)
		{
			result = value;
			dialog.Close();
		}));
		border.Child = stackPanel;
		dialog.Content = border;
		dialog.ShowDialog();
		return result;
	}

	public static AppDialogResult ShowAlreadyRunning()
	{
		AppDialogResult result = AppDialogResult.None;
		Window dialog = CreateWindow(null, "RemMon уже запущен", 410.0, double.NaN);
		Border border = CreateDialogShell();
		StackPanel stackPanel = CreateRootPanel();
		stackPanel.Children.Add(CreateTitle("Программа уже запущена", AppDialogKind.Warning));
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Завершить работающую копию и перезапустить RemMon?",
			TextWrapping = TextWrapping.Wrap,
			Foreground = TextBrush(),
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		stackPanel.Children.Add(CreateButtonRow(new(string, AppDialogResult, bool)[2]
		{
			("Перезапустить", AppDialogResult.Yes, true),
			("Отмена", AppDialogResult.No, false)
		}, delegate(AppDialogResult value)
		{
			result = value;
			dialog.Close();
		}));
		border.Child = stackPanel;
		dialog.Content = border;
		dialog.ShowDialog();
		return result;
	}

	public static void ShowStartupWelcome(Window? owner)
	{
		Window dialog = CreateWindow(null, "Ремский Мониторинг", 600.0, double.NaN);
		Border border = CreateDialogShell();
		StackPanel stackPanel = CreateRootPanel();
		stackPanel.Children.Add(CreateBrandTitle());
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Благодарим за использование Ремского Мониторинга.\n\nЧтобы открыть настройки программы, нажмите F10 или выберите пункт настроек в меню значка приложения в трее.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = TextBrush(),
			FontSize = 15.0,
			LineHeight = 22.0,
			TextAlignment = TextAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Для первого запуска требуется подключение к интернету: программа установит PawnIO в фоновом режиме, чтобы корректно отображать значения датчиков.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 213, 92)),
			FontWeight = FontWeights.SemiBold,
			FontSize = 14.0,
			LineHeight = 20.0,
			TextAlignment = TextAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Официальный сайт PawnIO: https://pawnio.eu/",
			TextWrapping = TextWrapping.Wrap,
			Foreground = new SolidColorBrush(Color.FromRgb(88, 166, byte.MaxValue)),
			FontSize = 14.0,
			TextAlignment = TextAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Программа прошла тестирование, но всё ещё может содержать ошибки и незавершённые элементы. Если заметите проблему, напишите нам: support@r-mont.ru.\n\nПриятного пользования!",
			TextWrapping = TextWrapping.Wrap,
			Foreground = TextBrush(),
			FontSize = 14.0,
			LineHeight = 20.0,
			TextAlignment = TextAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		});
		Button button = CreateButton("Я всё понял", primary: true);
		button.MinWidth = 150.0;
		button.Margin = new Thickness(0.0);
		button.Click += delegate
		{
			dialog.Close();
		};
		stackPanel.Children.Add(new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
			Children = { (UIElement)button }
		});
		border.Child = stackPanel;
		dialog.Content = border;
		dialog.ShowDialog();
	}

	private static TextBlock CreateBrandTitle()
	{
		return new TextBlock
		{
			FontWeight = FontWeights.SemiBold,
			FontSize = 20.0,
			TextAlignment = TextAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0),
			Inlines = 
			{
				(Inline)new Run("Ремский")
				{
					Foreground = TextBrush()
				},
				(Inline)new Run(" Мониторинг")
				{
					Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 106, 0))
				}
			}
		};
	}

	public static Window CreateWindow(Window? owner, string title, double width, double height)
	{
		Window window = new Window
		{
			Owner = ((owner != null && owner.IsVisible) ? owner : null),
			Title = title,
			Width = width,
			MinWidth = Math.Min(width, 420.0),
			MaxWidth = 720.0,
			MaxHeight = 680.0,
			WindowStartupLocation = ((owner == null || !owner.IsVisible) ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner),
			ResizeMode = ResizeMode.NoResize,
			WindowStyle = WindowStyle.None,
			AllowsTransparency = true,
			Background = Brushes.Transparent,
			Topmost = (owner?.Topmost ?? false),
			ShowInTaskbar = false,
			FontFamily = new FontFamily("Segoe UI")
		};
		if (double.IsNaN(height))
		{
			window.SizeToContent = SizeToContent.Height;
		}
		else
		{
			window.Height = height;
		}
		return window;
	}

	public static Border CreateDialogShell()
	{
		return new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(7, 17, 27)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(49, 69, 95)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(16.0)
		};
	}

	public static StackPanel CreateRootPanel()
	{
		return new StackPanel
		{
			Margin = new Thickness(0.0)
		};
	}

	public static TextBlock CreateTitle(string title, AppDialogKind kind)
	{
		TextBlock textBlock = new TextBlock();
		textBlock.Text = title;
		textBlock.FontWeight = FontWeights.SemiBold;
		textBlock.FontSize = 18.0;
		TextBlock textBlock2 = textBlock;
		textBlock2.Foreground = kind switch
		{
			AppDialogKind.Error => new SolidColorBrush(Color.FromRgb(byte.MaxValue, 76, 76)), 
			AppDialogKind.Warning => new SolidColorBrush(Color.FromRgb(byte.MaxValue, 166, 77)), 
			AppDialogKind.UpdateAvailable => new SolidColorBrush(Color.FromRgb(88, 166, byte.MaxValue)), 
			_ => TextBrush(), 
		};
		textBlock.Margin = new Thickness(0.0, 0.0, 0.0, 10.0);
		return textBlock;
	}

	public static StackPanel CreateButtonRow(IEnumerable<(string Label, AppDialogResult Result, bool Primary)> buttons, Action<AppDialogResult> onClick)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		};
		foreach (var button2 in buttons)
		{
			string item = button2.Label;
			AppDialogResult result = button2.Result;
			bool item2 = button2.Primary;
			Button button = CreateButton(item, item2);
			button.Click += delegate
			{
				onClick(result);
			};
			stackPanel.Children.Add(button);
		}
		return stackPanel;
	}

	public static Button CreateButton(string label, bool primary = false)
	{
		Button obj = new Button
		{
			Content = label,
			MinWidth = 110.0,
			Padding = new Thickness(16.0, 8.0, 16.0, 8.0),
			Margin = new Thickness(6.0, 0.0, 0.0, 0.0),
			Foreground = TextBrush(),
			Background = new SolidColorBrush(primary ? Color.FromRgb(22, 119, byte.MaxValue) : Color.FromRgb(17, 27, 42)),
			BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(46, 139, byte.MaxValue) : Color.FromRgb(49, 69, 95)),
			BorderThickness = new Thickness(1.0)
		};
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
		frameworkElementFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
		frameworkElementFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
		frameworkElementFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6.0));
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(ContentPresenter));
		frameworkElementFactory2.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
		frameworkElementFactory2.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		obj.Template = new ControlTemplate(typeof(Button))
		{
			VisualTree = frameworkElementFactory
		};
		return obj;
	}

	public static SolidColorBrush TextBrush()
	{
		return new SolidColorBrush(Color.FromRgb(232, 240, 250));
	}

	public static SolidColorBrush MutedTextBrush()
	{
		return new SolidColorBrush(Color.FromRgb(184, 196, 212));
	}
}
