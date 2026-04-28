using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

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
        RenderMarkdown(releaseNotes);

        MouseLeftButtonDown += (s, e) => DragMove();
    }

    private void RenderMarkdown(string md)
    {
        if (string.IsNullOrWhiteSpace(md)) return;

        var doc = new FlowDocument();
        doc.PagePadding = new Thickness(0);
        
        var lines = md.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // H2 or H3 Headers
            if (line.StartsWith("### "))
            {
                var p = new Paragraph(new Run(line.Substring(4)))
                {
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA7, 0x8B, 0xFA)), // AccentLight
                    Margin = new Thickness(0, 10, 0, 5)
                };
                doc.Blocks.Add(p);
            }
            else if (line.StartsWith("## "))
            {
                var p = new Paragraph(new Run(line.Substring(3)))
                {
                    FontSize = 17,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 15, 0, 8)
                };
                doc.Blocks.Add(p);
            }
            // List Items
            else if (line.StartsWith("* ") || line.StartsWith("- "))
            {
                var p = new Paragraph { Margin = new Thickness(10, 0, 0, 4) };
                p.Inlines.Add(new Run("• ") { Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x5C, 0xF6)) }); // Accent
                ParseInlines(p, line.Substring(2));
                doc.Blocks.Add(p);
            }
            // Regular Paragraph
            else
            {
                var p = new Paragraph();
                ParseInlines(p, line);
                doc.Blocks.Add(p);
            }
        }

        NotesRichText.Document = doc;
    }

    private void ParseInlines(Paragraph p, string text)
    {
        // Simple inline parser for **bold** and `code`
        var parts = Regex.Split(text, @"(\*\*.*?\*\*|`.*?`)").Where(s => !string.IsNullOrEmpty(s));
        foreach (var part in parts)
        {
            if (part.StartsWith("**") && part.EndsWith("**"))
            {
                p.Inlines.Add(new Bold(new Run(part.Substring(2, part.Length - 4))));
            }
            else if (part.StartsWith("`") && part.EndsWith("`"))
            {
                var r = new Run(part.Substring(1, part.Length - 2))
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2F, 0x45)), // BorderBrush
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6E, 0xE7, 0xB7)), // Success/Light Green
                    FontFamily = new System.Windows.Media.FontFamily("Consolas, Lucida Console, Courier New")
                };
                p.Inlines.Add(r);
            }
            else
            {
                p.Inlines.Add(new Run(part));
            }
        }
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
}
