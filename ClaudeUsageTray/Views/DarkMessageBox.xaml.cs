using System.Windows;

namespace ClaudeUsageTray.Views;

public enum DarkMessageBoxResult
{
    None,
    Confirm,
    Cancel
}

public partial class DarkMessageBox : Window
{
    public DarkMessageBoxResult Result { get; private set; } = DarkMessageBoxResult.None;

    private DarkMessageBox(string title, string message, string confirmText, string? cancelText = null)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmBtn.Content = confirmText;

        if (cancelText != null)
        {
            CancelBtn.Content = cancelText;
            CancelBtn.Visibility = Visibility.Visible;
        }
        else
        {
            CancelBtn.Visibility = Visibility.Collapsed;
        }

        MouseLeftButtonDown += (s, e) => DragMove();
        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) Close();
        };
    }

    public static DarkMessageBoxResult Show(
        string title, string message,
        string confirmText, string? cancelText = null)
    {
        var dialog = new DarkMessageBox(title, message, confirmText, cancelText);
        dialog.ShowDialog();
        return dialog.Result;
    }

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
    {
        Result = DarkMessageBoxResult.Confirm;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Result = DarkMessageBoxResult.Cancel;
        DialogResult = false;
        Close();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Result = DarkMessageBoxResult.Cancel;
        DialogResult = false;
        Close();
    }
}
