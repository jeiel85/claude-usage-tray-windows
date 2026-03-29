using System.Text.RegularExpressions;
using System.Windows;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.Views;

public partial class UpdateDialog : Window
{
    private readonly Func<Action<int>, Task> _onUpdate;
    private readonly Action _onSkip;

    public UpdateDialog(string version, string releaseNotes, Func<Action<int>, Task> onUpdate, Action onSkip)
    {
        InitializeComponent();
        _onUpdate = onUpdate;
        _onSkip   = onSkip;

        // Localize UI strings
        DialogTitleLabel.Text = Loc.UpdateDialogTitle;
        WhatsNewLabel.Text    = Loc.WhatsNew;
        UpdateNowLabel.Text   = Loc.UpdateNow;
        SkipLabel.Text        = Loc.SkipThisVersion;
        LaterLabel.Text       = Loc.Later;

        VersionLabel.Text      = version;
        ReleaseNotesText.Text  = ExtractLocalizedNotes(releaseNotes);

        MouseLeftButtonDown += (_, e) => DragMove();
    }

    private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        UpdateBtn.IsEnabled = false;
        ButtonPanel.Visibility = Visibility.Collapsed;
        DownloadingLabel.Text  = Loc.DownloadingUpdate;
        ProgressPanel.Visibility = Visibility.Visible;

        await _onUpdate(pct => Dispatcher.Invoke(() =>
        {
            ProgressPctLabel.Text = $"{pct}%";
            ProgressFill.Width    = (ProgressFill.Parent as System.Windows.Controls.Border)!
                                    .ActualWidth * pct / 100;
        }));
    }

    private void SkipBtn_Click(object sender, RoutedEventArgs e)
    {
        _onSkip();
        Close();
    }

    private void LaterBtn_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Release body에서 현재 언어 블록(<!-- ko -->...<!-- /ko -->)을 추출.
    /// 현재 언어 블록이 없으면 영어 → 전체 텍스트 순으로 fallback.
    /// </summary>
    private static string ExtractLocalizedNotes(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var lang = Loc.CurrentLang;

        // Try current language, then English as fallback
        foreach (var code in new[] { lang, "en" })
        {
            var block = ExtractBlock(raw, code);
            if (!string.IsNullOrWhiteSpace(block))
                return SimplifyMarkdown(block);
        }

        // No language blocks found — show full text as-is (legacy format)
        return SimplifyMarkdown(raw);
    }

    private static string ExtractBlock(string text, string lang)
    {
        var pattern = $@"<!--\s*{lang}\s*-->(.*?)<!--\s*/{lang}\s*-->";
        var match = Regex.Match(text, pattern,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    // Minimal markdown → plain text for WPF TextBlock readability
    private static string SimplifyMarkdown(string md)
    {
        if (string.IsNullOrWhiteSpace(md)) return "";

        md = Regex.Replace(md, @"^#{1,3}\s+", "", RegexOptions.Multiline);  // ## headers
        md = Regex.Replace(md, @"\*\*(.+?)\*\*", "$1");                     // **bold**
        md = Regex.Replace(md, @"`(.+?)`", "$1");                           // `code`
        md = Regex.Replace(md, @"\r\n|\r", "\n");
        md = Regex.Replace(md, @"\n{3,}", "\n\n");

        return md.Trim();
    }
}
