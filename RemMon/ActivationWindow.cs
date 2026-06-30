using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace RemMon;

internal partial class ActivationWindow : Window, IComponentConnector
{
	private readonly LicenseService _licenseService;

	private bool _isActivating;

	public ActivationWindow(LicenseService licenseService)
	{
		InitializeComponent();
		_licenseService = licenseService;
		base.Loaded += delegate
		{
			LicenseKeyBox.Focus();
		};
	}

	private void Shell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private void LicenseKeyBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			e.Handled = true;
			ActivateAsync();
		}
	}

	private void ActivateButton_Click(object sender, RoutedEventArgs e)
	{
		ActivateAsync();
	}

	private void ExitButton_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
		Close();
	}

	private async Task ActivateAsync()
	{
		if (_isActivating)
		{
			return;
		}
		_isActivating = true;
		ActivateButton.IsEnabled = false;
		LicenseKeyBox.IsEnabled = false;
		SetStatus("Активация...", isError: false);
		try
		{
			LicenseOperationResult licenseOperationResult = await _licenseService.ActivateAsync(LicenseKeyBox.Text);
			SetStatus(licenseOperationResult.Message, !licenseOperationResult.Success);
			if (licenseOperationResult.Success)
			{
				await Task.Delay(350);
				base.DialogResult = true;
				Close();
			}
		}
		finally
		{
			_isActivating = false;
			ActivateButton.IsEnabled = true;
			LicenseKeyBox.IsEnabled = true;
		}
	}

	private void SetStatus(string text, bool isError)
	{
		StatusText.Text = text;
		StatusText.Foreground = new SolidColorBrush(isError ? Color.FromRgb(byte.MaxValue, 166, 77) : Color.FromRgb(184, 196, 212));
	}
}
