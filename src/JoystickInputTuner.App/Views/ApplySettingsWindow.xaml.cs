using System.Windows;

namespace JoystickInputTuner.App.Views;

public partial class ApplySettingsWindow : Window
{
    public ApplySettingsWindow(string suggestedProfileName)
    {
        InitializeComponent();
        ProfileNameTextBox.Text = string.IsNullOrWhiteSpace(suggestedProfileName) ? "Default" : suggestedProfileName.Trim();
        ProfileNameTextBox.SelectAll();
        ProfileNameTextBox.Focus();
    }

    public string EnteredProfileName => ProfileNameTextBox.Text.Trim();

    public bool StartWithWindows => WindowsStartupRadioButton.IsChecked == true;

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EnteredProfileName))
        {
            ProfileNameTextBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
