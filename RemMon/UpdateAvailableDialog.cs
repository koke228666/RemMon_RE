using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RemMon;

internal sealed class UpdateAvailableDialog
{
	private readonly Window _dialog;

	private readonly UpdateService _updateService;

	private readonly UpdateManifest _manifest;

	private readonly Action<bool> _setRemindersEnabled;

	private readonly TextBlock _statusText;

	private readonly ProgressBar _progressBar;

	private readonly Button _downloadButton;

	private readonly Button _laterButton;

	private readonly CheckBox _doNotRemindCheckBox;

	public UpdateAvailableDialog(Window? owner, UpdateService updateService, UpdateCheckResult update, Action<bool> setRemindersEnabled)
	{
		_updateService = updateService;
		_manifest = update.Manifest ?? throw new ArgumentException("Update manifest is required.", "update");
		_setRemindersEnabled = setRemindersEnabled;
		_dialog = AppDialog.CreateWindow(owner, "Доступна новая версия", 560.0, double.NaN);
		_dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		Border border = AppDialog.CreateDialogShell();
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0)
		};
		stackPanel.Children.Add(AppDialog.CreateTitle("Доступна новая версия", AppDialogKind.UpdateAvailable));
		stackPanel.Children.Add(CreateVersionText("Текущая версия: " + update.CurrentVersion));
		stackPanel.Children.Add(CreateVersionText("Новая версия: " + update.ServerVersion));
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Что нового",
			FontWeight = FontWeights.SemiBold,
			Foreground = AppDialog.TextBrush(),
			Margin = new Thickness(0.0, 14.0, 0.0, 6.0)
		});
		stackPanel.Children.Add(CreateNotesBox(_manifest.Notes));
		_statusText = new TextBlock
		{
			Text = "Готово к скачиванию.",
			Foreground = AppDialog.MutedTextBrush(),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 12.0, 0.0, 6.0)
		};
		stackPanel.Children.Add(_statusText);
		_progressBar = new ProgressBar
		{
			Minimum = 0.0,
			Maximum = 100.0,
			Height = 10.0,
			Visibility = Visibility.Collapsed,
			Foreground = new SolidColorBrush(Color.FromRgb(47, 140, byte.MaxValue)),
			Background = new SolidColorBrush(Color.FromRgb(17, 27, 42)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(49, 69, 95)),
			Template = CreateProgressBarTemplate()
		};
		stackPanel.Children.Add(_progressBar);
		_doNotRemindCheckBox = CreateSwitchCheckBox("Больше не напоминать");
		stackPanel.Children.Add(_doNotRemindCheckBox);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		_downloadButton = AppDialog.CreateButton("Скачать", primary: true);
		_downloadButton.Click += async delegate
		{
			await DownloadAsync();
		};
		_laterButton = AppDialog.CreateButton("Позже");
		_laterButton.Click += delegate
		{
			CloseLater();
		};
		stackPanel2.Children.Add(_downloadButton);
		stackPanel2.Children.Add(_laterButton);
		stackPanel.Children.Add(stackPanel2);
		border.Child = stackPanel;
		_dialog.Content = border;
	}

	public void Show()
	{
		AppLogger.Info("Update popup shown.");
		_dialog.ShowDialog();
	}

	private async Task DownloadAsync()
	{
		SaveReminderChoice();
		_downloadButton.IsEnabled = false;
		_laterButton.IsEnabled = false;
		_progressBar.Visibility = Visibility.Visible;
		_statusText.Text = "Загрузка обновления...";
		Progress<UpdateDownloadProgress> progress = new Progress<UpdateDownloadProgress>(UpdateProgress);
		UpdateDownloadResult updateDownloadResult = await _updateService.DownloadAsync(_manifest, progress);
		if (!updateDownloadResult.Success)
		{
			_statusText.Text = updateDownloadResult.Message;
			_downloadButton.IsEnabled = true;
			_laterButton.IsEnabled = true;
			AppDialog.Show(_dialog, "Обновления", updateDownloadResult.Message, AppDialogKind.Warning);
		}
		else
		{
			_statusText.Text = "Обновление скачано";
			_progressBar.IsIndeterminate = false;
			_progressBar.Value = 100.0;
			UpdateService.OpenFolderForFile(updateDownloadResult.FilePath);
			AppLogger.Info("Explorer opened for downloaded update.");
			_dialog.Close();
		}
	}

	private void UpdateProgress(UpdateDownloadProgress progress)
	{
		if (progress.Percent.HasValue)
		{
			_progressBar.IsIndeterminate = false;
			_progressBar.Value = Math.Clamp(progress.Percent.Value, 0.0, 100.0);
		}
		else
		{
			_progressBar.IsIndeterminate = true;
		}
		_statusText.Text = progress.Status;
	}

	private void CloseLater()
	{
		SaveReminderChoice();
		_dialog.Close();
	}

	private void SaveReminderChoice()
	{
		if (_doNotRemindCheckBox.IsChecked == true)
		{
			_setRemindersEnabled(obj: false);
			AppLogger.Info("User disabled update reminders.");
		}
	}

	private static TextBlock CreateVersionText(string text)
	{
		return new TextBlock
		{
			Text = text,
			Foreground = AppDialog.TextBrush(),
			Margin = new Thickness(0.0, 2.0, 0.0, 2.0)
		};
	}

	private static TextBox CreateNotesBox(string notes)
	{
		return new TextBox
		{
			Text = (string.IsNullOrWhiteSpace(notes) ? "Описание изменений не указано." : notes.Replace("\\n", Environment.NewLine)),
			IsReadOnly = true,
			TextWrapping = TextWrapping.Wrap,
			AcceptsReturn = true,
			Height = 135.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Foreground = AppDialog.TextBrush(),
			Background = new SolidColorBrush(Color.FromRgb(17, 27, 42)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(49, 69, 95)),
			Padding = new Thickness(10.0),
			Resources = { 
			{
				(object)typeof(ScrollBar),
				(object)CreateDialogScrollBarStyle()
			} }
		};
	}

	private static ControlTemplate CreateProgressBarTemplate()
	{
		return (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n                 xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n                 TargetType=\"{x:Type ProgressBar}\">\n    <Border Background=\"{TemplateBinding Background}\"\n            BorderBrush=\"{TemplateBinding BorderBrush}\"\n            BorderThickness=\"1\"\n            CornerRadius=\"5\"\n            ClipToBounds=\"True\">\n        <Grid x:Name=\"PART_Track\">\n            <Rectangle x:Name=\"PART_Indicator\"\n                       HorizontalAlignment=\"Left\"\n                       Fill=\"{TemplateBinding Foreground}\"\n                       RadiusX=\"4\"\n                       RadiusY=\"4\" />\n        </Grid>\n    </Border>\n</ControlTemplate>");
	}

	private static CheckBox CreateSwitchCheckBox(string label)
	{
		return new CheckBox
		{
			Content = label,
			Foreground = AppDialog.TextBrush(),
			Margin = new Thickness(0.0, 10.0, 0.0, 8.0),
			Cursor = Cursors.Hand,
			Template = CreateSwitchTemplate()
		};
	}

	private static ControlTemplate CreateSwitchTemplate()
	{
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(DockPanel));
		frameworkElementFactory.SetValue(DockPanel.LastChildFillProperty, true);
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(ContentPresenter));
		frameworkElementFactory2.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory2.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
		FrameworkElementFactory frameworkElementFactory3 = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory3.Name = "SwitchTrack";
		frameworkElementFactory3.SetValue(DockPanel.DockProperty, Dock.Right);
		frameworkElementFactory3.SetValue(FrameworkElement.WidthProperty, 42.0);
		frameworkElementFactory3.SetValue(FrameworkElement.HeightProperty, 22.0);
		frameworkElementFactory3.SetValue(Border.CornerRadiusProperty, new CornerRadius(11.0));
		frameworkElementFactory3.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(27, 38, 51)));
		frameworkElementFactory3.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(37, 50, 68)));
		frameworkElementFactory3.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
		frameworkElementFactory3.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
		frameworkElementFactory3.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
		frameworkElementFactory3.SetValue(FrameworkElement.UseLayoutRoundingProperty, true);
		frameworkElementFactory3.SetValue(UIElement.ClipToBoundsProperty, true);
		FrameworkElementFactory frameworkElementFactory4 = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory4.Name = "SwitchThumb";
		frameworkElementFactory4.SetValue(FrameworkElement.WidthProperty, 18.0);
		frameworkElementFactory4.SetValue(FrameworkElement.HeightProperty, 18.0);
		frameworkElementFactory4.SetValue(FrameworkElement.MarginProperty, new Thickness(2.0, 0.0, 0.0, 0.0));
		frameworkElementFactory4.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
		frameworkElementFactory4.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory4.SetValue(Border.CornerRadiusProperty, new CornerRadius(9.0));
		frameworkElementFactory4.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(184, 196, 214)));
		frameworkElementFactory4.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
		frameworkElementFactory4.SetValue(FrameworkElement.UseLayoutRoundingProperty, true);
		frameworkElementFactory3.AppendChild(frameworkElementFactory4);
		frameworkElementFactory.AppendChild(frameworkElementFactory3);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		ControlTemplate obj = new ControlTemplate(typeof(CheckBox))
		{
			VisualTree = frameworkElementFactory
		};
		Trigger trigger = new Trigger
		{
			Property = ToggleButton.IsCheckedProperty,
			Value = true
		};
		trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(47, 140, byte.MaxValue)), "SwitchTrack"));
		trigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(47, 140, byte.MaxValue)), "SwitchTrack"));
		trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(242, 247, byte.MaxValue)), "SwitchThumb"));
		trigger.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(22.0, 0.0, 0.0, 0.0), "SwitchThumb"));
		trigger.EnterActions.Add(CreateSwitchAnimation(new Thickness(22.0, 0.0, 0.0, 0.0)));
		trigger.ExitActions.Add(CreateSwitchAnimation(new Thickness(2.0, 0.0, 0.0, 0.0)));
		obj.Triggers.Add(trigger);
		Trigger trigger2 = new Trigger
		{
			Property = UIElement.IsMouseOverProperty,
			Value = true
		};
		trigger2.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(58, 149, byte.MaxValue)), "SwitchTrack"));
		obj.Triggers.Add(trigger2);
		return obj;
	}

	private static BeginStoryboard CreateSwitchAnimation(Thickness to)
	{
		ThicknessAnimation thicknessAnimation = new ThicknessAnimation
		{
			To = to,
			Duration = TimeSpan.FromMilliseconds(140L),
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		Storyboard.SetTargetName(thicknessAnimation, "SwitchThumb");
		Storyboard.SetTargetProperty(thicknessAnimation, new PropertyPath(FrameworkElement.MarginProperty));
		Storyboard storyboard = new Storyboard();
		storyboard.Children.Add(thicknessAnimation);
		return new BeginStoryboard
		{
			Storyboard = storyboard
		};
	}

	private static Style CreateDialogScrollBarStyle()
	{
		return (Style)XamlReader.Parse("<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n       xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n       TargetType=\"{x:Type ScrollBar}\">\n    <Setter Property=\"Width\" Value=\"10\" />\n    <Setter Property=\"Background\" Value=\"Transparent\" />\n    <Setter Property=\"Template\">\n        <Setter.Value>\n            <ControlTemplate TargetType=\"{x:Type ScrollBar}\">\n                <Grid Background=\"Transparent\">\n                    <Track x:Name=\"PART_Track\" IsDirectionReversed=\"True\">\n                        <Track.DecreaseRepeatButton>\n                            <RepeatButton Command=\"ScrollBar.PageUpCommand\" Opacity=\"0\" />\n                        </Track.DecreaseRepeatButton>\n                        <Track.Thumb>\n                            <Thumb Background=\"#FF26384F\">\n                                <Thumb.Template>\n                                    <ControlTemplate TargetType=\"{x:Type Thumb}\">\n                                        <Border x:Name=\"ThumbRoot\"\n                                                Background=\"{TemplateBinding Background}\"\n                                                CornerRadius=\"5\"\n                                                Margin=\"2\" />\n                                        <ControlTemplate.Triggers>\n                                            <Trigger Property=\"IsMouseOver\" Value=\"True\">\n                                                <Setter TargetName=\"ThumbRoot\" Property=\"Background\" Value=\"#FF3A5678\" />\n                                            </Trigger>\n                                            <Trigger Property=\"IsDragging\" Value=\"True\">\n                                                <Setter TargetName=\"ThumbRoot\" Property=\"Background\" Value=\"#FF2F8CFF\" />\n                                            </Trigger>\n                                        </ControlTemplate.Triggers>\n                                    </ControlTemplate>\n                                </Thumb.Template>\n                            </Thumb>\n                        </Track.Thumb>\n                        <Track.IncreaseRepeatButton>\n                            <RepeatButton Command=\"ScrollBar.PageDownCommand\" Opacity=\"0\" />\n                        </Track.IncreaseRepeatButton>\n                    </Track>\n                </Grid>\n            </ControlTemplate>\n        </Setter.Value>\n    </Setter>\n</Style>");
	}
}
