using System.Diagnostics;
using System.Text.Json;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Views;
using Timer = System.Timers.Timer;

namespace ClaudeUsageTray.ViewModels;

    public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly UsageApiService _api;
    private readonly CredentialService _credentials;
    private readonly SessionMonitor _session;
    private readonly CodexUsageMonitor _codex;
    private readonly GeminiCliUsageMonitor _geminiCli;
    private readonly OpenCodeUsageMonitor _openCode;
    private readonly AntigravityUsageMonitor _antigravity;
    private readonly NotificationService _notifier;
    private readonly SettingsService _settingsService;
    private readonly UpdateService _updater;
    private readonly HistoryService _history;
    private readonly WeatherService _weather;
    private readonly WeatherAlertService _weatherAlert;
    private readonly Timer _timer;
    private readonly Timer _countdownTimer;
    private readonly Timer _updateTimer;
    private int _secondsUntilRefresh = 0;

    public AntigravityViewModel AntigravityVm { get; }
    public WeatherViewModel WeatherVm { get; }
    public OpenCodeViewModel OpenCodeVm { get; }
    public GeminiViewModel GeminiVm { get; }
    public CodexViewModel CodexVm { get; }
    public ClaudeViewModel ClaudeVm { get; }

    // Tracks previous 5h usage to detect threshold crossings
    private double _prevShortPercent = -1;
    private double _prevCodexPercent = -1;
    private double _prevGeminiPercent = -1;
    private double _prevExtraPercent = -1;
    private bool _prevHadRateLimit = false;

    // Tracks early exhaustion detection per reset cycle
    private string _prevShortDepletion = "";
    private DateTimeOffset? _lastNotifiedEarlyDepletionAt;

    // Last known good API data (kept when rate-limited so UI doesn't reset to 0)
    private double _lastKnownShortPercent = 0;
    private double _lastKnownLongPercent = 0;
    private string _lastKnownShortReset = "";
    private string _lastKnownLongReset = "";

    // Rate limit backoff — skip API calls until this time
    private DateTimeOffset _apiRetryAfter = DateTimeOffset.MinValue;
    private DateTimeOffset _weatherLastRefresh = DateTimeOffset.MinValue;

    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private string _nextRefreshLabel = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _lastUpdatedLabel = "";
    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";

    // Claude usage → ClaudeVm (ShortPercent, LongPercent, HasError, etc.)

    // Codex Usage
    [ObservableProperty] private double _codexPercent = 0;
    [ObservableProperty] private string _codexReset = "";
    [ObservableProperty] private string _codexDataSource = "";
    [ObservableProperty] private bool _codexHasError = false;
    [ObservableProperty] private string _codexErrorMessage = "";
    [ObservableProperty] private string _codexNote = Loc.ProviderCodexNote;
    [ObservableProperty] private string _codexSummary = "";
    // v1.26.0: 보조(secondary) 윈도우 — Anthropic 의 7d 와 같은 위치
    [ObservableProperty] private double _codexLongPercent = 0;
    [ObservableProperty] private string _codexLongReset = "";
    [ObservableProperty] private string _codexLongSummary = "";
    [ObservableProperty] private bool _isCodexLongVisible = false;   // secondary 응답이 있을 때만 노출
    // v1.26.0: PlanType 라벨 — 응답에 PlanType 있으면 "ChatGPT Plus" 식으로, 없으면 "ChatGPT plan"
    [ObservableProperty] private string _codexPlanLabel = "ChatGPT plan";
    // 오늘의 토큰 4타일 (Input / Output / CacheRead / CacheWrite — Codex는 cache write 개념 없어 "—")
    [ObservableProperty] private string _codexInputLabel = "—";
    [ObservableProperty] private string _codexOutputLabel = "—";
    [ObservableProperty] private string _codexCacheReadLabel = "—";
    [ObservableProperty] private string _codexCacheWriteLabel = "—";

    // Gemini Usage
    [ObservableProperty] private double _geminiPercent = 0;
    [ObservableProperty] private string _geminiReset = "";
    [ObservableProperty] private bool _geminiHasError = false;
    [ObservableProperty] private string _geminiErrorMessage = "";
    [ObservableProperty] private string _geminiNote = Loc.ProviderGeminiCliNote;
    [ObservableProperty] private string _geminiSummary = "";
    [ObservableProperty] private string _geminiRequestsLabel = "";
    [ObservableProperty] private string _geminiOutputTokensLabel = "";
    // 오늘의 토큰 4타일 (Input / Output / CacheRead / CacheWrite — Gemini는 cache write 없어 "—")
    [ObservableProperty] private string _geminiInputLabel = "—";
    [ObservableProperty] private string _geminiCacheReadLabel = "—";
    [ObservableProperty] private string _geminiCacheWriteLabel = "—";

    // OpenCode Usage
    [ObservableProperty] private bool _openCodeHasError = false;
    [ObservableProperty] private string _openCodeErrorMessage = "";
    [ObservableProperty] private string _openCodeNote = Loc.ProviderOpenCodeNote;
    [ObservableProperty] private string _openCodeSummary = "";
    [ObservableProperty] private string _openCodeInputLabel = "";
    [ObservableProperty] private string _openCodeOutputLabel = "";
    [ObservableProperty] private string _openCodeRequestCountLabel = "";
    // 오늘의 토큰 4타일 보강 — input/output 이미 있고 cache read/write 추가
    [ObservableProperty] private string _openCodeCacheReadLabel = "—";
    [ObservableProperty] private string _openCodeCacheWriteLabel = "—";

    // Antigravity Usage (v1.31.0) — per-model quota panel from Antigravity (Gemini Code Assist) IDE
    [ObservableProperty] private bool _antigravityHasData = false;
    [ObservableProperty] private bool _antigravityHasError = false;
    [ObservableProperty] private string _antigravityErrorMessage = "";
    [ObservableProperty] private string _antigravityTierName = "";       // e.g. "Gemini Code Assist"
    [ObservableProperty] private string _antigravityPaidTierName = "";   // e.g. "Gemini Code Assist in Google One AI Pro"
    [ObservableProperty] private System.Collections.Generic.IReadOnlyList<AntigravityModelRow> _antigravityModels =
        System.Array.Empty<AntigravityModelRow>();
    [ObservableProperty] private bool _isAntigravityEnabled = true;
    [ObservableProperty] private double _antigravityPercent = 0.0;

    // Weather (v1.29.0)
    [ObservableProperty] private bool _weatherEnabled;
    [ObservableProperty] private bool _weatherShowInTrayTooltip = true;
    [ObservableProperty] private string _weatherLocationMode = "manual";
    [ObservableProperty] private string _weatherLocationName = "";
    [ObservableProperty] private string _weatherCountryCode = "";
    [ObservableProperty] private double? _weatherLatitude;
    [ObservableProperty] private double? _weatherLongitude;
    [ObservableProperty] private string _weatherTimezone = "auto";
    [ObservableProperty] private int _weatherRefreshIntervalMinutes = 30;
    [ObservableProperty] private bool _weatherDailyForecastEnabled = true;
    [ObservableProperty] private string _weatherDailyForecastTime = "07:30";
    [ObservableProperty] private bool _weatherConditionAlertsEnabled = true;
    [ObservableProperty] private int _weatherRainProbabilityThreshold = 70;
    [ObservableProperty] private double _weatherHighTemperatureThresholdC = 33;
    [ObservableProperty] private double _weatherLowTemperatureThresholdC = -10;
    [ObservableProperty] private double _weatherWindSpeedThresholdKmh = 50;
    [ObservableProperty] private bool _weatherOfficialAlertsEnabled = true;
    [ObservableProperty] private string _weatherStatusLabel = "";
    [ObservableProperty] private string _weatherTooltipLabel = "";
    [ObservableProperty] private bool _weatherHasError;
    [ObservableProperty] private string _weatherErrorMessage = "";
    [ObservableProperty] private string _weatherTemperatureLabel = "";
    [ObservableProperty] private string _weatherConditionLabel = "";
    [ObservableProperty] private string _weatherIcon = "•";

    public bool WeatherHasLocation => WeatherEnabled
        && WeatherLatitude.HasValue && WeatherLongitude.HasValue
        && !string.IsNullOrEmpty(WeatherLocationName);
    public bool WeatherHasCurrent => WeatherHasLocation
        && !string.IsNullOrEmpty(WeatherTemperatureLabel);
    public string WeatherPopupLabel => WeatherHasLocation && !string.IsNullOrEmpty(WeatherTooltipLabel)
        ? $"📍 {WeatherTooltipLabel}" : "";
    public string WeatherShortLocation
    {
        get
        {
            if (string.IsNullOrEmpty(WeatherLocationName)) return "";

            // Single-segment names (e.g. "Seoul", or the new structured reverse-geocode
            // result) — show as-is.
            var parts = WeatherLocationName
                .Split(',')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();
            if (parts.Length <= 1) return parts.Length == 1 ? parts[0] : WeatherLocationName;

            // Migration path for users whose location was saved as the full
            // Nominatim display_name before v1.29.5 (e.g.
            // "헬로소프트 ..., 294-7, 하안로, 하안3동, 광명시, 경기도, ..., 대한민국").
            // Walk parts looking for a city-like administrative suffix.
            string[] citySuffixes = ["시", "구", "군", "City", "Town", "市", "区"];
            foreach (var p in parts)
            {
                foreach (var suf in citySuffixes)
                {
                    if (p.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }

            // Fallback: first non-numeric, non-postal segment.
            foreach (var p in parts)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(p, @"^[\d\s\-]+$"))
                    return p;
            }

            return parts[0];
        }
    }
    public string LblWeatherSection => Loc.WeatherForecastTitle;

    // 5h window (Legacy/Compatibility - will keep for now to avoid breaking other parts)
    [ObservableProperty] private double _shortUsagePercent = 0;
    [ObservableProperty] private string _shortResetLabel = "";

    // 7d window (Legacy/Compatibility)
    [ObservableProperty] private double _longUsagePercent = 0;
    [ObservableProperty] private string _longResetLabel = "";

    // Per-model usage (7d)
    [ObservableProperty] private double _opusPercent = 0;
    [ObservableProperty] private double _sonnetPercent = 0;
    [ObservableProperty] private long _opusTokens = 0;
    [ObservableProperty] private long _sonnetTokens = 0;

    // Today's local session stats
    [ObservableProperty] private long _todayInputTokens = 0;
    [ObservableProperty] private long _todayOutputTokens = 0;
    [ObservableProperty] private long _todayCacheRead = 0;
    [ObservableProperty] private long _todayCacheWrite = 0;
    [ObservableProperty] private string _sessionsLabel = "";
    [ObservableProperty] private bool _hasRateLimitHit = false;
    [ObservableProperty] private string _rateLimitInfo = "";

    // Language
    [ObservableProperty] private string _selectedLanguage = "system";

    // Notification settings
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _notifyRateLimit;
    [ObservableProperty] private bool _notifyOnQuotaReset;
    [ObservableProperty] private bool _threshold50;
    [ObservableProperty] private bool _threshold75;
    [ObservableProperty] private bool _threshold90;
    [ObservableProperty] private bool _threshold100;
    [ObservableProperty] private string _ntfyTopic = "";
    [ObservableProperty] private bool _ntfySendFromThisPc = true;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private int _pollingIntervalMinutes;

    // v1.27.0 표시 옵션 토글
    [ObservableProperty] private bool _showCodexPlanBadge = true;
    [ObservableProperty] private bool _showAbsoluteResetTime = false;

    // 절대 시각 토글 시 재포맷 위해 raw DateTimeOffset 보관 — API 재호출 없이 즉시 라벨 갱신
    private DateTimeOffset? _rawClaudeShortResetAt;
    private DateTimeOffset? _rawClaudeLongResetAt;
    private DateTimeOffset? _rawCodexShortResetAt;
    private bool _rawCodexShortResetEstimated;
    private DateTimeOffset? _rawCodexLongResetAt;

    // History
    [ObservableProperty] private IReadOnlyList<DailyStats> _historyData = [];

    // 오늘 시간대별 토큰 (0~23시)
    [ObservableProperty] private long[] _hourlyTokens = new long[24];

    public string HistoryChartTitle => Loc.HistoryTitleFor("Claude");

    // 5시간 소진 예측
    [ObservableProperty] private string _shortDepletionLabel = "";

    // 7일 소진 예측
    [ObservableProperty] private string _longDepletionLabel = "";

    // 오늘 추정 비용 (API 기준 참고값)
    [ObservableProperty] private string _todayCostLabel = "";

    // Extra usage (purchased add-on)
    [ObservableProperty] private bool _extraUsageEnabled = false;
    [ObservableProperty] private bool _extraHasLimit = false;
    [ObservableProperty] private double _extraUsagePercent = 0;
    [ObservableProperty] private string _extraCreditsLabel = "";
    [ObservableProperty] private bool _isExtraOnlyMode = false; // 기본 사용량 100% 소진 후 추가 사용량만 표시

    public string LblExtraUsage => Loc.ExtraUsageTitle;

    private string? _lastKnownOrgUuid;

    // Update banner
    [ObservableProperty] private bool _updateAvailable = false;
    [ObservableProperty] private string _updateLabel = "";
    [ObservableProperty] private string _updateCheckLabel = "";
    [ObservableProperty] private bool _isUpdating = false;
    [ObservableProperty] private int _updateProgress = 0;
    [ObservableProperty] private string _updateStatusText = "";
    [ObservableProperty] private string _selectedProvider = UsageProviderKind.Claude;
    [ObservableProperty] private string _providerNote = "";
    [ObservableProperty] private string _trayDisplayMode = UsageProviderKind.Auto;
    [ObservableProperty] private string _effectiveTrayProvider = UsageProviderKind.Claude;
    [ObservableProperty] private bool _hideInactiveProviders = true;
    
    // Manual provider visibility toggles
    [ObservableProperty] private bool _isClaudeEnabled = true;
    [ObservableProperty] private bool _isCodexEnabled = true;
    [ObservableProperty] private bool _isGeminiEnabled = true;
    [ObservableProperty] private bool _isOpenCodeEnabled = true;

    // Popup에서 한 번에 한 공급자만 상세 펼침 — 나머지는 컴팩트 행 (v1.25.0 신규)
    // 빈 문자열은 "자동 결정" 의미 — LoadSettings/EnsureValidFocusedProvider 가 즉시 채움
    [ObservableProperty] private string _focusedProvider = UsageProviderKind.Claude;
    [ObservableProperty] private bool _isClaudeFocused = true;
    [ObservableProperty] private bool _isCodexFocused = false;
    [ObservableProperty] private bool _isGeminiFocused = false;
    [ObservableProperty] private bool _isOpenCodeFocused = false;
    [ObservableProperty] private bool _isAntigravityFocused = false;

    [ObservableProperty] private double _openCodePercent = 0;
    [ObservableProperty] private double _trayUsagePercent = 0;

    // Visibility control (IsClaudeActive → ClaudeVm.IsActive)
    [ObservableProperty] private bool _isCodexActive = false;
    [ObservableProperty] private bool _isGeminiActive = false;
    [ObservableProperty] private bool _isOpenCodeActive = false;
    // IsClaudeUsageEmpty → ClaudeVm.IsUsageEmpty
    [ObservableProperty] private bool _isCodexUsageEmpty = true;
    [ObservableProperty] private bool _isGeminiUsageEmpty = true;
    [ObservableProperty] private bool _isOpenCodeUsageEmpty = true;

    // IsClaudeSubscribed → ClaudeVm.IsSubscribed
    private string _updateDownloadUrl = "";
    private string _updateSha256Url = "";
    private string _updateVersion = "";
    private string _updateReleaseNotes = "";
    private bool _isUpdateDialogOpen = false;
    public string CurrentVersionLabel => $"v{UpdateService.CurrentVersion.ToString(3)}";

    public string? RawApiResponse { get; private set; }

    // Localized static labels
    public string LblAppTitle        => Loc.AgentUsageTitle;
    public string LblApiQuota        => Loc.ApiQuota;
    public string LblTodayTokens     => Loc.TodayTokens;
    public string LblFiveHour        => Loc.FiveHourWindow;
    public string LblSevenDay        => Loc.SevenDayWindow;
    public string LblShortWindow     => Loc.ShortWindow;
    public string LblLongWindow      => Loc.LongWindow;
    public string LblInput           => Loc.Input;
    public string LblOutput          => Loc.Output;
    public string LblCacheRead       => Loc.CacheRead;
    public string LblCacheWrite      => Loc.CacheWrite;
    public string LblTokens          => Loc.Tokens;
    public string LblHistory         => Loc.HistoryTitle;
    public string LblRefresh         => Loc.Refresh;
    public string LblQuit            => Loc.Quit;
    public string LblRefreshing      => Loc.Refreshing;
    public string LblNotifications   => Loc.Notifications;
    public string LblNotiEnabled     => Loc.NotificationsEnabled;
    public string LblNotiRateLimit   => Loc.NotifyRateLimit;
    public string LblThresholds      => Loc.ThresholdsLabel;
    public string LblNtfyTopic       => Loc.NtfyTopic;
    public string LblNtfyPlaceholder => Loc.NtfyPlaceholder;

    // ntfy 발송 대상 토픽: 이 PC에서 발송 비활성화 시 빈 문자열 반환
    private string NtfyTopicEffective => NtfySendFromThisPc ? NtfyTopic : "";
    public string LblGeminiRequests   => Loc.GeminiRequests;
    public string LblExtraCredits    => Loc.ExtraCreditsLabel;
    public string LblCheckUpdate     => Loc.CheckUpdate;
    public string LblClaudeNoUsage   => Loc.ClaudeNoUsageToday;
    public string LblCodexNoUsage    => Loc.CodexNoUsageToday;
    public string LblGeminiNoUsage   => Loc.GeminiCliNoUsageToday;
    public string LblOpenCodeNoUsage => Loc.OpenCodeNoUsageToday;
    public string LblVisibleProviders => Loc.VisibleProviders;
    public string DisclaimerText     => SelectedProvider == UsageProviderKind.Claude ? Loc.Disclaimer : Loc.GenericDisclaimer;

    // Tooltips
    public string TipInput      => Loc.InputTooltip;
    public string TipOutput     => Loc.OutputTooltip;
    public string TipCacheRead  => Loc.CacheReadTooltip;
    public string TipCacheWrite => Loc.CacheWriteTooltip;

    public MainViewModel(UsageApiService api, CredentialService credentials,
                         SessionMonitor session, CodexUsageMonitor codex, GeminiCliUsageMonitor geminiCli,
                         OpenCodeUsageMonitor openCode, AntigravityUsageMonitor antigravity,
                         NotificationService notifier, SettingsService settingsService,
                         UpdateService updater, HistoryService history,
                         WeatherService weather, WeatherAlertService weatherAlert)
    {
        _api = api;
        _credentials = credentials;
        _session = session;
        _codex = codex;
        _geminiCli = geminiCli;
        _openCode = openCode;
        _antigravity = antigravity;
        _notifier = notifier;
        _settingsService = settingsService;
        _updater = updater;
        _history = history;
        _weather = weather;
        _weatherAlert = weatherAlert;

        AntigravityVm = new AntigravityViewModel(antigravity);
        WeatherVm = new WeatherViewModel(weather, weatherAlert);
        OpenCodeVm = new OpenCodeViewModel(openCode, history);
        GeminiVm = new GeminiViewModel(geminiCli, history);
        CodexVm = new CodexViewModel(codex, history);
        ClaudeVm = new ClaudeViewModel(api, credentials, session, history);

        // 계정 전환 자동 감지: credentials 파일 변경 → 새로고침
        _credentials.CredentialsChanged += OnCredentialsChanged;

        // 언어 변경 시 모든 바인딩 갱신
        Loc.LanguageChanged += () =>
            System.Windows.Application.Current.Dispatcher.Invoke(() => OnPropertyChanged(string.Empty));

        // _timer 초기화는 LoadSettings 이전에 필요 (ApplyPollingInterval에서 사용)
        _timer = new Timer(AppConstants.PollingIntervalMs); // 2 minutes — API has rate limits
        _timer.Elapsed += async (_, _) => await RefreshAsync();
        _timer.AutoReset = true;

        LoadSettings();

        _countdownTimer = new Timer(1_000);
        _countdownTimer.Elapsed += (_, _) =>
        {
            if (_secondsUntilRefresh > 0)
                _secondsUntilRefresh--;
            var s = _secondsUntilRefresh;
            var label = s >= 60 ? $"{s / 60}:{s % 60:D2}" : $"{s}s";
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                NextRefreshLabel = label;
                // 10분 미만일 때 리셋 라벨도 1초마다 업데이트
                UpdateResetLabelsIfNeeded();
            });
        };
        _countdownTimer.AutoReset = true;

        _updateTimer = new Timer(AppConstants.UpdateCheckIntervalMs); // 24 hours
        _updateTimer.Elapsed += async (_, _) => await CheckForUpdateAsync();
        _updateTimer.AutoReset = true;
    }

    private void OnCredentialsChanged()
    {
        // 구독 상태 재확인 (credentials 변경 시 subscriptionType 갱신)
        UpdateClaudeSubscription();
        
        if (SelectedProvider != UsageProviderKind.Claude) return;
        // 계정 전환 감지 — 히스토리를 새 계정으로 전환하고 즉시 새로고침
        var orgUuid = _credentials.GetOrganizationUuid();
        _history.SetScope(UsageProviderKind.Claude, orgUuid);
        // 계정 전환 시 rate-limit 대기를 초기화 — 새 계정은 독립적으로 조회
        _apiRetryAfter = DateTimeOffset.MinValue;
        // 타이머 카운트다운 리셋 + 즉시 새로고침
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            await RefreshAsync());
    }

    private void LoadSettings()
    {
        var s = _settingsService.Load();
        SelectedProvider = UsageProviderKind.IsValid(s.SelectedProvider) ? s.SelectedProvider : UsageProviderKind.Claude;
        SelectedLanguage = s.Language ?? "system";
        Loc.SetLanguage(SelectedLanguage);
        NotificationsEnabled = s.Enabled;
        NotifyRateLimit = s.NotifyOnRateLimit;
        NotifyOnQuotaReset = s.NotifyOnQuotaReset;
        Threshold50  = s.Thresholds.Contains(50);
        Threshold75  = s.Thresholds.Contains(75);
        Threshold90  = s.Thresholds.Contains(90);
        Threshold100 = s.Thresholds.Contains(100);
        NtfyTopic           = s.NtfyTopic;
        NtfySendFromThisPc  = s.NtfySendFromThisPc;
        StartWithWindows    = s.StartWithWindows;
        PollingIntervalMinutes = s.PollingIntervalMinutes;
        TrayDisplayMode = UsageProviderKind.IsValid(s.TrayDisplayMode) ? s.TrayDisplayMode : UsageProviderKind.Auto;
        HideInactiveProviders = s.HideInactiveProviders;

        // Manual provider visibility
        IsClaudeEnabled   = s.VisibleProviders.Contains(UsageProviderKind.Claude);
        IsCodexEnabled    = s.VisibleProviders.Contains(UsageProviderKind.Codex);
        IsGeminiEnabled   = s.VisibleProviders.Contains(UsageProviderKind.GeminiCli);
        IsOpenCodeEnabled = s.VisibleProviders.Contains(UsageProviderKind.OpenCode);

        // Popup focused provider — 빈 값/유효하지 않으면 자동 결정
        FocusedProvider = string.IsNullOrEmpty(s.FocusedProvider) ? UsageProviderKind.Claude : s.FocusedProvider;
        EnsureValidFocusedProvider();

        // v1.27.0 표시 옵션
        ShowCodexPlanBadge   = s.ShowCodexPlanBadge;
        ShowAbsoluteResetTime = s.ShowAbsoluteResetTime;

        // 현재 로그인된 계정의 orgUuid로 히스토리 경로 초기화
        ApplySelectedProviderScope();

        // Apply polling interval
        ApplyPollingInterval();

        // Weather settings
        WeatherEnabled                = s.WeatherEnabled;
        WeatherShowInTrayTooltip      = s.WeatherShowInTrayTooltip;
        WeatherLocationMode           = s.WeatherLocationMode;
        WeatherLocationName            = s.WeatherLocationName;
        WeatherCountryCode            = s.WeatherCountryCode;
        WeatherLatitude               = s.WeatherLatitude;
        WeatherLongitude              = s.WeatherLongitude;
        WeatherTimezone               = s.WeatherTimezone;
        WeatherRefreshIntervalMinutes = s.WeatherRefreshIntervalMinutes;
        WeatherDailyForecastEnabled   = s.WeatherDailyForecastEnabled;
        WeatherDailyForecastTime      = s.WeatherDailyForecastTime;
        WeatherConditionAlertsEnabled = s.WeatherConditionAlertsEnabled;
        WeatherRainProbabilityThreshold   = s.WeatherRainProbabilityThreshold;
        WeatherHighTemperatureThresholdC  = s.WeatherHighTemperatureThresholdC;
        WeatherLowTemperatureThresholdC   = s.WeatherLowTemperatureThresholdC;
        WeatherWindSpeedThresholdKmh      = s.WeatherWindSpeedThresholdKmh;
        WeatherOfficialAlertsEnabled      = s.WeatherOfficialAlertsEnabled;

        // Check Claude subscription status from stored credentials
        UpdateClaudeSubscription();
    }

    partial void OnSelectedProviderChanged(string value)
    {
        ApplySelectedProviderScope();

        ProviderNote = value switch
        {
            UsageProviderKind.Codex     => Loc.ProviderCodexNote,
            UsageProviderKind.GeminiCli => Loc.ProviderGeminiCliNote,
            UsageProviderKind.OpenCode  => Loc.ProviderOpenCodeNote,
            _ => ""
        };
    }

    /// <summary>
    /// FocusedProvider 변경 시 IsXxxFocused 부울 4개를 동기화.
    /// (UsagePopup.xaml 의 Visibility 바인딩이 이 부울들을 본다)
    /// </summary>
    partial void OnFocusedProviderChanged(string value)
    {
        IsClaudeFocused   = value == UsageProviderKind.Claude;
        IsCodexFocused    = value == UsageProviderKind.Codex;
        IsGeminiFocused   = value == UsageProviderKind.GeminiCli;
        IsOpenCodeFocused = value == UsageProviderKind.OpenCode;
        IsAntigravityFocused = value == UsageProviderKind.Antigravity;
    }

    /// <summary>
    /// FocusedProvider 가 비어있거나 비활성화/숨김 공급자를 가리키면 안전한 값으로 폴백.
    /// 우선순위: Claude → Codex → Gemini → OpenCode (활성된 것 중 첫 번째).
    /// </summary>
    private void EnsureValidFocusedProvider()
    {
        bool IsEnabledFor(string p) => p switch
        {
            UsageProviderKind.Claude    => IsClaudeEnabled,
            UsageProviderKind.Codex     => IsCodexEnabled,
            UsageProviderKind.GeminiCli => IsGeminiEnabled,
            UsageProviderKind.OpenCode  => IsOpenCodeEnabled,
            UsageProviderKind.Antigravity => IsAntigravityEnabled,
            _ => false
        };

        // 빈 문자열은 "모두 접힘" 상태로 허용
        if (string.IsNullOrEmpty(FocusedProvider))
            return;

        if (UsageProviderKind.IsValid(FocusedProvider) &&
            IsEnabledFor(FocusedProvider))
        {
            return; // 현재 값 유효
        }

        // 폴백 — 유효하지 않은 공급자거나 비활성된 공급자를 가리킬 때만
        if (IsClaudeEnabled)        FocusedProvider = UsageProviderKind.Claude;
        else if (IsCodexEnabled)    FocusedProvider = UsageProviderKind.Codex;
        else if (IsGeminiEnabled)   FocusedProvider = UsageProviderKind.GeminiCli;
        else if (IsOpenCodeEnabled) FocusedProvider = UsageProviderKind.OpenCode;
        else if (IsAntigravityEnabled) FocusedProvider = UsageProviderKind.Antigravity;
        else                        FocusedProvider = UsageProviderKind.Claude; // 모두 비활성 시 최후 폴백
    }

    private void ApplySelectedProviderScope()
    {
        switch (SelectedProvider)
        {
            case UsageProviderKind.Codex:
                _history.SetScope(UsageProviderKind.Codex, null);
                break;
            case UsageProviderKind.GeminiCli:
                _history.SetScope(UsageProviderKind.GeminiCli, null);
                break;
            case UsageProviderKind.OpenCode:
                _history.SetScope(UsageProviderKind.OpenCode, null);
                break;
            default:
                _history.SetScope(UsageProviderKind.Claude, _credentials.GetOrganizationUuid());
                break;
        }
    }

    /// <summary>
    /// Apply the polling interval from settings or default.
    /// </summary>
    public void ApplyPollingInterval()
    {
        var interval = PollingIntervalMinutes > 0 
            ? PollingIntervalMinutes * 60_000 
            : AppConstants.PollingIntervalMs;
        _timer.Interval = interval;
    }

    /// <summary>
    /// Reads subscriptionType from local credentials and determines if Claude is a paid plan.
    /// Paid plans (pro, max, team, etc.) should always show the Claude section even at 0% usage.
    /// </summary>
    private void UpdateClaudeSubscription()
    {
        var subType = _credentials.GetSubscriptionType();
        ClaudeVm.IsSubscribed = !string.IsNullOrEmpty(subType)
            && !string.Equals(subType, "free", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public void SaveSettings()
    {
        var thresholds = new List<int>();
        if (Threshold50)  thresholds.Add(50);
        if (Threshold75)  thresholds.Add(75);
        if (Threshold90)  thresholds.Add(90);
        if (Threshold100) thresholds.Add(100);

        var visibleProviders = new List<string>();
        if (IsClaudeEnabled)   visibleProviders.Add(UsageProviderKind.Claude);
        if (IsCodexEnabled)    visibleProviders.Add(UsageProviderKind.Codex);
        if (IsGeminiEnabled)   visibleProviders.Add(UsageProviderKind.GeminiCli);
        if (IsOpenCodeEnabled) visibleProviders.Add(UsageProviderKind.OpenCode);

        // Preserve SkippedVersion from disk
        var existing = _settingsService.Load();

        _settingsService.Save(new NotificationSettings
        {
            SelectedProvider = SelectedProvider,
            Language = SelectedLanguage,
            Enabled = NotificationsEnabled,
            NotifyOnRateLimit = NotifyRateLimit,
            NotifyOnQuotaReset = NotifyOnQuotaReset,
            Thresholds = thresholds,
            NtfyTopic = NtfyTopic.Trim(),
            NtfySendFromThisPc = NtfySendFromThisPc,
            StartWithWindows = StartWithWindows,
            SkippedVersion = existing.SkippedVersion,
            PollingIntervalMinutes = PollingIntervalMinutes,
            TrayDisplayMode = TrayDisplayMode,
            HideInactiveProviders = HideInactiveProviders,
            VisibleProviders = visibleProviders,
            FocusedProvider = FocusedProvider,
            ShowCodexPlanBadge = ShowCodexPlanBadge,
            ShowAbsoluteResetTime = ShowAbsoluteResetTime,
            // 추적 필드 보존 — settings 저장 시 매번 잃지 않도록
            OAuthNotAllowedFirstSeenUtc = existing.OAuthNotAllowedFirstSeenUtc,

            // Weather settings
            WeatherEnabled                = WeatherEnabled,
            WeatherShowInTrayTooltip      = WeatherShowInTrayTooltip,
            WeatherLocationMode           = WeatherLocationMode,
            WeatherLocationName            = WeatherLocationName,
            WeatherCountryCode            = WeatherCountryCode,
            WeatherLatitude               = WeatherLatitude,
            WeatherLongitude              = WeatherLongitude,
            WeatherTimezone               = WeatherTimezone,
            WeatherRefreshIntervalMinutes = WeatherRefreshIntervalMinutes,
            WeatherDailyForecastEnabled   = WeatherDailyForecastEnabled,
            WeatherDailyForecastTime      = WeatherDailyForecastTime,
            WeatherConditionAlertsEnabled = WeatherConditionAlertsEnabled,
            WeatherRainProbabilityThreshold   = WeatherRainProbabilityThreshold,
            WeatherHighTemperatureThresholdC  = WeatherHighTemperatureThresholdC,
            WeatherLowTemperatureThresholdC   = WeatherLowTemperatureThresholdC,
            WeatherWindSpeedThresholdKmh      = WeatherWindSpeedThresholdKmh,
            WeatherOfficialAlertsEnabled      = WeatherOfficialAlertsEnabled,
        });
    }

    public async Task StartAsync()
    {
        await RefreshAsync();
        _timer.Start();
        _countdownTimer.Start();
        _updateTimer.Start();
        _ = CheckForUpdateAsync();
    }

    [RelayCommand]
    public async Task ManualCheckForUpdateAsync()
    {
        if (IsUpdating) return;
        if (UpdateAvailable && TryShowCachedUpdateDialog()) return;

        UpdateCheckLabel = Loc.CheckingUpdate;

        UpdateService.UpdateInfo? result;
        try
        {
            result = await _updater.CheckForUpdateAsync();
        }
        catch (UpdateCheckException uex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                UpdateCheckLabel = uex.Kind switch
                {
                    UpdateCheckErrorKind.RateLimit => Loc.UpdateCheckRateLimited(uex.RetryAtLocal ?? ""),
                    UpdateCheckErrorKind.Network   => Loc.UpdateCheckNetworkError,
                    UpdateCheckErrorKind.Timeout   => Loc.UpdateCheckTimeout,
                    UpdateCheckErrorKind.ApiError  => Loc.UpdateCheckApiError(uex.StatusCode ?? 0),
                    _ => Loc.UpdateCheckFailed
                };

                if (uex.Kind is UpdateCheckErrorKind.RateLimit
                               or UpdateCheckErrorKind.Timeout
                               or UpdateCheckErrorKind.ApiError)
                {
                    var choice = DarkMessageBox.Show(
                        Loc.UpdateCheckFailed,
                        Loc.UpdateCheckApiErrorDialogPrompt,
                        Loc.OpenReleasesPage,
                        Loc.Later);

                    if (choice == DarkMessageBoxResult.Confirm)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                            UpdateService.ReleasePage) { UseShellExecute = true });
                        UpdateCheckLabel = "";
                    }
                }
                else
                {
                    await Task.Delay(5000);
                    UpdateCheckLabel = "";
                }
            });
            return;
        }
        catch (Exception ex)
        {
            // 분류되지 않은 예외 — 메시지 일부라도 노출
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var detail = ex.Message;
                if (detail.Length > 80) detail = detail[..80] + "…";
                UpdateCheckLabel = $"{Loc.UpdateCheckFailed}: {detail}";
                await Task.Delay(5000);
                UpdateCheckLabel = "";
            });
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (result is null)
            {
                UpdateCheckLabel = Loc.AlreadyUpToDate;
                await Task.Delay(3000);
                UpdateCheckLabel = "";
                return;
            }

            var info = result;
            var versionStr = StoreAvailableUpdate(info);
            UpdateCheckLabel = "";

            var settings = _settingsService.Load();
            if (settings.SkippedVersion == versionStr)
            {
                settings.SkippedVersion = "";
                _settingsService.Save(settings);
            }

            UpdateLabel = Loc.UpdateAvailable($"v{versionStr}");
            UpdateAvailable = true;

            ShowUpdateDialog(versionStr, _updateReleaseNotes);
        });
    }

    private async Task CheckForUpdateAsync()
    {
        if (IsUpdating) return;

        UpdateService.UpdateInfo? result;
        try
        {
            result = await _updater.CheckForUpdateAsync();
        }
        catch
        {
            return;
        }
        if (result is null) return;

        var info = result;
        var versionStr = StoreAvailableUpdate(info);

        var settings = _settingsService.Load();
        if (settings.SkippedVersion == versionStr) return;

        // 배너도 자동 모달도 띄우지 않는다 (v1.33.8): 캐시만 갱신해 두고, 사용자가 좌하단 버전을
        // 클릭하면 ManualCheckForUpdateAsync 의 캐시 경로가 즉시 모달을 연다. 백그라운드 체크가
        // 갑자기 UI 를 가로채지 않도록 한다.
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateLabel = Loc.UpdateAvailable($"v{versionStr}");
            UpdateAvailable = true;
        });
    }

    private string StoreAvailableUpdate(UpdateService.UpdateInfo info)
    {
        var versionStr = info.version.ToString(3);
        _updateVersion = versionStr;
        _updateReleaseNotes = info.releaseNotes;
        _updateDownloadUrl = info.downloadUrl;
        _updateSha256Url = info.sha256Url;
        return versionStr;
    }

    private bool TryShowCachedUpdateDialog()
    {
        if (string.IsNullOrWhiteSpace(_updateVersion) || string.IsNullOrWhiteSpace(_updateDownloadUrl))
            return false;

        ShowUpdateDialog(_updateVersion, _updateReleaseNotes);
        return true;
    }

    private void ShowUpdateDialog(string version, string notes)
    {
        if (_isUpdateDialogOpen) return;

        try
        {
            _isUpdateDialogOpen = true;
            var dialog = new Views.UpdateDialog(
                $"v{version}",
                notes,
                onSkip: () => SkipVersion(version));

            dialog.OnUpdateRequested += () =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        IsUpdating = true;
                        var tempPath = await _updater.DownloadAndPrepareUpdateAsync(
                            _updateDownloadUrl, _updateSha256Url,
                            (pc, status) =>
                            {
                                dialog.UpdateProgress(pc, status);
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    UpdateProgress = pc;
                                    UpdateStatusText = status;
                                });
                            });

                        dialog.UpdateProgress(100, "Restarting...");
                        await Task.Delay(500);
                        _updater.ApplyPreparedUpdate(tempPath);
                    }
                    catch (Exception ex)
                    {
                        IsUpdating = false;
                        dialog.ShowError(ex.Message);
                    }
                });
            };

            // Owner 를 지정하지 않는다 (v1.33.8 회귀 수정):
            // UsagePopup 은 Topmost + Deactivated 시 스스로 Hide 되는데, 이 모달을 팝업의
            // Owner 로 지정하면 팝업이 숨는 순간 "소유된" 모달까지 함께 숨겨진다. 그러면 모달은
            // 보이지 않는데 ShowDialog() 는 계속 블로킹되어 _isUpdateDialogOpen 이 영구 고착되고,
            // 이후 버전 클릭이 전부 무반응이 된다. UpdateDialog 는 Topmost=True + CenterScreen 이라
            // Owner 없이도 항상 화면 최상단 중앙에 독립적으로 표시된다.
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            // 모달 표시 실패 — 조용히 삼키지 말고 버전 옆에 원인을 노출한다.
            System.Diagnostics.Debug.WriteLine($"[UpdateDialog] show error: {ex.Message}");
            var detail = ex.Message.Length > 80 ? ex.Message[..80] + "…" : ex.Message;
            UpdateCheckLabel = $"{Loc.UpdateCheckFailed}: {detail}";
        }
        finally
        {
            _isUpdateDialogOpen = false;
        }
    }

    [RelayCommand]
    public void StartUpdate()
    {
        if (IsUpdating) return;
        if (TryShowCachedUpdateDialog()) return;

        _ = ManualCheckForUpdateAsync();
    }

    public void SkipVersion(string version)
    {
        var settings = _settingsService.Load();
        settings.SkippedVersion = version;
        _settingsService.Save(settings);
        UpdateAvailable = false;
    }

    [RelayCommand]
    public void ExportCsv()
    {
        var filePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"claude-usage-{DateTime.Now:yyyyMMdd}.csv");
        _history.ExportCsv(filePath);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    public async Task<NotificationTestResult> SendTestNotificationAsync()
    {
        return await _notifier.ShowTestAlertAsync(NtfyTopicEffective);
    }

    public async Task<string?> SendTestWeatherNotificationAsync()
    {
        if (!WeatherLatitude.HasValue || !WeatherLongitude.HasValue)
            return Loc.TestWeatherNoLocation;

        var location = new Models.WeatherLocation(
            WeatherLocationName, WeatherCountryCode, null,
            WeatherLatitude.Value, WeatherLongitude.Value, WeatherTimezone);

        WeatherReport report;
        try
        {
            report = await _weather.GetForecastAsync(location);
        }
        catch
        {
            return Loc.TestWeatherNoData;
        }

        if (report.Current == null)
            return Loc.TestWeatherNoData;

        var loc = report.Current.Location;
        var condLabel = GetWeatherConditionLabel(report.Current.ConditionKey);
        var ntfyTopic = NtfySendFromThisPc ? NtfyTopic : "";

        var title = Loc.WeatherForecastTitle;
        var ntfyBody = $"{loc.Name}: {condLabel}";
        if (report.Daily.Count > 0)
        {
            var today = report.Daily[0];
            if (today.MinTemperatureC.HasValue && today.MaxTemperatureC.HasValue)
                ntfyBody += $"\n{Loc.WeatherDailyTemp(today.MinTemperatureC.Value, today.MaxTemperatureC.Value)}";
        }
        ntfyBody += $"\n{Loc.WeatherCurrentTemp(report.Current.TemperatureC)}";
        if (report.Current.ApparentTemperatureC.HasValue)
            ntfyBody += $" ({Loc.WeatherFeelsLike(report.Current.ApparentTemperatureC.Value)})";
        if (report.Daily.Count > 0 && report.Daily[0].PrecipitationProbabilityMax is int precip)
            ntfyBody += $"\n{Loc.WeatherRainProbability(precip)}";

        var clickUrl = WeatherAlertService.BuildWeatherClickUrlPublic(loc);

        _notifier.ShowWeatherAlert(title, ntfyBody, ntfyTopic,
            ntfyBody, tags: ["sunny"], clickUrl: clickUrl);

        return null;
    }

    [RelayCommand]
    public void ApplyUpdate()
    {
        _ = ManualCheckForUpdateAsync();
    }

    public async Task RefreshAsync()
    {
        _secondsUntilRefresh = PollingIntervalMinutes > 0 ? PollingIntervalMinutes * 60 : 120;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);

        try
        {
            // 병렬로 모든 공급자 데이터 갱신
            var tasks = new List<Task>
            {
                RefreshClaudeAsync(),
                RefreshCodexInternalAsync(),
                RefreshGeminiCliInternalAsync(),
                RefreshOpenCodeInternalAsync(),
                RefreshAntigravityInternalAsync()
            };

            if (WeatherEnabled)
                tasks.Add(RefreshWeatherInternalAsync());

            await Task.WhenAll(tasks);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 공통 정보 업데이트
                UpdateOverallStatus();
                
                LastUpdatedLabel = (ClaudeVm.HasError || CodexHasError || GeminiHasError || OpenCodeHasError)
                    ? $"⚠ {DateTime.Now:HH:mm:ss}"
                    : Loc.UpdatedAt(DateTime.Now.ToString("HH:mm:ss"));
                IsLoading = false;
            });
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasError = true;
                ErrorMessage = ex.Message;
                StatusText = "Error";
                IsLoading = false;
            });
        }
    }

    private int _lastGeminiRequestCount = 0;
    private long _lastGeminiOutputTokens = 0;

    private int _lastOpenCodeRequestCount = 0;
    private long _lastOpenCodeInputTokens = 0;
    private long _lastOpenCodeOutputTokens = 0;

    private void UpdateOverallStatus()
    {
        // FocusedProvider가 비활성 공급자를 가리키면 자동 폴백 (사용자가 표시 OFF 했는데 그 공급자가 focus였던 경우)
        EnsureValidFocusedProvider();

        // 1. 각 공급자 활성 상태 판단 (데이터가 있거나 에러가 있는 경우 활성으로 간주, 단 설정에 따라 숨김)
        var settings = _settingsService.Load();
        var hideInactive = settings.HideInactiveProviders;

        ClaudeVm.IsActive = IsClaudeEnabled && (!hideInactive || TodayInputTokens + TodayOutputTokens > 0 || ClaudeVm.ShortPercent > 0 || ClaudeVm.HasError);
        IsCodexActive = IsCodexEnabled && (!hideInactive || CodexPercent > 0 || CodexHasError);
        IsGeminiActive = IsGeminiEnabled && (!hideInactive || _lastGeminiRequestCount > 0 || GeminiHasError);
        IsOpenCodeActive = IsOpenCodeEnabled && (!hideInactive || _lastOpenCodeRequestCount > 0 || OpenCodeHasError);

        ClaudeVm.IsUsageEmpty = TodayInputTokens + TodayOutputTokens == 0;
        IsCodexUsageEmpty = CodexPercent == 0;
        IsGeminiUsageEmpty = _lastGeminiRequestCount == 0;
        IsOpenCodeUsageEmpty = _lastOpenCodeRequestCount == 0;

        // 2. 트레이 표시 기준 결정 (자동 모드 로직 개선)
        if (TrayDisplayMode == UsageProviderKind.Auto)
        {
            // 오늘 사용량이 있는 공급자를 우선순위에 따라 선택 (OpenCode -> Gemini -> Codex -> Claude)
            // 단, 사용자가 활성화(Enabled)한 공급자만 선택 대상
            if (IsOpenCodeEnabled && _lastOpenCodeRequestCount > 0)
                EffectiveTrayProvider = UsageProviderKind.OpenCode;
            else if (IsGeminiEnabled && _lastGeminiRequestCount > 0)
                EffectiveTrayProvider = UsageProviderKind.GeminiCli;
            else if (IsCodexEnabled && CodexPercent > 0 && !CodexHasError)
                EffectiveTrayProvider = UsageProviderKind.Codex;
            else if (IsClaudeEnabled && TodayInputTokens + TodayOutputTokens > 0)
                EffectiveTrayProvider = UsageProviderKind.Claude;
            else
            {
                // 오늘 사용량이 없거나 모두 비활성인 경우 fallback
                // 현재 수동 선택된 공급자가 활성 상태면 그것을, 아니면 활성된 것 중 첫 번째(우선순위 역순)
                if (SelectedProvider == UsageProviderKind.Claude && IsClaudeEnabled) EffectiveTrayProvider = UsageProviderKind.Claude;
                else if (SelectedProvider == UsageProviderKind.Codex && IsCodexEnabled) EffectiveTrayProvider = UsageProviderKind.Codex;
                else if (SelectedProvider == UsageProviderKind.GeminiCli && IsGeminiEnabled) EffectiveTrayProvider = UsageProviderKind.GeminiCli;
                else if (SelectedProvider == UsageProviderKind.OpenCode && IsOpenCodeEnabled) EffectiveTrayProvider = UsageProviderKind.OpenCode;
                else if (IsClaudeEnabled)   EffectiveTrayProvider = UsageProviderKind.Claude;
                else if (IsCodexEnabled)    EffectiveTrayProvider = UsageProviderKind.Codex;
                else if (IsGeminiEnabled)   EffectiveTrayProvider = UsageProviderKind.GeminiCli;
                else if (IsOpenCodeEnabled) EffectiveTrayProvider = UsageProviderKind.OpenCode;
                else EffectiveTrayProvider = UsageProviderKind.Claude; // All disabled fallback
            }
        }
        else
        {
            EffectiveTrayProvider = TrayDisplayMode;
        }

        // 3. 트레이 표시 비율 계산
        TrayUsagePercent = EffectiveTrayProvider switch
        {
            UsageProviderKind.Claude => ClaudeVm.ShortPercent,
            UsageProviderKind.Codex => CodexPercent,
            UsageProviderKind.GeminiCli => GeminiPercent,
            UsageProviderKind.OpenCode => OpenCodePercent,
            _ => ClaudeVm.ShortPercent
        };

        // 제미나이/오픈코드 등 고정 할당량이 없는 경우 최근 7일 최대치 대비 비율로 보정 (트레이 전용)
        if (TrayDisplayMode == UsageProviderKind.Auto || TrayDisplayMode == EffectiveTrayProvider)
        {
            // 초기에 데이터가 없을 때를 대비해 최소 10,000 토큰을 기준으로 잡음
            const long defaultMinGoal = 10000;

            if (EffectiveTrayProvider == UsageProviderKind.GeminiCli)
            {
                var max = _history.GetRecentMaxTotalTokens(UsageProviderKind.GeminiCli, null, 7);
                var goal = Math.Max(defaultMinGoal, max);
                if (goal > 0) TrayUsagePercent = Math.Clamp(_lastGeminiOutputTokens / (double)goal, 0, 1);
            }
            else if (EffectiveTrayProvider == UsageProviderKind.OpenCode)
            {
                var max = _history.GetRecentMaxTotalTokens(UsageProviderKind.OpenCode, null, 7);
                var goal = Math.Max(defaultMinGoal, max);
                if (goal > 0) TrayUsagePercent = Math.Clamp((_lastOpenCodeInputTokens + _lastOpenCodeOutputTokens) / (double)goal, 0, 1);
            }
        }

        if (ClaudeVm.HasError && CodexHasError && GeminiHasError && OpenCodeHasError)
        {
            StatusText = "All Providers Error";
            HasError = true;
        }
        else if (ClaudeVm.HasError || CodexHasError || GeminiHasError || OpenCodeHasError)
        {
            StatusText = "Partial Error";
            HasError = false;
        }
        else if (IsExtraOnlyMode)
        {
            StatusText = Loc.ExtraUsageExhausted;
        }
        else
        {
            // 현재 트레이 기준을 에이전트별 특성에 맞게 표시
            StatusText = EffectiveTrayProvider switch
            {
                UsageProviderKind.Codex     => Loc.TrayStatusCodex(CodexPercent, CodexDataSource),
                UsageProviderKind.GeminiCli => Loc.TrayStatusGemini(_lastGeminiRequestCount, _lastGeminiOutputTokens),
                UsageProviderKind.OpenCode  => Loc.TrayStatusOpenCode(_lastOpenCodeRequestCount, _lastOpenCodeInputTokens, _lastOpenCodeOutputTokens),
                _ => Loc.TrayStatusClaude(ClaudeVm.ShortPercent)
            };
        }
    }

    private async Task RefreshClaudeAsync()
    {
        // FileSystemWatcher 미감지 폴백: 정기 새로고침마다 orgUuid 변경 여부 확인
        var currentOrgUuid = _credentials.GetOrganizationUuid();
        if (currentOrgUuid != _lastKnownOrgUuid)
        {
            _lastKnownOrgUuid = currentOrgUuid;
            _history.SetScope(UsageProviderKind.Claude, currentOrgUuid);
            _apiRetryAfter = DateTimeOffset.MinValue;
        }

        bool skipApi = DateTimeOffset.UtcNow < _apiRetryAfter;

        try
        {
            UsageResponse? usage = null;
            if (!skipApi)
            {
                usage = await _api.FetchUsageAsync();
                RawApiResponse = _api.LastRawResponse;
                if (_api.LastRetryAfterSeconds > 0)
                    _apiRetryAfter = DateTimeOffset.UtcNow.AddSeconds(_api.LastRetryAfterSeconds);
            }
            var sessionStats = _session.ScanTodayUsage();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TodayInputTokens  = sessionStats.TotalInputTokens;
                TodayOutputTokens = sessionStats.TotalOutputTokens;
                TodayCacheRead    = sessionStats.TotalCacheReadTokens;
                TodayCacheWrite   = sessionStats.TotalCacheWriteTokens;
                SessionsLabel     = Loc.Sessions(sessionStats.SessionCount);

                _history.RecordToday(sessionStats.TotalInputTokens, sessionStats.TotalOutputTokens,
                    sessionStats.TotalCacheReadTokens, sessionStats.TotalCacheWriteTokens,
                    sessionStats.SessionCount);
                
                // 히스토리와 시간대별 차트는 항상 Claude 기준
                HistoryData = _history.GetLast(7);
                HourlyTokens = sessionStats.HourlyTokens;

                TodayCostLabel = CalcCostLabel(sessionStats.TotalInputTokens,
                    sessionStats.TotalOutputTokens,
                    sessionStats.TotalCacheReadTokens,
                    sessionStats.TotalCacheWriteTokens);
                HasRateLimitHit   = sessionStats.HasRateLimitHit;
                RateLimitInfo     = sessionStats.RateLimitResetTime ?? "";

                if (NotificationsEnabled && NotifyRateLimit &&
                    sessionStats.HasRateLimitHit && !_prevHadRateLimit)
                {
                    _notifier.ShowRateLimitAlert(NtfyTopicEffective);
                }
                _prevHadRateLimit = sessionStats.HasRateLimitHit;

                if (usage?.FiveHour != null || usage?.SevenDay != null)
                {
                    ClaudeVm.HasError = false;
                    ClaudeVm.ErrorMessage = "";
                    ClaudeVm.ApiNote = "";
                    // OAuth-not-allowed 가 해소된 첫 성공 → 첫감지 시각 클리어
                    ClearOAuthNotAllowedFirstSeenIfNeeded();

                    if (usage.FiveHour != null && usage.FiveHour.UsagePercent < 1.0)
                    {
                        HasRateLimitHit = false;
                        RateLimitInfo = "";
                    }

                    if (usage!.FiveHour != null)
                    {
                        var newPercent = usage.FiveHour.UsagePercent;
                        _rawClaudeShortResetAt = usage.FiveHour.ResetsAtParsed;
                        ClaudeVm.ShortReset = FormatResetLabel(_rawClaudeShortResetAt);
                        ClaudeVm.ShortSummary = Loc.UsageSummary(newPercent);
                        ClaudeVm.ShortDepletion = CalcDepletionLabel(usage.FiveHour);

                        // 조기 소진 푸시 알림: 예상 소진 시각이 이전보다 당겨졌을 때만 발송
                        if (NotificationsEnabled && !string.IsNullOrEmpty(ClaudeVm.ShortDepletion))
                        {
                            var currentReset = usage.FiveHour.ResetsAtParsed;
                            if (currentReset.HasValue)
                            {
                                var windowStart = currentReset.Value - TimeSpan.FromHours(5);
                                var elapsed = DateTimeOffset.Now - windowStart;
                                if (elapsed.TotalMinutes >= 5)
                                {
                                    double ratePerHour = usage.FiveHour.UsagePercent / elapsed.TotalHours;
                                    if (ratePerHour > 0)
                                    {
                                        double hoursToFull = (1.0 - usage.FiveHour.UsagePercent) / ratePerHour;
                                        var depletionAt = DateTimeOffset.Now.AddHours(hoursToFull).ToLocalTime();

                                        if (!_lastNotifiedEarlyDepletionAt.HasValue || depletionAt < _lastNotifiedEarlyDepletionAt.Value)
                                        {
                                            _notifier.ShowEarlyExhaustionAlert(
                                                depletionAt.ToString("HH:mm"),
                                                FormatResetLabel(currentReset),
                                                NtfyTopicEffective);
                                        }
                                        // 조기 소진 예상이 늦춰졌더라도 기준 시각은 업데이트(다음 비교 기준)
                                        _lastNotifiedEarlyDepletionAt = depletionAt;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // 조기 소진 예상이 사라졌으면 초기화
                            _lastNotifiedEarlyDepletionAt = null;
                        }
                        _prevShortDepletion = ClaudeVm.ShortDepletion;

                        if (NotificationsEnabled && _prevShortPercent >= 0)
                        {
                            CheckThresholds(newPercent, ClaudeVm.ShortReset, NtfyTopicEffective);
                        }

                        ClaudeVm.ShortPercent = newPercent;
                        _prevShortPercent = newPercent;
                        _lastKnownShortPercent = newPercent;
                        _lastKnownShortReset = ClaudeVm.ShortReset;
                        
                        // Compatibility for legacy bindings if any
                        ShortUsagePercent = newPercent;
                        ShortResetLabel = ClaudeVm.ShortReset;
                    }

                    if (usage.SevenDay != null)
                    {
                        ClaudeVm.LongPercent    = usage.SevenDay.UsagePercent;
                        _rawClaudeLongResetAt = usage.SevenDay.ResetsAtParsed;
                        ClaudeVm.LongReset      = FormatResetLabel(_rawClaudeLongResetAt);
                        ClaudeVm.LongSummary    = Loc.UsageSummary(ClaudeVm.LongPercent);
                        ClaudeVm.LongDepletion  = CalcLongDepletionLabel(usage.SevenDay);
                        _lastKnownLongPercent = ClaudeVm.LongPercent;
                        _lastKnownLongReset = ClaudeVm.LongReset;
                        
                        LongUsagePercent = ClaudeVm.LongPercent;
                        LongResetLabel = ClaudeVm.LongReset;
                    }

                    if (usage.SevenDayOpus != null)
                    {
                        OpusPercent = usage.SevenDayOpus.UsagePercent;
                        OpusTokens  = (long)usage.SevenDayOpus.Utilization;
                    }

                    if (usage.SevenDaySonnet != null)
                    {
                        SonnetPercent = usage.SevenDaySonnet.UsagePercent;
                        SonnetTokens  = (long)usage.SevenDaySonnet.Utilization;
                    }

                    if (usage.ExtraUsage is { IsEnabled: true } eu)
                    {
                        var extraHasLimit = eu.MonthlyLimit.HasValue;
                        var extraUsagePercent = eu.MonthlyLimit.HasValue
                            ? Math.Min(1.0, (eu.Utilization ?? 0) / 100.0)
                            : 0;
                        var extraCreditsLabel = (eu.UsedCredits.HasValue && eu.MonthlyLimit.HasValue)
                            ? Loc.ExtraCredits(eu.UsedCredits.Value, eu.MonthlyLimit.Value)
                            : eu.UsedCredits.HasValue
                                ? Loc.ExtraCreditsUsedOnly(eu.UsedCredits.Value)
                                : "";
                        SetClaudeExtraUsage(true, extraHasLimit, extraUsagePercent, extraCreditsLabel);

                        if (_prevExtraPercent < 0) _prevExtraPercent = ExtraUsagePercent;
                    }
                    else
                    {
                        SetClaudeExtraUsage(false);
                    }

                    if (ExtraUsageEnabled && ClaudeVm.ShortPercent >= 1.0)
                    {
                        SetClaudeExtraOnlyMode(true);
                        StatusText = Loc.ExtraUsageExhausted;
                    }
                    else if (ClaudeVm.ShortPercent < 0.5)
                    {
                        SetClaudeExtraOnlyMode(false);
                        StatusText = $"{ClaudeVm.ShortPercent:P0} used";
                    }
                    else if (!IsExtraOnlyMode)
                    {
                        StatusText = $"{ClaudeVm.ShortPercent:P0} used";
                    }
                }
                else if (skipApi || _api.LastError != null)
                {
                    ClaudeVm.ShortPercent = _lastKnownShortPercent;
                    ClaudeVm.ShortReset   = _lastKnownShortReset;
                    ClaudeVm.ShortSummary = Loc.UsageSummary(_lastKnownShortPercent);
                    ClaudeVm.LongPercent  = _lastKnownLongPercent;
                    ClaudeVm.LongReset    = _lastKnownLongReset;
                    ClaudeVm.LongSummary  = Loc.UsageSummary(_lastKnownLongPercent);

                    // 403 permission_error: 두 가지 케이스로 세분
                    //   (a) "currently not allowed for this organization" — 신규 계정 검증/조직 OAuth 미활성 (일시적, 24h내 자동 해소 가능성)
                    //   (b) 그 외 permission_error — 영구적 권한 부족 (워크스페이스 설정 필요 등)
                    bool isPermissionDenied = _api.LastError != null
                        && _api.LastError.Contains("HTTP 403")
                        && _api.LastError.Contains("permission_error");

                    bool isOAuthNotAllowed = isPermissionDenied
                        && _api.LastError!.Contains("currently not allowed");

                    // 쿨다운 판정은 호출 "후" _apiRetryAfter 기준으로 — 첫 429부터 즉시 회색 톤 라우팅
                    bool isCooldown = _apiRetryAfter > DateTimeOffset.UtcNow;

                    if (isPermissionDenied)
                    {
                        ClaudeVm.HasError = false;
                        ClaudeVm.ErrorMessage = "";
                        if (isOAuthNotAllowed)
                        {
                            ClaudeVm.ApiNote = ResolveOAuthNotAllowedNote();
                        }
                        else
                        {
                            // 다른 종류의 permission_error → OAuth-not-allowed 추적 상태가 있다면 정리
                            ClearOAuthNotAllowedFirstSeenIfNeeded();
                            ClaudeVm.ApiNote = Loc.ApiPermissionDeniedNote;
                        }
                    }
                    else if (isCooldown)
                    {
                        // 일시 제한은 에러 아닌 안내로만 — 빨간 박스 대신 회색 톤 자동 재시도 안내
                        ClaudeVm.HasError = false;
                        ClaudeVm.ErrorMessage = "";
                        ClaudeVm.ApiNote = Loc.ApiCooldownNote(_apiRetryAfter.ToLocalTime().ToString("HH:mm"));
                        // 쿨다운은 OAuth-not-allowed 와 무관 — 추적 상태 유지(클리어하지 않음)
                    }
                    else
                    {
                        ClaudeVm.HasError = true;
                        // 토큰 자체가 없어 네트워크 호출 전에 실패한 경우 — 막연한 원문 대신 로그인 안내로.
                        ClaudeVm.ErrorMessage = _api.LastError == UsageApiService.NoTokenError
                            ? Loc.NoToken
                            : _api.LastError != null
                                ? ParseFriendlyError(_api.LastError)
                                : Loc.RateLimited;
                        ClaudeVm.ApiNote = "";
                        // 다른 종류의 에러로 전이 → 추적 상태 정리
                        ClearOAuthNotAllowedFirstSeenIfNeeded();
                    }
                }
            });
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ClaudeVm.HasError = true;
                ClaudeVm.ErrorMessage = ex.Message;
                ClaudeVm.ApiNote = "";
                UpdateOverallStatus();
            });
        }
        finally
        {
        }
    }

    // 403 + "currently not allowed" 가 처음 감지된 시각을 settings 에 보관해두고,
    // 24h 이상 지속되면 안내문을 에스컬레이션 톤으로 전환한다.
    // settings 디스크 쓰기는 상태 전이(미감지→감지) 시 1회만 — 폴링마다 매번 쓰지 않음.
    private string ResolveOAuthNotAllowedNote()
    {
        var settings = _settingsService.Load();
        var nowUtc = DateTime.UtcNow;
        DateTime firstSeen;
        if (settings.OAuthNotAllowedFirstSeenUtc is DateTime existing)
        {
            firstSeen = existing;
        }
        else
        {
            // 첫 감지 — history 에 24h+ 전 기록이 있으면 사용자가 이미 그만큼 앱을 써온 셈이므로
            // firstSeen 을 가장 오래된 history 시점으로 추정해 즉시 에스컬레이션 톤으로 진입.
            // 신규 사용자(history 비어있음 / 24h 미만)는 nowUtc 로 잡아 기존 유예 톤 유지.
            firstSeen = EstimateOAuthNotAllowedFirstSeenUtc(nowUtc);
            settings.OAuthNotAllowedFirstSeenUtc = firstSeen;
            _settingsService.Save(settings);
        }

        var elapsed = nowUtc - firstSeen;
        if (elapsed >= TimeSpan.FromHours(24))
        {
            return Loc.ApiOAuthNotAllowedEscalatedNote(Loc.ElapsedDurationLabel(elapsed));
        }
        return Loc.ApiOAuthNotAllowedNote;
    }

    // history 에서 가장 오래된 사용 기록 시점을 찾아 24h+ 전이면 그 시점을 반환.
    // history 가 비어있거나 모든 기록이 24h 이내면 nowUtc 반환 (=신규 사용자).
    // history Date 는 "yyyy-MM-dd" UTC 기준 — 자정으로 환산해 보수적으로 추정.
    private DateTime EstimateOAuthNotAllowedFirstSeenUtc(DateTime nowUtc)
    {
        try
        {
            var orgUuid = _credentials.GetOrganizationUuid();
            var entries = _history.GetLast(UsageProviderKind.Claude, orgUuid, AppConstants.HistoryRetentionDays);
            if (entries.Count == 0) return nowUtc;

            var earliestStr = entries.Min(s => s.Date);
            if (string.IsNullOrEmpty(earliestStr)) return nowUtc;

            if (DateTime.TryParseExact(earliestStr, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var earliestUtc))
            {
                // 자정 UTC 로 잡혀있어 충분히 보수적. 24h 이상 전이어야 즉시 에스컬레이션.
                if (earliestUtc <= nowUtc.AddHours(-24)) return earliestUtc;
            }
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] EstimateOAuthNotAllowedFirstSeenUtc failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
        }
        return nowUtc;
    }

    // OAuth-not-allowed 추적 상태를 정리한다. 값이 이미 null 이면 디스크 I/O 생략.
    private void ClearOAuthNotAllowedFirstSeenIfNeeded()
    {
        var settings = _settingsService.Load();
        if (settings.OAuthNotAllowedFirstSeenUtc is null) return;
        settings.OAuthNotAllowedFirstSeenUtc = null;
        _settingsService.Save(settings);
    }

    private async Task RefreshCodexInternalAsync()
    {
        await CodexVm.RefreshAsync(
            ShowAbsoluteResetTime,
            NtfyTopicEffective,
            NotificationsEnabled,
            NotifyOnQuotaReset,
            (threshold, windowLabel, resetLabel, topic) =>
                _notifier.ShowUsageAlert(threshold, windowLabel, resetLabel, topic,
                    UsageProviderKind.DisplayName(UsageProviderKind.Codex),
                    ThresholdToPriority(threshold)),
            () => _notifier.ShowQuotaResetAlert(NtfyTopicEffective));

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CodexPercent = CodexVm.Percent;
            _prevCodexPercent = CodexVm.Percent;
            _rawCodexShortResetAt = CodexVm.RawShortResetAt;
            _rawCodexShortResetEstimated = CodexVm.RawShortResetEstimated;
            CodexReset = CodexVm.Reset;
            CodexDataSource = CodexVm.DataSource;
            CodexHasError = CodexVm.HasError;
            CodexErrorMessage = CodexVm.ErrorMessage;
            CodexSummary = CodexVm.Summary;
            CodexLongPercent = CodexVm.LongPercent;
            _rawCodexLongResetAt = CodexVm.RawLongResetAt;
            CodexLongReset = CodexVm.LongReset;
            CodexLongSummary = CodexVm.LongSummary;
            IsCodexLongVisible = CodexVm.IsLongVisible;
            CodexPlanLabel = CodexVm.PlanLabel;
            CodexInputLabel = CodexVm.InputLabel;
            CodexOutputLabel = CodexVm.OutputLabel;
            CodexCacheReadLabel = CodexVm.CacheReadLabel;
            CodexCacheWriteLabel = CodexVm.CacheWriteLabel;
            IsCodexUsageEmpty = CodexVm.IsUsageEmpty;
            UpdateOverallStatus();
        });
    }

    private async Task RefreshGeminiCliInternalAsync()
    {
        await GeminiVm.RefreshAsync();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            GeminiPercent = GeminiVm.Percent;
            _prevGeminiPercent = GeminiVm.Percent;
            GeminiHasError = GeminiVm.HasError;
            GeminiErrorMessage = GeminiVm.ErrorMessage;
            GeminiSummary = GeminiVm.Summary;
            GeminiRequestsLabel = GeminiVm.RequestsLabel;
            GeminiOutputTokensLabel = GeminiVm.OutputTokensLabel;
            GeminiInputLabel = GeminiVm.InputLabel;
            GeminiCacheReadLabel = GeminiVm.CacheReadLabel;
            GeminiCacheWriteLabel = GeminiVm.CacheWriteLabel;
            IsGeminiUsageEmpty = GeminiVm.IsUsageEmpty;
            _lastGeminiRequestCount = GeminiVm.LastRequestCount;
            _lastGeminiOutputTokens = GeminiVm.LastOutputTokens;
            UpdateOverallStatus();
        });
    }

    private async Task RefreshOpenCodeInternalAsync()
    {
        await OpenCodeVm.RefreshAsync();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            OpenCodeHasError = OpenCodeVm.HasError;
            OpenCodeErrorMessage = OpenCodeVm.ErrorMessage;
            OpenCodeSummary = OpenCodeVm.Summary;
            OpenCodeInputLabel = OpenCodeVm.InputLabel;
            OpenCodeOutputLabel = OpenCodeVm.OutputLabel;
            OpenCodeRequestCountLabel = OpenCodeVm.RequestCountLabel;
            OpenCodeCacheReadLabel = OpenCodeVm.CacheReadLabel;
            OpenCodeCacheWriteLabel = OpenCodeVm.CacheWriteLabel;
            OpenCodePercent = OpenCodeVm.Percent;
            IsOpenCodeUsageEmpty = OpenCodeVm.IsUsageEmpty;
            _lastOpenCodeRequestCount = OpenCodeVm.LastRequestCount;
            _lastOpenCodeInputTokens = OpenCodeVm.LastInputTokens;
            _lastOpenCodeOutputTokens = OpenCodeVm.LastOutputTokens;
            UpdateOverallStatus();
        });
    }

    // Manual triggers (optional, usually RefreshAsync is enough)
    [RelayCommand]
    public async Task RefreshCodexAsync() => await RefreshAsync();

    [RelayCommand]
    public async Task RefreshGeminiCliAsync() => await RefreshAsync();

    private void ApplyProviderSnapshot(ProviderUsageSnapshot snapshot, bool isError)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            TodayInputTokens = snapshot.TotalInputTokens;
            TodayOutputTokens = snapshot.TotalOutputTokens;
            TodayCacheRead = snapshot.TotalCacheReadTokens;
            TodayCacheWrite = snapshot.TotalCacheWriteTokens;
            SessionsLabel = Loc.Sessions(snapshot.SessionCount);
            HistoryData = _history.GetLast(7);
            HourlyTokens = snapshot.HourlyTokens;
            TodayCostLabel = CalcCostLabel(snapshot.TotalInputTokens,
                snapshot.TotalOutputTokens,
                snapshot.TotalCacheReadTokens,
                snapshot.TotalCacheWriteTokens);

            _history.RecordToday(snapshot.TotalInputTokens, snapshot.TotalOutputTokens,
                snapshot.TotalCacheReadTokens, snapshot.TotalCacheWriteTokens, snapshot.SessionCount);
            HistoryData = _history.GetLast(7);

            ShortUsagePercent = snapshot.ShortUsagePercent;
            LongUsagePercent = snapshot.LongUsagePercent;
            ShortResetLabel = FormatResetLabel(snapshot.ShortResetAt);
            LongResetLabel = FormatResetLabel(snapshot.LongResetAt);
            ClaudeVm.ShortSummary = Loc.UsageSummary(snapshot.ShortUsagePercent);
            ClaudeVm.LongSummary = Loc.UsageSummary(snapshot.LongUsagePercent);
            ShortDepletionLabel = snapshot.ShortResetAt is null ? "" : "";
            LongDepletionLabel = snapshot.LongResetAt is null ? "" : "";

            SetClaudeExtraUsage(false);
            SetClaudeExtraOnlyMode(false);
            HasRateLimitHit = false;
            RateLimitInfo = "";

            HasError = isError;
            ErrorMessage = snapshot.IsLimited ? "" : snapshot.ErrorMessage ?? "";
            RateLimitInfo = snapshot.IsLimited ? snapshot.ErrorMessage ?? "" : "";
            HasRateLimitHit = snapshot.IsLimited;
            StatusText = isError
                ? "Source Unavailable"
                : snapshot.IsLimited
                    ? "Limited Data"
                : snapshot.ShortUsagePercent > 0
                    ? $"{snapshot.ShortUsagePercent:P0} used"
                    : Loc.Updated;
            LastUpdatedLabel = HasError
                ? $"⚠ {DateTime.Now:HH:mm:ss}"
                : Loc.UpdatedAt(DateTime.Now.ToString("HH:mm:ss"));
            IsLoading = false;
        });
    }

    private void CheckThresholds(double newPercent, string resetLabel, string ntfyTopic)
    {
        var settings = _settingsService.Load();

        // 1. 할당량 초기화 감지 (100% -> 100% 미만)
        if (NotifyOnQuotaReset && _prevShortPercent >= 1.0 && newPercent < 1.0)
        {
            _notifier.ShowQuotaResetAlert(ntfyTopic);
        }

        // 2. 기본 사용량 임계값 알림
        foreach (var t in settings.Thresholds.OrderBy(x => x))
        {
            double tf = t / 100.0;
            if (_prevShortPercent < tf && newPercent >= tf)
            {
                _notifier.ShowUsageAlert(t, Loc.FiveHourWindow, resetLabel, ntfyTopic, "Claude", ThresholdToPriority(t));
            }
        }

        // 3. 추가 사용량 알림 (기본 사용량 100% 소진 후 모드인 경우)
        if (IsExtraOnlyMode && ExtraHasLimit)
        {
            foreach (var t in settings.Thresholds.OrderBy(x => x))
            {
                double tf = t / 100.0;
                // 이전 값과 현재 값 비교 (초기값 0에서 첫 알림이 가지 않도록)
                if (_prevExtraPercent < tf && ExtraUsagePercent >= tf)
                {
                    _notifier.ShowUsageAlert(t, Loc.ExtraUsageTitle, "", ntfyTopic, "Claude", ThresholdToPriority(t));
                }
            }
            _prevExtraPercent = ExtraUsagePercent;
        }
    }

    private void SetClaudeExtraUsage(bool enabled, bool hasLimit = false, double usagePercent = 0, string creditsLabel = "")
    {
        ExtraUsageEnabled = enabled;
        ExtraHasLimit = enabled && hasLimit;
        ExtraUsagePercent = enabled ? usagePercent : 0;
        ExtraCreditsLabel = enabled ? creditsLabel : "";

        ClaudeVm.ExtraUsageEnabled = ExtraUsageEnabled;
        ClaudeVm.ExtraHasLimit = ExtraHasLimit;
        ClaudeVm.ExtraUsagePercent = ExtraUsagePercent;
        ClaudeVm.ExtraCreditsLabel = ExtraCreditsLabel;
    }

    private void SetClaudeExtraOnlyMode(bool enabled)
    {
        IsExtraOnlyMode = enabled;
        ClaudeVm.IsExtraOnlyMode = enabled;
    }

    private static int ThresholdToPriority(int threshold) =>
        UsageCalculator.ThresholdToPriority(threshold);

    private string FormatResetLabel(DateTimeOffset? resetAt, bool isEstimated = false) =>
        UsageCalculator.FormatResetLabel(resetAt, isEstimated, ShowAbsoluteResetTime, DateTimeOffset.Now);

    /// <summary>
    /// 10분 미만 남은 리셋 라벨을 1초마다 업데이트 (초 단위 카운트다운).
    /// API 요청 없이 화면 표시만 갱신한다.
    /// </summary>
    private void UpdateResetLabelsIfNeeded()
    {
        var now = DateTimeOffset.Now;

        if (_rawClaudeShortResetAt.HasValue && (_rawClaudeShortResetAt.Value - now).TotalMinutes < 10)
            ClaudeVm.ShortReset = FormatResetLabel(_rawClaudeShortResetAt);

        if (_rawClaudeLongResetAt.HasValue && (_rawClaudeLongResetAt.Value - now).TotalMinutes < 10)
            ClaudeVm.LongReset = FormatResetLabel(_rawClaudeLongResetAt);

        if (_rawCodexShortResetAt.HasValue && (_rawCodexShortResetAt.Value - now).TotalMinutes < 10)
            CodexReset = FormatResetLabel(_rawCodexShortResetAt, _rawCodexShortResetEstimated);

        if (_rawCodexLongResetAt.HasValue && (_rawCodexLongResetAt.Value - now).TotalMinutes < 10)
            CodexLongReset = FormatResetLabel(_rawCodexLongResetAt);
    }

    /// <summary>
    /// ShowAbsoluteResetTime 토글 시 4개 reset 라벨을 raw 값에서 즉시 재포맷.
    /// (API 재호출 없이 사용자가 토글한 즉시 반영)
    /// </summary>
    partial void OnShowAbsoluteResetTimeChanged(bool value)
    {
        ClaudeVm.ShortReset = FormatResetLabel(_rawClaudeShortResetAt);
        ClaudeVm.LongReset  = FormatResetLabel(_rawClaudeLongResetAt);
        CodexReset       = FormatResetLabel(_rawCodexShortResetAt, _rawCodexShortResetEstimated);
        CodexLongReset   = FormatResetLabel(_rawCodexLongResetAt);
    }

    partial void OnWeatherEnabledChanged(bool value) => NotifyWeatherComputed();
    partial void OnWeatherLatitudeChanged(double? value) => NotifyWeatherComputed();
    partial void OnWeatherLongitudeChanged(double? value) => NotifyWeatherComputed();
    partial void OnWeatherLocationNameChanged(string value)
    {
        NotifyWeatherComputed();
        OnPropertyChanged(nameof(WeatherShortLocation));
    }
    partial void OnWeatherTooltipLabelChanged(string value) => OnPropertyChanged(nameof(WeatherPopupLabel));
    partial void OnWeatherTemperatureLabelChanged(string value) => OnPropertyChanged(nameof(WeatherHasCurrent));

    private void NotifyWeatherComputed()
    {
        OnPropertyChanged(nameof(WeatherHasLocation));
        OnPropertyChanged(nameof(WeatherHasCurrent));
        OnPropertyChanged(nameof(WeatherPopupLabel));
    }

    private static string ParseFriendlyError(string raw)
    {
        var msgText = raw;
        try
        {
            var start = raw.IndexOf('{');
            if (start >= 0)
            {
                var json = raw[start..];
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    if (err.TryGetProperty("message", out var msg))
                        msgText = msg.GetString() ?? raw;
                    // 특정 에러 타입을 감지하여 사용자 친화적 메시지로 전환
                    var errorType = err.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (errorType == "permission_error" || msgText?.Contains("does not support") == true)
                        return Loc.ApiPermissionDenied;
                }
            }
        }
        catch { }
        if (raw.Contains("429") || raw.Contains("rate_limit"))
            return Loc.RateLimited;
        var display = msgText ?? raw;
        return Loc.ApiError(display.Length > 200 ? display[..200] + "…" : display);
    }

    private static string CalcDepletionLabel(Models.UsageWindow w) =>
        UsageCalculator.CalcDepletionLabel(w, DateTimeOffset.Now);

    private static string CalcLongDepletionLabel(Models.UsageWindow w) =>
        UsageCalculator.CalcLongDepletionLabel(w, DateTimeOffset.Now);

    private static string CalcCostLabel(long input, long output, long cacheRead, long cacheWrite) =>
        UsageCalculator.CalcCostLabel(input, output, cacheRead, cacheWrite);

    public NotificationSettings GetCurrentSettings()
    {
        return _settingsService.Load();
    }

    private async Task RefreshWeatherInternalAsync()
    {
        var interval = TimeSpan.FromMinutes(WeatherRefreshIntervalMinutes);
        if (DateTimeOffset.Now - _weatherLastRefresh < interval)
            return;

        WeatherVm.Enabled = WeatherEnabled;
        WeatherVm.ShowInTrayTooltip = WeatherShowInTrayTooltip;
        WeatherVm.LocationMode = WeatherLocationMode;
        WeatherVm.LocationName = WeatherLocationName;
        WeatherVm.CountryCode = WeatherCountryCode;
        WeatherVm.Latitude = WeatherLatitude;
        WeatherVm.Longitude = WeatherLongitude;
        WeatherVm.Timezone = WeatherTimezone;
        WeatherVm.RefreshIntervalMinutes = WeatherRefreshIntervalMinutes;
        WeatherVm.DailyForecastEnabled = WeatherDailyForecastEnabled;
        WeatherVm.DailyForecastTime = WeatherDailyForecastTime;
        WeatherVm.ConditionAlertsEnabled = WeatherConditionAlertsEnabled;
        WeatherVm.RainProbabilityThreshold = WeatherRainProbabilityThreshold;
        WeatherVm.HighTemperatureThresholdC = WeatherHighTemperatureThresholdC;
        WeatherVm.LowTemperatureThresholdC = WeatherLowTemperatureThresholdC;
        WeatherVm.WindSpeedThresholdKmh = WeatherWindSpeedThresholdKmh;
        WeatherVm.OfficialAlertsEnabled = WeatherOfficialAlertsEnabled;

        await WeatherVm.RefreshAsync();
        _weatherLastRefresh = DateTimeOffset.Now;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            WeatherStatusLabel = WeatherVm.StatusLabel;
            WeatherTooltipLabel = WeatherVm.TooltipLabel;
            WeatherHasError = WeatherVm.HasError;
            WeatherErrorMessage = WeatherVm.ErrorMessage;
            WeatherTemperatureLabel = WeatherVm.TemperatureLabel;
            WeatherConditionLabel = WeatherVm.ConditionLabel;
            WeatherIcon = WeatherVm.Icon;
        });
    }

    // BMP-only (U+2600–U+26FF, U+2744): WPF can render these via Segoe UI Symbol.
    // High-plane emoji (U+1F3xx) like 🌤🌫🌦🌧⛈ aren't covered by symbol fonts, so
    // we collapse those conditions onto the closest BMP analogue.
    private static string GetWeatherIcon(string conditionKey) => conditionKey switch
    {
        "clear" or "mainly_clear" => "☀",
        "partly_cloudy" => "⛅",
        "overcast" or "fog" => "☁",
        "drizzle" or "freezing_drizzle" => "☂",
        "rain" or "freezing_rain" or "rain_showers" => "☔",
        "snow" or "snow_grains" or "snow_showers" => "❄",
        "thunderstorm" => "⚡",
        _ => "•"
    };

    private static string GetWeatherConditionLabel(string conditionKey) => conditionKey switch
    {
        "clear" => Loc.WeatherClear,
        "mainly_clear" => Loc.WeatherMainlyClear,
        "partly_cloudy" => Loc.WeatherPartlyCloudy,
        "overcast" => Loc.WeatherOvercast,
        "fog" => Loc.WeatherFog,
        "drizzle" or "freezing_drizzle" => Loc.WeatherDrizzle,
        "rain" or "freezing_rain" or "rain_showers" => Loc.WeatherRain,
        "snow" or "snow_grains" or "snow_showers" => Loc.WeatherSnow,
        "thunderstorm" => Loc.WeatherThunderstorm,
        _ => Loc.WeatherUnknown
    };

    public void Dispose()
    {
        _credentials.CredentialsChanged -= OnCredentialsChanged;
        _credentials.Dispose();
        _antigravity.Dispose();
        _timer.Dispose();
        _countdownTimer.Dispose();
        _updateTimer.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RefreshAntigravityInternalAsync()
    {
        AntigravityVm.IsEnabled = IsAntigravityEnabled;
        await AntigravityVm.RefreshAsync();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            AntigravityHasData = AntigravityVm.HasData;
            AntigravityHasError = AntigravityVm.HasError;
            AntigravityErrorMessage = AntigravityVm.ErrorMessage;
            AntigravityTierName = AntigravityVm.TierName;
            AntigravityPaidTierName = AntigravityVm.PaidTierName;
            AntigravityModels = AntigravityVm.Models;
            AntigravityPercent = AntigravityVm.Percent;
        });
    }
}

/// <summary>Single row binding model for the Antigravity model quota list.</summary>
public sealed class AntigravityModelRow
{
    public string ModelId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public double UsagePercent { get; init; }   // 0..1
    public string UsageLabel { get; init; } = "";
    public string ResetAtLabel { get; init; } = "";
}
