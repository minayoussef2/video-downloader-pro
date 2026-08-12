using System.Windows;

namespace VideoDownloaderPro.Views;

public partial class ErrorDetailsWindow : Window
{
    public ErrorDetailsWindow(string errorText)
    {
        InitializeComponent();
        TxtError.Text = string.IsNullOrWhiteSpace(errorText) ? "No detailed error log recorded." : errorText;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(TxtError.Text);
            System.Windows.MessageBox.Show("Error details copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
