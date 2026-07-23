using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.Views;

public partial class UpdateDialog : Window
{
    private readonly Action _onSkip;
    private readonly DispatcherTimer? _countdownTimer;
    private int _secondsRemaining;
    private bool _updateStarted;

    public event Action? OnUpdateRequested;

    /// <summary>
    /// 카운트다운 만료로 업데이트가 자동 시작됐는지. <see cref="OnUpdateRequested"/> 핸들러가
    /// 이 값을 읽어 "자동 시도한 버전"을 기록하고, 적용 실패 시 무한 재시도를 막는다.
    /// </summary>
    public bool StartedAutomatically { get; private set; }

    /// <param name="autoUpdateSeconds">
    /// 자동 적용까지의 대기 시간(초). 0 이하면 카운트다운 없이 수동 클릭만 받는다.
    /// 만료 전에 "지금 업데이트"/"이번 버전 건너뛰기"/닫기를 누르면 카운트다운은 취소된다.
    /// </param>
    public UpdateDialog(string version, string releaseNotes, Action onSkip, int autoUpdateSeconds = 0)
    {
        InitializeComponent();
        _onSkip = onSkip;

        // 카운트다운이 끝나면 사용자가 보고 있지 않아도 설치되므로, 어떤 버전인지 제목에 명시한다.
        TitleText.Text    = string.IsNullOrWhiteSpace(version)
            ? Loc.UpdateDialogTitle
            : $"{Loc.UpdateDialogTitle} · {version}";
        SkipBtn.Content   = Loc.SkipThisVersion;
        UpdateBtn.Content = Loc.UpdateNow;
        MinimizeBtn.ToolTip = Loc.MinimizeWindow;

        RenderMarkdown(releaseNotes);

        MouseLeftButtonDown += (s, e) => DragMove();

        // Topmost 를 쓰지 않으므로(다른 작업 위에 계속 떠 있지 않도록) 열릴 때 한 번만 앞으로 가져온다.
        Loaded += (_, _) => Activate();

        if (autoUpdateSeconds > 0)
        {
            _secondsRemaining = autoUpdateSeconds;
            CountdownText.Text = Loc.AutoUpdateCountdown(_secondsRemaining);
            CountdownText.Visibility = Visibility.Visible;

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += OnCountdownTick;
            _countdownTimer.Start();
        }

        // 창이 어떤 경로로 닫히든 타이머를 반드시 멈춘다 (닫힌 창에서 자동 적용되는 것을 방지).
        Closed += (_, _) => StopCountdown();
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        _secondsRemaining--;
        if (_secondsRemaining > 0)
        {
            CountdownText.Text = Loc.AutoUpdateCountdown(_secondsRemaining);
            return;
        }

        StartedAutomatically = true;
        BeginUpdate();
    }

    private void StopCountdown()
    {
        if (_countdownTimer is null) return;
        _countdownTimer.Stop();
        _countdownTimer.Tick -= OnCountdownTick;
        CountdownText.Visibility = Visibility.Collapsed;
    }

    /// <summary>다운로드 시작 — 수동 클릭과 카운트다운 만료가 공유하는 단일 진입점.</summary>
    private void BeginUpdate()
    {
        if (_updateStarted) return;
        _updateStarted = true;

        StopCountdown();
        ActionPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        OnUpdateRequested?.Invoke();
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
            // 실패했으니 버튼을 다시 열어 재시도할 수 있게 한다. 단 카운트다운은 되살리지 않는다 —
            // 실패한 자동 적용이 계속 재시도되면 다운로드 루프가 된다.
            _updateStarted = false;
            ActionPanel.Visibility = Visibility.Visible;
            ProgressPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        });
    }

    private void Update_Click(object sender, RoutedEventArgs e) => BeginUpdate();

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        _onSkip?.Invoke();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        Close();
    }

    // 최소화해도 카운트다운은 계속 흐른다 — 잠깐 치워 두는 것이지 자동 적용을 취소하는 조작이 아니다.
    // 자동 적용을 원치 않으면 닫기(✕) 또는 "이번 버전 건너뛰기"를 쓴다.
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
}
