using System.Text.RegularExpressions;
using System.Windows;

namespace ClaudeUsageTray.Views;

public partial class UpdateDialog : Window
{
    private readonly Action _onSkip;
    public event Action? OnUpdateRequested;

    public UpdateDialog(string version, string releaseNotes, Action onSkip)
    {
        InitializeComponent();
        _onSkip = onSkip;

        VersionLabel.Text = version;
        NotesText.Text = FormatReleaseNotes(releaseNotes);

        MouseLeftButtonDown += (s, e) => DragMove();
    }

    public void UpdateProgress(int percent, string status)
    {
        Dispatcher.Invoke(() =>
        {
            ActionPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressBar.Value = percent;
            PercentText.Text = $"{percent}%";
            StatusText.Text = status;
        });
    }

    public void ShowError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            ActionPanel.Visibility = Visibility.Visible;
            ProgressPanel.Visibility = Visibility.Collapsed;
            System.Windows.MessageBox.Show(message, "업데이트 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        });
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        ActionPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        OnUpdateRequested?.Invoke();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _onSkip?.Invoke();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private string FormatReleaseNotes(string md)
    {
        if (string.IsNullOrWhiteSpace(md)) return "";

        // Simple markdown to plain text conversion
        md = Regex.Replace(md, @"^### (.+)$", "$1", RegexOptions.Multiline); // H3
        md = Regex.Replace(md, @"^## (.+)$", "$1", RegexOptions.Multiline);  // H2
        md = Regex.Replace(md, @"^\* (.+)$", "• $1", RegexOptions.Multiline); // Bullet
        md = Regex.Replace(md, @"^- (.+)$", "• $1", RegexOptions.Multiline);  // Bullet
        md = Regex.Replace(md, @"`(.+?)`", "$1");                           // `code`
        md = Regex.Replace(md, @"\r\n|\r", "\n");
        md = Regex.Replace(md, @"\n{3,}", "\n\n");

        return md.Trim();
    }
}
