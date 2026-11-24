using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.WeatherWarnings;
using ClaudeUsageTray.Tests.ViewModels;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Views;
using Xunit;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ClaudeUsageTray.Tests.Views;

// v1.56.0(#224)에서 창 라벨(10.5→11.5)과 초기화 문구(10→11) 글씨를 키우자, 오른쪽 "퍼센트 · 초기화" 묶음이 넓어져
// 라벨 열(*)이 0폭으로 눌렸고 "7일 윈도우" 가 "25%" 위로 넘쳐 그려졌다(#234).
// 실제 팝업을 화면 밖에 띄워, 가장 긴 초기화 문구(절대 시각·추정 표기)에서도 라벨·퍼센트·초기화 문구가
// 겹치지 않고 잘리지 않는지 4개 언어로 본다. Loc 언어를 바꾸므로 WpfTests 컬렉션에서 직렬로 돈다.
[Collection("WpfTests")]
[Trait("Category", "Integration")]
public class WindowRowLayoutTests : IDisposable
{
    private readonly string _dir;

    public WindowRowLayoutTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cut-window-row-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // MainViewModelIntegrationTests.CreateViewModel 과 같은 격리 구성 — 개발자 PC 의 설정·자격 파일을 건드리지 않는다.
    private MainViewModel CreateViewModel()
    {
        if (Application.Current == null)
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var credService = new CredentialService(Path.Combine(_dir, ".credentials.json"));
        var notifier = new NotificationService(() => null);
        var weather = new WeatherService();
        return new MainViewModel(new UsageApiService(credService), credService, new SessionMonitor(),
            new CodexUsageMonitor(), new OpenCodeUsageMonitor(), new AntigravityUsageMonitor(), notifier,
            new SettingsService(Path.Combine(_dir, "claude-usage-tray.json")), new UpdateService(),
            new HistoryService(_dir), new UsageSyncService(Path.Combine(_dir, "appdata")), weather,
            new WeatherAlertService(weather, notifier, () => new NotificationSettings(), Array.Empty<IWeatherWarningProvider>()));
    }

    public static TheoryData<string, bool> Cases()
    {
        var data = new TheoryData<string, bool>();
        foreach (var lang in new[] { "ko", "zh", "ja", "en" })
        {
            data.Add(lang, false);   // 보통: 상대 시간 + 절대 시각
            data.Add(lang, true);    // 가장 긴 경우: 추정 표기까지 붙음
        }
        return data;
    }

    // 자정 직전이라 초기화 시각이 다음 날로 넘어가 "MM/dd HH:mm" 의 긴 절대 시각이 붙는다.
    private static DateTimeOffset LateNight => new(DateTime.Today.AddHours(23).AddMinutes(50));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ClaudeWindowRows_LabelPercentAndResetDoNotOverlap(string lang, bool estimated)
    {
        var originalLang = Loc.CurrentLang;
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                Loc.SetLanguage(lang);
                vm.HideInactiveProviders = false;
                vm.FocusedProvider = UsageProviderKind.Claude;
                var c = vm.ClaudeVm;
                c.IsActive = true;
                c.HasQuotaData = true;
                c.ShortPercent = 1.0;   // "100%" — 퍼센트가 가장 넓은 경우
                c.LongPercent = 1.0;
                var now = LateNight;
                c.ShortReset = UsageCalculator.FormatResetLabel(now.AddHours(4).AddMinutes(59), estimated, true, now);
                c.LongReset = UsageCalculator.FormatResetLabel(now.AddDays(6).AddHours(23), false, true, now);

                using var popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                AssertRowFits(popup, "ClaudeShort", requireSingleLine: false);
                AssertRowFits(popup, "ClaudeLong", requireSingleLine: false);
                popup.Hide();
            }
            finally
            {
                Loc.SetLanguage(originalLang);
                vm.Dispose();
            }
        });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CodexWindowRows_LabelPercentAndResetDoNotOverlap(string lang, bool estimated)
    {
        var originalLang = Loc.CurrentLang;
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                Loc.SetLanguage(lang);
                vm.HideInactiveProviders = false;
                vm.IsCodexActive = true;
                vm.FocusedProvider = UsageProviderKind.Codex;
                vm.CodexShortWindowLabel = Loc.CodexWindowLabel(300);
                vm.CodexLongWindowLabel = Loc.CodexWindowLabel(10080);
                vm.CodexPercent = 1.0;
                vm.CodexLongPercent = 1.0;
                vm.IsCodexLongVisible = true;
                var now = LateNight;
                vm.CodexReset = UsageCalculator.FormatResetLabel(now.AddHours(4).AddMinutes(59), estimated, true, now);
                vm.CodexLongReset = UsageCalculator.FormatResetLabel(now.AddDays(6).AddHours(23), false, true, now);

                using var popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                AssertRowFits(popup, "CodexShort", requireSingleLine: false);
                AssertRowFits(popup, "CodexLong", requireSingleLine: false);
                popup.Hide();
            }
            finally
            {
                Loc.SetLanguage(originalLang);
                vm.Dispose();
            }
        });
    }

    // #234 신고 화면 그대로(한국어, 10%·25%, 절대 시각 표시): 두 줄 모두 한 줄에 겹침 없이 들어가야 한다.
    [Fact]
    public async Task ClaudeWindowRows_ReportedKoreanCase_FitOnOneLine()
    {
        var originalLang = Loc.CurrentLang;
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                Loc.SetLanguage("ko");
                vm.HideInactiveProviders = false;
                vm.FocusedProvider = UsageProviderKind.Claude;
                var c = vm.ClaudeVm;
                c.IsActive = true;
                c.HasQuotaData = true;
                c.ShortPercent = 0.10;
                c.LongPercent = 0.25;
                var now = new DateTimeOffset(DateTime.Today.AddHours(23).AddMinutes(4));
                c.ShortReset = UsageCalculator.FormatResetLabel(now.AddMinutes(4).AddSeconds(1), false, true, now);
                c.LongReset = UsageCalculator.FormatResetLabel(now.AddDays(4).AddHours(22), false, true, now);

                using var popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                AssertRowFits(popup, "ClaudeShort", requireSingleLine: true);
                AssertRowFits(popup, "ClaudeLong", requireSingleLine: true);
                popup.Hide();
            }
            finally
            {
                Loc.SetLanguage(originalLang);
                vm.Dispose();
            }
        });
    }

    /// <summary>
    /// 한 줄(라벨 | 퍼센트 · 초기화)에서 셋이 겹치지 않고, 라벨·퍼센트는 잘리지 않으며, 초기화 문구는 줄 안에 다 들어가는지 본다.
    /// 잘리지 않았다 = 배치된 폭이 제약 없이 잰 폭 이상이다. 초기화 문구는 폭이 모자라면(일·영 라벨, 추정 표기 등)
    /// 오른쪽 정렬로 줄바꿈하도록 했으므로 <paramref name="requireSingleLine"/> 일 때만 한 줄 폭을 요구한다.
    /// </summary>
    private static void AssertRowFits(Window popup, string prefix, bool requireSingleLine) =>
        AssertRowFits(prefix,
            Assert.IsType<TextBlock>(popup.FindName(prefix + "Label")),
            Assert.IsType<TextBlock>(popup.FindName(prefix + "Percent")),
            Assert.IsType<TextBlock>(popup.FindName(prefix + "Reset")),
            requireSingleLine);

    private static void AssertRowFits(string prefix, TextBlock label, TextBlock percent, TextBlock reset, bool requireSingleLine)
    {
        Assert.True(label.IsVisible, $"{prefix} label not visible");
        Assert.False(string.IsNullOrEmpty(reset.Text), $"{prefix} reset text empty");
        Assert.Equal(TextTrimming.None, reset.TextTrimming);   // 말줄임으로 시각을 숨기지 않는다

        var row = (FrameworkElement)label.Parent;
        double Left(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), row).X;
        double Right(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth, 0), row).X;
        double Bottom(FrameworkElement e) => e.TranslatePoint(new Point(0, e.ActualHeight), row).Y;

        var mustBeWhole = requireSingleLine ? new[] { label, percent, reset } : new[] { label, percent };
        foreach (var tb in mustBeWhole)
        {
            var need = UnconstrainedWidth(tb);
            Assert.True(tb.ActualWidth + 0.5 >= need,
                $"{prefix} '{tb.Text}' squeezed: {tb.ActualWidth:F1} < {need:F1}");
        }

        Assert.True(Right(label) <= Left(percent) + 0.5,
            $"{prefix} label '{label.Text}' overlaps percent: {Right(label):F1} > {Left(percent):F1}");
        Assert.True(Right(percent) <= Left(reset) + 0.5,
            $"{prefix} percent overlaps reset: {Right(percent):F1} > {Left(reset):F1}");
        Assert.True(Right(reset) <= row.ActualWidth + 0.5,
            $"{prefix} reset '{reset.Text}' runs past the row: {Right(reset):F1} > {row.ActualWidth:F1}");
        // 줄바꿈된 경우에도 줄(행) 안에 다 들어가 아래 게이지를 덮지 않는다.
        Assert.True(Bottom(reset) <= row.ActualHeight + 0.5,
            $"{prefix} reset spills below the row: {Bottom(reset):F1} > {row.ActualHeight:F1}");
    }

    // OpenCode Go 공식 할당량 행(#237). 예전에는 라벨이 * 열이라 #234 와 같은 이유로 긴 초기화 문구에 눌릴 수 있었다.
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OpenCodeQuotaRows_LabelPercentAndResetDoNotOverlap(string lang, bool estimated)
    {
        var originalLang = Loc.CurrentLang;
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                Loc.SetLanguage(lang);
                vm.HideInactiveProviders = false;
                vm.IsOpenCodeActive = true;
                vm.FocusedProvider = UsageProviderKind.OpenCode;
                var now = LateNight;
                vm.OpenCodeVm.ApplySyncedWebUsage(new OpenCodeWebUsage
                {
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                    Rolling = new OpenCodeQuotaWindow { UsagePercent = 1, ResetAt = now.AddHours(4).AddMinutes(59) },
                    Weekly = new OpenCodeQuotaWindow { UsagePercent = 1, ResetAt = now.AddDays(6).AddHours(23) },
                    Monthly = new OpenCodeQuotaWindow { UsagePercent = 1, ResetAt = now.AddDays(29).AddHours(23) },
                });
                // 가장 긴 문구(절대 시각 + 추정 표기)로 덮어쓴다.
                var o = vm.OpenCodeVm;
                o.RollingResetLabel = UsageCalculator.FormatResetLabel(now.AddHours(4).AddMinutes(59), estimated, true, now);
                o.WeeklyResetLabel = UsageCalculator.FormatResetLabel(now.AddDays(6).AddHours(23), estimated, true, now);
                o.MonthlyResetLabel = UsageCalculator.FormatResetLabel(now.AddDays(29).AddHours(23), estimated, true, now);

                using var popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                AssertRowFits(popup, "OpenCodeRolling", requireSingleLine: false);
                AssertRowFits(popup, "OpenCodeWeekly", requireSingleLine: false);
                AssertRowFits(popup, "OpenCodeMonthly", requireSingleLine: false);
                popup.Hide();
            }
            finally
            {
                Loc.SetLanguage(originalLang);
                vm.Dispose();
            }
        });
    }

    // DeepSeek(100milionseek) 섹션의 주 게이지(#316). 템플릿 안의 행이라 FindName 대신 시각 트리에서 찾는다.
    // 막대도 다른 게이지와 같은 높이(스타일 값 4)인지 본다 — v1.70.1 은 Height="8" 로 덮어써 두 배 두꺼웠다.
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DeepSeekGaugeRow_LabelPercentAndResetDoNotOverlap(string lang, bool estimated)
    {
        _ = estimated; // 일일 창은 추정 표기가 없다 — 같은 언어 목록을 쓰려고 Cases 를 그대로 받는다.
        var originalLang = Loc.CurrentLang;
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                Loc.SetLanguage(lang);
                vm.HideInactiveProviders = false;
                vm.FocusedProvider = MainViewModel.HmsFocusKey;
                vm.ShowAbsoluteResetTime = true;
                var now = LateNight;
                vm.HmsVm.Apply(new HmsReadResult(HmsReadState.Ok,
                    new HmsDailyUsage(400_000_000, 400_000_000, 0, 9_999, now.AddHours(9).AddMinutes(59), now.AddDays(1)), "k"),
                    now);

                using var popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                AssertRowFits("HmsGauge",
                    FindNamed<TextBlock>(popup, "HmsGaugeLabel"),
                    FindNamed<TextBlock>(popup, "HmsGaugePercent"),
                    FindNamed<TextBlock>(popup, "HmsGaugeReset"),
                    requireSingleLine: false);

                var list = Assert.IsType<ItemsControl>(popup.FindName("HmsGaugeList"));
                var bar = FindDescendants<System.Windows.Controls.ProgressBar>(list).Single();
                var codexBar = FindDescendants<System.Windows.Controls.ProgressBar>(popup)
                    .First(b => b.Style == popup.FindResource("CodexBarStyle"));
                Assert.Equal(codexBar.Height, bar.Height);
                popup.Hide();
            }
            finally
            {
                Loc.SetLanguage(originalLang);
                vm.Dispose();
            }
        });
    }

    // Antigravity 게이지 행(#345): 주 게이지 규칙(Auto 짧은 창 이름 + * DockPanel)과 미사용·크레딧 조건·하단 출처가
    // 4개 언어 + 절대 시각 표시에서도 겹치거나 잘리지 않는지 본다.
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task AntigravityGaugeRows_LabelPercentAndResetDoNotOverlap(string lang, bool estimated)
    {
        _ = estimated;
        var originalLang = Loc.CurrentLang;
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                Loc.SetLanguage(lang);
                vm.HideInactiveProviders = false;
                vm.IsAntigravityEnabled = true;
                vm.ShowAntigravityThirdPartyQuota = true;
                vm.ShowAbsoluteResetTime = true;
                vm.FocusedProvider = UsageProviderKind.Antigravity;

                var now = LateNight;
                vm.AntigravityVm.ApplyQuota(
                [
                    new AntigravityModelQuota
                    {
                        ModelId = "gemini-5h", TokenType = "5h", GroupName = "Gemini Models",
                        GroupDescription = "Models within this group: Gemini 4 Neon, Gemini 4 Carbon, Gemini 4 Argon, Gemini 4 Hydrogen, Gemini 3.8 Flash",
                        RemainingFraction = 0.0, ResetTime = now.AddHours(4).AddMinutes(59),
                    },
                    new AntigravityModelQuota
                    {
                        ModelId = "gemini-weekly", TokenType = "weekly", GroupName = "Gemini Models",
                        GroupDescription = "Models within this group: Gemini 4 Neon, Gemini 4 Carbon, Gemini 4 Argon, Gemini 4 Hydrogen, Gemini 3.8 Flash",
                        RemainingFraction = 0.94, ResetTime = now.AddDays(6).AddHours(23),
                    },
                    new AntigravityModelQuota
                    {
                        ModelId = "3p-5h", TokenType = "5h", GroupName = "Claude and GPT models",
                        GroupDescription = "Models within this group: Claude Opus 5.5, Claude Sonnet 5.5, GPT-OSS 120B",
                        RemainingFraction = 1.0, ResetTime = null,
                    },
                    new AntigravityModelQuota
                    {
                        ModelId = "gemini-argon-wtu-weekly", TokenType = "weekly", GroupName = "Gemini 4 Argon",
                        DisplayName = "Gemini 4 Argon Weekly Limit", RemainingFraction = 0.29, ResetTime = now.AddDays(6).AddHours(23),
                    },
                ], "Antigravity", "Google AI Pro", aiCredits: 17, aiCreditsMinimum: 50);

                vm.AntigravityHasData = true;
                vm.AntigravityPaidTierName = vm.AntigravityVm.PaidTierName;
                vm.AntigravityAiCreditsText = vm.AntigravityVm.AiCreditsText;
                vm.AntigravityAiCreditsNotice = vm.AntigravityVm.AiCreditsNotice;
                vm.AntigravityModels = vm.AntigravityVm.Models;
                vm.AntigravityPercent = vm.AntigravityVm.Percent;
                vm.AntigravityNote = Loc.ProviderAntigravityNote;
                vm.AntigravityDataSource = Loc.UsageSyncQuotaFromDevice("DESKTOP-HOME", "04/12 23:50");

                using var popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                var labels = FindDescendants<TextBlock>(popup).Where(t => t.Name == "AntigravityGaugeLabel").ToList();
                var percents = FindDescendants<TextBlock>(popup).Where(t => t.Name == "AntigravityGaugePercent").ToList();
                var resets = FindDescendants<TextBlock>(popup).Where(t => t.Name == "AntigravityGaugeReset").ToList();
                Assert.Equal(4, labels.Count);
                Assert.Equal(4, percents.Count);
                Assert.Equal(4, resets.Count);

                for (var i = 0; i < labels.Count; i++)
                {
                    AssertRowFits($"Antigravity[{i}]", labels[i], percents[i], resets[i], requireSingleLine: false);
                }

                var list = Assert.IsType<ItemsControl>(popup.FindName("AntigravityGaugeList"));
                var bars = FindDescendants<System.Windows.Controls.ProgressBar>(list).ToList();
                var codexBar = FindDescendants<System.Windows.Controls.ProgressBar>(popup)
                    .First(b => b.Style == popup.FindResource("CodexBarStyle"));
                Assert.Equal(4, bars.Count);
                Assert.All(bars, b => Assert.Equal(codexBar.Height, b.Height));

                var note = Assert.IsType<TextBlock>(popup.FindName("AntigravityNoteText"));
                var source = Assert.IsType<TextBlock>(popup.FindName("AntigravityDataSourceText"));
                var footer = (FrameworkElement)note.Parent;
                var noteBottom = note.TranslatePoint(new Point(0, note.ActualHeight), footer).Y;
                var sourceTop = source.TranslatePoint(new Point(0, 0), footer).Y;
                Assert.True(noteBottom <= sourceTop + 0.5, $"Antigravity note overlaps source: {noteBottom:F1} > {sourceTop:F1}");

                popup.Hide();
            }
            finally
            {
                Loc.SetLanguage(originalLang);
                vm.Dispose();
            }
        });
    }

    private static T FindNamed<T>(DependencyObject root, string name) where T : FrameworkElement =>
        FindDescendants<T>(root).Single(e => e.Name == name);

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }

    private static double UnconstrainedWidth(TextBlock source)
    {
        var probe = new TextBlock
        {
            Text = source.Text,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontWeight = source.FontWeight,
            FontStyle = source.FontStyle,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }
}
