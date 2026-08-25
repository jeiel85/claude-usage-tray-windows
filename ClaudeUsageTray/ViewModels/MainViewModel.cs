using System.Diagnostics;
using System.Text.Json;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Forecasts;
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
    private readonly UsageSyncService _usageSync;
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

    // Last known good API data (kept when rate-limited so UI doesn't reset to 0).
    // null = 아직 한 번도 조회에 성공하지 못함 — 이 상태를 0% 로 표시하면 "여유 100%"라는
    // 거짓 정보가 되므로 UI 에서 "—" 로 구분해야 한다.
    private double? _lastKnownShortPercent;
    private double? _lastKnownLongPercent;
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
    [ObservableProperty] private double _codexShortTimePercent = 0;
    [ObservableProperty] private double _codexLongTimePercent = 0;
    // 시간선 표시 여부 — 지금이 창 밖(리셋이 이미 지났거나 창 길이가 어긋남)이면 마커를 숨긴다.
    [ObservableProperty] private bool _hasCodexShortTimeline = false;
    [ObservableProperty] private bool _hasCodexLongTimeline = false;
    [ObservableProperty] private string _codexShortPaceTip = "";
    [ObservableProperty] private string _codexLongPaceTip = "";
    // v1.26.0: PlanType 라벨 ("ChatGPT Plus"). 요금제를 모르면 비워 두고 배지를 숨긴다.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCodexPlanBadgeVisible))]
    private string _codexPlanLabel = "";
    [ObservableProperty] private string _codexShortWindowLabel = Loc.ShortWindow;
    [ObservableProperty] private string _codexLongWindowLabel = Loc.LongWindow;
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
    // 상세 하단 안내 + 출처 — Codex 상세의 같은 자리(왼쪽 안내 / 오른쪽 출처)와 같은 구성.
    [ObservableProperty] private string _antigravityNote = Loc.ProviderAntigravityNote;
    [ObservableProperty] private string _antigravityDataSource = "";
    // 출처 문구는 언어가 바뀌면 다시 만들어야 하므로 완성된 문장 대신 재료(기기·관측 시각)를 들고 있는다.
    private (string Device, DateTimeOffset ObservedAt)? _antigravityQuotaOrigin;

    /// <summary>플랜 배지 문구 — Codex 의 플랜 라벨과 같은 자리에 쓴다. 요금제 이름이 없으면 tier 이름으로 대신한다.</summary>
    public string AntigravityPlanLabel =>
        string.IsNullOrWhiteSpace(AntigravityPaidTierName) ? AntigravityTierName : AntigravityPaidTierName;

    partial void OnAntigravityTierNameChanged(string value)
    {
        OnPropertyChanged(nameof(AntigravityPlanLabel));
        OnPropertyChanged(nameof(IsAntigravityPlanBadgeVisible));
    }

    partial void OnAntigravityPaidTierNameChanged(string value)
    {
        OnPropertyChanged(nameof(AntigravityPlanLabel));
        OnPropertyChanged(nameof(IsAntigravityPlanBadgeVisible));
    }

    // 구독 등급 배지 (v1.41.0) — 공급자마다 등급을 알아내는 출처가 다르지만 표시 규칙은 하나다:
    // 등급을 알아낸 공급자만 배지를 그리고, 모르는 공급자는 자리 자체를 비운다.
    //   Claude       ~/.claude/.credentials.json 의 subscriptionType + rateLimitTier
    //   Codex        세션 로그·API 의 rate_limits.plan_type, 없으면 ~/.codex/auth.json 의 id_token
    //   OpenCode     ~/.local/share/opencode/auth.json 에 저장된 OpenCode 로그인 항목
    //   Antigravity  loadCodeAssist 응답의 tier 이름
    //   Gemini CLI   등급을 알 수 있는 로컬 자료가 없어 배지 없음
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClaudePlanBadgeVisible))]
    private string _claudePlanLabel = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenCodePlanBadgeVisible))]
    private string _openCodePlanLabel = "";

    public bool IsClaudePlanBadgeVisible => ShowPlanBadge && !string.IsNullOrWhiteSpace(ClaudePlanLabel);
    public bool IsCodexPlanBadgeVisible => ShowPlanBadge && !string.IsNullOrWhiteSpace(CodexPlanLabel);
    public bool IsOpenCodePlanBadgeVisible => ShowPlanBadge && !string.IsNullOrWhiteSpace(OpenCodePlanLabel);
    public bool IsAntigravityPlanBadgeVisible => ShowPlanBadge && !string.IsNullOrWhiteSpace(AntigravityPlanLabel);

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

    // Weather data source (v1.37.0)
    [ObservableProperty] private string _weatherForecastSource = WeatherService.AutoSource;
    [ObservableProperty] private string _weatherForecastModel = OpenMeteoForecastProvider.AutoModel;

    /// <summary>
    /// 설정 파일에 남아 있는 알 수 없는 소스/모델 값(예전 버전, 손댄 JSON, 지원이 끊긴 모델)을
    /// 자동값으로 되돌린다. 그대로 두면 매 조회가 폴백을 타면서 첫 시도가 낭비된다.
    /// </summary>
    private static string NormalizeForecastSource(string? value) =>
        !string.IsNullOrWhiteSpace(value) && WeatherService.SelectableSources.Contains(value)
            ? value : WeatherService.AutoSource;

    private static string NormalizeForecastModel(string? value) =>
        !string.IsNullOrWhiteSpace(value) && OpenMeteoForecastProvider.SelectableModels.Contains(value)
            ? value : OpenMeteoForecastProvider.AutoModel;

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

    // 오늘 세션 목록 — "오늘 N개 세션" 라벨을 눌러 펼친다. 목록은 이 PC 의 트랜스크립트에서만
    // 나오므로, 다중 PC 동기화가 켜져 있으면 합계 세션 수보다 짧을 수 있다(차이는 안내로 표시).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSessionListEmpty))]
    private IReadOnlyList<SessionListItem> _todaySessions = [];

    [ObservableProperty] private bool _isSessionListExpanded = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionListMoreLabel))]
    private int _sessionListHiddenCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionListNote))]
    private int _sessionListRemoteCount = 0;

    public bool IsSessionListEmpty => TodaySessions.Count == 0;
    public string SessionListMoreLabel => SessionListHiddenCount > 0 ? Loc.SessionListMore(SessionListHiddenCount) : "";
    public string SessionListNote => SessionListRemoteCount > 0 ? Loc.SessionListRemoteOnly(SessionListRemoteCount) : "";

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
    [ObservableProperty] private bool _usageSyncEnabled;
    [ObservableProperty] private string _usageSyncFolderPath = "";
    [ObservableProperty] private int _usageSyncApiSnapshotTtlMinutes = UsageSyncService.DefaultApiSnapshotTtlMinutes;
    [ObservableProperty] private int _usageSyncLocalSnapshotTtlHours = UsageSyncService.DefaultLocalSnapshotTtlHours;
    [ObservableProperty] private string _usageSyncStatusLabel = Loc.UsageSyncDisabled;

    // v1.27.0 표시 옵션 토글 (v1.41.0: Codex 전용 → 전 공급자 공통)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClaudePlanBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsCodexPlanBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsOpenCodePlanBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsAntigravityPlanBadgeVisible))]
    private bool _showPlanBadge = true;
    [ObservableProperty] private bool _showAbsoluteResetTime = false;
    [ObservableProperty] private bool _keepPopupAboveTaskbar = false;
    [ObservableProperty] private double _usagePanelOpacity = 0.94;

    // 절대 시각 토글 시 재포맷 위해 raw DateTimeOffset 보관 — API 재호출 없이 즉시 라벨 갱신
    private DateTimeOffset? _rawClaudeShortResetAt;
    private DateTimeOffset? _rawClaudeLongResetAt;
    private DateTimeOffset? _rawCodexShortResetAt;
    private bool _rawCodexShortResetEstimated;
    private DateTimeOffset? _rawCodexLongResetAt;
    // Codex 창 길이는 계정/플랜마다 다르다(5시간 · 주간 …). 시간선 마커 위치는 이 길이로 역산한다.
    private TimeSpan _rawCodexShortWindow = TimeSpan.FromHours(5);
    private TimeSpan _rawCodexLongWindow = TimeSpan.FromDays(7);

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
    [ObservableProperty] private bool _isCodexLoading = true;

    // Codex 토큰 4타일을 표시할지 판단하는 실제 기준(로컬 또는 동기화된 다른 기기 토큰 유무).
    // UpdateOverallStatus 가 IsCodexUsageEmpty 를 재계산할 때 이 값을 참조한다.
    private bool _codexHasTokenData;
    [ObservableProperty] private bool _isGeminiUsageEmpty = true;
    [ObservableProperty] private bool _isOpenCodeUsageEmpty = true;

    // IsClaudeSubscribed → ClaudeVm.IsSubscribed
    private string _updateDownloadUrl = "";
    private string _updateSha256Url = "";
    private string _updateVersion = "";
    private string _updateReleaseNotes = "";
    private bool _isUpdateDialogOpen = false;
    public string CurrentVersionLabel => $"v{UpdateService.CurrentVersion.ToString(3)}";

    /// <summary>새 버전을 카운트다운 후 자동 설치할지 (설정).</summary>
    [ObservableProperty] private bool _autoUpdateEnabled = true;

    /// <summary>자동 설치까지의 대기 시간(초, 설정).</summary>
    [ObservableProperty] private int _autoUpdateCountdownSeconds = AppConstants.DefaultAutoUpdateCountdownSeconds;

    /// <summary>마지막 업데이트 확인 시각(로컬). null = 이번 실행에서 아직 확인 결과가 없음.</summary>
    private DateTime? _lastUpdateCheckAt;

    /// <summary>
    /// 마지막 확인 결과 문구를 지연 평가로 보관한다. 완성된 문자열을 캐시해 두면 사용자가 표시 언어를
    /// 바꿨을 때 이 툴팁만 이전 언어로 남는다.
    /// </summary>
    private Func<string>? _lastUpdateCheckStatus;

    /// <summary>푸터 버전 툴팁 — 시작 시 확인이 실제로 돌았는지 사용자가 눈으로 확인할 수 있는 유일한 지점.</summary>
    public string UpdateCheckTooltip =>
        _lastUpdateCheckAt is null || _lastUpdateCheckStatus is null
            ? Loc.UpdateCheckNotYetRun
            : Loc.LastUpdateCheck(_lastUpdateCheckAt.Value.ToString("yyyy-MM-dd HH:mm"), _lastUpdateCheckStatus());

    public string? RawApiResponse { get; private set; }

    // Localized static labels
    public string LblAppTitle        => Loc.AgentUsageTitle;
    public string LblApiQuota        => Loc.ApiQuota;
    public string LblTodayTokens     => Loc.TodayTokens;
    public string LblSessionListEmpty => Loc.SessionListEmpty;
    public string LblFiveHour        => Loc.FiveHourWindow;
    public string LblSevenDay        => Loc.SevenDayWindow;
    public string LblInput           => Loc.Input;
    public string LblOutput          => Loc.Output;
    public string LblCacheRead       => Loc.CacheRead;
    public string LblCacheWrite      => Loc.CacheWrite;
    public string LblTokens          => Loc.Tokens;
    public string LblHistory         => Loc.HistoryTitle;
    public string LblRefresh         => Loc.Refresh;
    public string LblSettings        => Loc.Settings;
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
    public string LblCodexNoUsage    => IsCodexLoading ? Loc.CodexLoading : Loc.CodexNoUsageToday;
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
                         UsageSyncService usageSync,
                         WeatherService weather, WeatherAlertService weatherAlert,
                         OpenCodeWebUsageService? openCodeWebUsage = null)
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
        _usageSync = usageSync;
        _weather = weather;
        _weatherAlert = weatherAlert;

        AntigravityVm = new AntigravityViewModel(antigravity);
        WeatherVm = new WeatherViewModel(weather, weatherAlert);
        OpenCodeVm = new OpenCodeViewModel(openCode, history, openCodeWebUsage);
        GeminiVm = new GeminiViewModel(geminiCli, history);
        CodexVm = new CodexViewModel(codex, history);
        ClaudeVm = new ClaudeViewModel();

        // 계정 전환 자동 감지: credentials 파일 변경 → 새로고침
        _credentials.CredentialsChanged += OnCredentialsChanged;

        // 언어 변경 시 모든 바인딩 갱신.
        // Loc.LanguageChanged 는 static 이벤트라 구독을 놓으면 Dispose 후에도 이 인스턴스가 계속 붙어 있는다.
        // 그러면 종료 중처럼 Application.Current 가 사라진 시점의 언어 변경이 NullReferenceException 이 된다.
        Loc.LanguageChanged += OnLanguageChanged;

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
        AutoUpdateEnabled          = s.AutoUpdateEnabled;
        AutoUpdateCountdownSeconds = ClampAutoUpdateCountdown(s.AutoUpdateCountdownSeconds);
        PollingIntervalMinutes = s.PollingIntervalMinutes;
        UsageSyncEnabled = s.UsageSyncEnabled;
        UsageSyncFolderPath = s.UsageSyncFolderPath ?? "";
        UsageSyncApiSnapshotTtlMinutes = Math.Max(1, s.UsageSyncApiSnapshotTtlMinutes);
        UsageSyncLocalSnapshotTtlHours = Math.Max(1, s.UsageSyncLocalSnapshotTtlHours);
        RefreshUsageSyncStatus();
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
        ShowPlanBadge         = s.ShowPlanBadge;
        ShowAbsoluteResetTime = s.ShowAbsoluteResetTime;
        KeepPopupAboveTaskbar = s.KeepPopupAboveTaskbar;
        UsagePanelOpacity = Math.Clamp(s.UsagePanelOpacity <= 0 ? 0.94 : s.UsagePanelOpacity, 0.5, 1.0);

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
        WeatherForecastSource             = NormalizeForecastSource(s.WeatherForecastSource);
        WeatherForecastModel              = NormalizeForecastModel(s.WeatherForecastModel);

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

    public void RefreshUsageSyncStatus()
    {
        if (!UsageSyncEnabled)
        {
            UsageSyncStatusLabel = Loc.UsageSyncDisabled;
            return;
        }

        if (string.IsNullOrWhiteSpace(UsageSyncFolderPath))
        {
            UsageSyncStatusLabel = Loc.UsageSyncFolderRequired;
            return;
        }

        UsageSyncStatusLabel = System.IO.Directory.Exists(UsageSyncFolderPath)
            ? Loc.UsageSyncReady
            : Loc.UsageSyncFolderWillBeCreated;
    }

    partial void OnUsageSyncEnabledChanged(bool value) => RefreshUsageSyncStatus();

    partial void OnUsageSyncFolderPathChanged(string value) => RefreshUsageSyncStatus();

    // 로딩 상태가 바뀌면 placeholder 문구(조회 중 ↔ 오늘 사용 없음)를 다시 계산한다.
    partial void OnIsCodexLoadingChanged(bool value) => OnPropertyChanged(nameof(LblCodexNoUsage));

    /// <summary>
    /// Reads subscriptionType from local credentials and determines if Claude is a paid plan.
    /// Paid plans (pro, max, team, etc.) should always show the Claude section even at 0% usage.
    /// </summary>
    private void UpdateClaudeSubscription()
    {
        var (subType, rateLimitTier) = _credentials.GetSubscriptionInfo();
        ClaudeVm.IsSubscribed = !string.IsNullOrEmpty(subType)
            && !string.Equals(subType, "free", StringComparison.OrdinalIgnoreCase);
        ClaudePlanLabel = PlanLabels.Claude(subType, rateLimitTier);
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

        // Preserve SkippedVersion / AutoUpdateAttemptedVersion from disk
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
            AutoUpdateAttemptedVersion = existing.AutoUpdateAttemptedVersion,
            AutoUpdateEnabled = AutoUpdateEnabled,
            AutoUpdateCountdownSeconds = ClampAutoUpdateCountdown(AutoUpdateCountdownSeconds),
            PollingIntervalMinutes = PollingIntervalMinutes,
            UsageSyncEnabled = UsageSyncEnabled,
            UsageSyncFolderPath = UsageSyncFolderPath.Trim(),
            UsageSyncApiSnapshotTtlMinutes = Math.Max(1, UsageSyncApiSnapshotTtlMinutes),
            UsageSyncLocalSnapshotTtlHours = Math.Max(1, UsageSyncLocalSnapshotTtlHours),
            TrayDisplayMode = TrayDisplayMode,
            HideInactiveProviders = HideInactiveProviders,
            VisibleProviders = visibleProviders,
            FocusedProvider = FocusedProvider,
            ShowPlanBadge = ShowPlanBadge,
            ShowAbsoluteResetTime = ShowAbsoluteResetTime,
            KeepPopupAboveTaskbar = KeepPopupAboveTaskbar,
            UsagePanelOpacity = UsagePanelOpacity,
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
            WeatherForecastSource             = WeatherForecastSource,
            WeatherForecastModel              = WeatherForecastModel,
        });
    }

    public async Task StartAsync()
    {
        // 이전 자동 적용이 실제로 반영됐다면 루프 방지 표식을 먼저 정리한다.
        ClearCompletedAutoUpdateMarker();

        // 업데이트 확인은 사용량 갱신과 독립적으로 진행한다. RefreshAsync() 뒤에 붙여 두면 모든 공급자와
        // 날씨 조회가 끝날 때까지 확인이 시작조차 못 하고, 그 사이 실패하면 24시간 동안 재시도가 없었다.
        _ = RunStartupUpdateCheckAsync();

        await RefreshAsync();
        _timer.Start();
        _countdownTimer.Start();
        _updateTimer.Start();
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
            RecordUpdateCheck(() => DescribeUpdateCheckError(uex));
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                UpdateCheckLabel = DescribeUpdateCheckError(uex);

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
            var detail = Truncate(ex.Message);
            RecordUpdateCheck(() => $"{Loc.UpdateCheckFailed}: {detail}");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                UpdateCheckLabel = $"{Loc.UpdateCheckFailed}: {detail}";
                await Task.Delay(5000);
                UpdateCheckLabel = "";
            });
            return;
        }

        if (result is null)
        {
            RecordUpdateCheck(() => Loc.AlreadyUpToDate);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                UpdateAvailable = false;
                UpdateCheckLabel = Loc.AlreadyUpToDate;
                await Task.Delay(3000);
                UpdateCheckLabel = "";
            });
            return;
        }

        // 사용자가 직접 확인을 요청했으므로, 이전에 건너뛴 버전이라도 다시 제시한다.
        var manualVersion = result.version.ToString(3);
        var stored = _settingsService.Load();
        if (stored.SkippedVersion == manualVersion)
        {
            stored.SkippedVersion = "";
            _settingsService.Save(stored);
        }

        await ApplyCheckResultAsync(result, showDialog: true);
    }

    /// <summary>
    /// 시작 직후 1회 업데이트 확인. Windows 시작 프로그램으로 부팅 직후 실행되면 네트워크 스택이 아직
    /// 준비되지 않아 첫 요청이 실패하는 일이 잦다 — 여기서 조용히 포기하면 다음 기회가 24시간 뒤가
    /// 되므로, 도달 불가/타임아웃에 한해 백오프 재시도한다.
    /// </summary>
    private async Task RunStartupUpdateCheckAsync()
    {
        await Task.Delay(AppConstants.StartupUpdateCheckDelayMs);

        var retryDelays = AppConstants.StartupUpdateCheckRetryDelaysMs;
        for (int attempt = 0; attempt <= retryDelays.Length; attempt++)
        {
            if (IsUpdating) return;

            UpdateService.UpdateInfo? result;
            try
            {
                result = await _updater.CheckForUpdateAsync();
            }
            catch (UpdateCheckException uex)
            {
                RecordUpdateCheck(() => DescribeUpdateCheckError(uex));

                // rate limit / API 오류는 즉시 재시도해도 같은 응답이다 — 24시간 타이머에 맡긴다.
                bool retryable = uex.Kind is UpdateCheckErrorKind.Network or UpdateCheckErrorKind.Timeout;
                if (!retryable || attempt == retryDelays.Length) return;

                await Task.Delay(retryDelays[attempt]);
                continue;
            }
            catch (Exception ex)
            {
                var detail = Truncate(ex.Message);
                RecordUpdateCheck(() => $"{Loc.UpdateCheckFailed}: {detail}");
                return;
            }

            // 시작 시점은 앱을 재시작해도 사용자의 작업을 끊지 않는 유일한 타이밍이라 모달을 띄운다.
            await ApplyCheckResultAsync(result, showDialog: true);
            return;
        }
    }

    private async Task CheckForUpdateAsync()
    {
        if (IsUpdating) return;

        UpdateService.UpdateInfo? result;
        try
        {
            result = await _updater.CheckForUpdateAsync();
        }
        catch (UpdateCheckException uex)
        {
            RecordUpdateCheck(() => DescribeUpdateCheckError(uex));
            return;
        }
        catch (Exception ex)
        {
            var detail = Truncate(ex.Message);
            RecordUpdateCheck(() => $"{Loc.UpdateCheckFailed}: {detail}");
            return;
        }

        // 주기 확인은 모달을 띄우지 않는다 (v1.33.8): 작업 중인 화면을 갑자기 가로채지 않도록 캐시와
        // 푸터 라벨만 갱신하고, 실제 설치는 사용자가 버전을 클릭하거나 다음 시작 시 진행된다.
        await ApplyCheckResultAsync(result, showDialog: false);
    }

    /// <summary>확인 결과를 캐시·라벨·툴팁에 반영하고, 필요하면 카운트다운 모달을 띄운다.</summary>
    private async Task ApplyCheckResultAsync(UpdateService.UpdateInfo? result, bool showDialog)
    {
        if (result is null)
        {
            RecordUpdateCheck(() => Loc.AlreadyUpToDate);
            await OnUiThreadAsync(() => UpdateAvailable = false);
            return;
        }

        var versionStr = StoreAvailableUpdate(result);
        RecordUpdateCheck(() => Loc.UpdateAvailable($"v{versionStr}"));

        if (_settingsService.Load().SkippedVersion == versionStr) return;

        await OnUiThreadAsync(() =>
        {
            UpdateLabel = Loc.UpdateAvailable($"v{versionStr}");
            UpdateAvailable = true;
            // UpdateLabel 은 어떤 XAML 에도 바인딩돼 있지 않다 — 확인 결과가 화면에 전혀 드러나지 않던
            // 원인이라, 푸터에 실제로 보이는 UpdateCheckLabel 에도 같은 문구를 남긴다.
            UpdateCheckLabel = UpdateLabel;

            if (showDialog)
                ShowUpdateDialogWithCountdown(versionStr, _updateReleaseNotes);
        });
    }

    private static string DescribeUpdateCheckError(UpdateCheckException uex) => uex.Kind switch
    {
        UpdateCheckErrorKind.RateLimit => Loc.UpdateCheckRateLimited(uex.RetryAtLocal ?? ""),
        UpdateCheckErrorKind.Network   => Loc.UpdateCheckNetworkError,
        UpdateCheckErrorKind.Timeout   => Loc.UpdateCheckTimeout,
        UpdateCheckErrorKind.ApiError  => Loc.UpdateCheckApiError(uex.StatusCode ?? 0),
        _ => Loc.UpdateCheckFailed
    };

    private static string Truncate(string text, int max = 80) =>
        text.Length > max ? text[..max] + "…" : text;

    /// <summary>확인 시각과 결과를 기록해 푸터 버전 툴팁에 노출한다.</summary>
    private void RecordUpdateCheck(Func<string> status)
    {
        _lastUpdateCheckAt = DateTime.Now;
        _lastUpdateCheckStatus = status;
        _ = OnUiThreadAsync(() => OnPropertyChanged(nameof(UpdateCheckTooltip)));
    }

    /// <summary>
    /// UI 스레드로 마샬링한다. 시작 시 확인은 대기·재시도까지 최대 수 분간 살아 있어서, 그 사이 사용자가
    /// 앱을 종료하면 <c>Application.Current</c> 가 사라진 뒤 접근하게 된다 — 그때는 조용히 건너뛴다.
    /// </summary>
    private static async Task OnUiThreadAsync(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) return;

        try
        {
            await app.Dispatcher.InvokeAsync(action);
        }
        catch (TaskCanceledException)
        {
            // 대기 중 디스패처가 종료됨 — 종료 경로이므로 무시한다.
        }
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

        ShowUpdateDialogWithCountdown(_updateVersion, _updateReleaseNotes);
        return true;
    }

    /// <summary>
    /// 무인 설치(카운트다운)를 허용할지 결정한다. 정책 자체를 검증할 수 있도록 순수 함수로 분리했다.
    /// </summary>
    /// <param name="canVerify">릴리스에 SHA256 자산이 있어 무결성 검증이 가능한지.</param>
    /// <param name="autoRetryExhausted">이 버전을 이미 자동으로 적용하려다 실패한 적이 있는지.</param>
    /// <returns>카운트다운 초(0 이면 수동 실행만) 와 모달에 띄울 안내 문구(없으면 null).</returns>
    internal static (int seconds, string? notice) ResolveAutoUpdatePlan(
        bool canVerify, bool autoRetryExhausted, bool autoUpdateEnabled, int countdownSeconds)
    {
        // 검증이 불가능하면 사람이 보지 않는 사이에 설치하지 않는다. 이 안내는 자동 설치를 꺼 둔
        // 사용자에게도 유효하다 — 직접 실행할지 판단하는 데 필요한 정보라 먼저 확인한다.
        if (!canVerify) return (0, Loc.UpdateNoChecksumWarning);

        // 사용자가 자동 설치를 껐다면 그대로 따른다. 본인이 고른 동작이므로 별도 안내는 띄우지 않는다.
        if (!autoUpdateEnabled) return (0, null);

        // 자동 적용이 끝내 반영되지 않은 버전이면 자동 재시도 대신 수동 실행을 요구한다 —
        // 그렇지 않으면 매 실행마다 다운로드 → 재시작을 반복하는 루프가 된다.
        if (autoRetryExhausted) return (0, Loc.AutoUpdateRetryManual);

        return (ClampAutoUpdateCountdown(countdownSeconds), null);
    }

    /// <summary>설정값을 허용 범위로 잘라낸다. 0 이하(미설정·구버전 설정 파일)는 기본값으로 본다.</summary>
    internal static int ClampAutoUpdateCountdown(int seconds) => Math.Clamp(
        seconds <= 0 ? AppConstants.DefaultAutoUpdateCountdownSeconds : seconds,
        AppConstants.MinAutoUpdateCountdownSeconds,
        AppConstants.MaxAutoUpdateCountdownSeconds);

    /// <summary>업데이트 모달을 연다. 카운트다운 허용 여부는 <see cref="ResolveAutoUpdatePlan"/> 가 정한다.</summary>
    private void ShowUpdateDialogWithCountdown(string version, string notes)
    {
        var (seconds, notice) = ResolveAutoUpdatePlan(
            canVerify: !string.IsNullOrWhiteSpace(_updateSha256Url),
            autoRetryExhausted: _settingsService.Load().AutoUpdateAttemptedVersion == version,
            autoUpdateEnabled: AutoUpdateEnabled,
            countdownSeconds: AutoUpdateCountdownSeconds);

        ShowUpdateDialog(version, notes, autoUpdateSeconds: seconds, notice: notice);
    }

    private void ShowUpdateDialog(string version, string notes, int autoUpdateSeconds, string? notice)
    {
        if (_isUpdateDialogOpen) return;

        try
        {
            _isUpdateDialogOpen = true;
            var dialog = new Views.UpdateDialog(
                $"v{version}",
                notes,
                onSkip: () => SkipVersion(version),
                autoUpdateSeconds: autoUpdateSeconds);

            if (!string.IsNullOrEmpty(notice))
                dialog.ShowError(notice);

            dialog.OnUpdateRequested += () =>
            {
                // 카운트다운 만료로 시작된 자동 적용만 기록한다. 사용자가 직접 누른 경우는 실패해도
                // 루프가 아니므로 표식을 남기지 않는다.
                if (dialog.StartedAutomatically) RecordAutoUpdateAttempt(version);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        IsUpdating = true;
                        var tempPath = await _updater.DownloadAndPrepareUpdateAsync(
                            _updateDownloadUrl, _updateSha256Url,
                            // 이중 방어: 카운트다운으로 시작된 무인 설치는 검증 자산이 없으면 서비스가 거부한다.
                            // 사용자가 경고를 보고 직접 누른 경우에만 검증 없이 진행한다.
                            allowUnverified: !dialog.StartedAutomatically,
                            onProgress: (pc, status) =>
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
        UpdateCheckLabel = "";
    }

    /// <summary>자동(카운트다운) 적용을 시도한 버전을 기록해 다음 실행의 무한 재시도를 막는다.</summary>
    private void RecordAutoUpdateAttempt(string version)
    {
        var settings = _settingsService.Load();
        if (settings.AutoUpdateAttemptedVersion == version) return;

        settings.AutoUpdateAttemptedVersion = version;
        _settingsService.Save(settings);
    }

    /// <summary>
    /// 자동 적용이 실제로 반영됐으면(현재 버전 >= 기록된 시도 버전) 루프 방지 표식을 지운다.
    /// 남겨 두면 이후 새 버전과 비교가 어긋나 자동 적용이 영구히 비활성화된다.
    /// </summary>
    private void ClearCompletedAutoUpdateMarker()
    {
        var settings = _settingsService.Load();
        var attempted = settings.AutoUpdateAttemptedVersion;
        if (string.IsNullOrEmpty(attempted)) return;

        // 파싱 불가한 값은 잔재로 보고 정리한다.
        if (Version.TryParse(attempted, out var attemptedVersion) &&
            attemptedVersion > UpdateService.CurrentVersion)
            return;

        settings.AutoUpdateAttemptedVersion = "";
        _settingsService.Save(settings);
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
            report = await _weather.GetForecastAsync(location, WeatherForecastSource, WeatherForecastModel);
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
    private bool _openCodeHasPeriodUsage = false;

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
        IsOpenCodeActive = IsOpenCodeSectionActive(
            IsOpenCodeEnabled, hideInactive, _lastOpenCodeRequestCount, _openCodeHasPeriodUsage,
            OpenCodeVm.HasWebQuota, OpenCodeVm.HasStaleSyncedQuota, OpenCodeHasError);

        ClaudeVm.IsUsageEmpty = TodayInputTokens + TodayOutputTokens == 0;
        IsCodexUsageEmpty = !_codexHasTokenData;
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

        // Gemini CLI는 공급자 할당량이 없어 최근 7일 최대치 대비 비율을 트레이 참고값으로 쓴다.
        // OpenCode는 같은 상대값이 실제 할당량처럼 오해되므로 만들지 않는다.
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
                // 미조회 상태에서 "Claude 0%" 라고 쓰면 여유가 100% 라는 뜻으로 읽힌다
                _ => ClaudeVm.HasQuotaData
                        ? Loc.TrayStatusClaude(ClaudeVm.ShortPercent)
                        : $"Claude {Loc.QuotaUnknownMark}"
            };
        }
    }

    private bool IsUsageSyncReady =>
        UsageSyncEnabled && !string.IsNullOrWhiteSpace(UsageSyncFolderPath);

    private TimeSpan UsageSyncApiTtl =>
        TimeSpan.FromMinutes(Math.Clamp(UsageSyncApiSnapshotTtlMinutes, 1, 60));

    private TimeSpan UsageSyncLocalTtl =>
        TimeSpan.FromHours(Math.Clamp(UsageSyncLocalSnapshotTtlHours, 1, 168));

    private TimeSpan UsageSyncQuotaTtl(string provider) =>
        UsageSyncQuotaTtl(provider, UsageSyncApiSnapshotTtlMinutes);

    internal static TimeSpan UsageSyncQuotaTtl(string provider, int configuredMinutes) =>
        provider == UsageProviderKind.OpenCode
            ? TimeSpan.FromMinutes(Math.Max(
                Math.Clamp(configuredMinutes, 1, 60),
                OpenCodeWebUsageService.CachedFallbackMaxAge.TotalMinutes))
            : TimeSpan.FromMinutes(Math.Clamp(configuredMinutes, 1, 60));

    private UsageSyncSnapshot? TrySyncClaudeUsage(
        string? accountKey,
        UsageResponse? usage,
        SessionStats sessionStats,
        string errorKind,
        out UsageSyncMergedLocalTotals? mergedTotals)
    {
        mergedTotals = null;
        if (!IsUsageSyncReady)
            return null;

        try
        {
            var snapshot = _usageSync.CreateSnapshot(
                UsageProviderKind.Claude,
                accountKey,
                CreateClaudeQuotaSnapshot(usage),
                CreateLocalTotals(sessionStats),
                errorKind);
            _usageSync.WriteSnapshot(UsageSyncFolderPath, snapshot);

            var today = DateOnly.FromDateTime(DateTime.Now);
            var read = _usageSync.ReadSnapshots(UsageSyncFolderPath, UsageProviderKind.Claude, accountKey, today);
            mergedTotals = _usageSync.MergeLocalTotals(read.Snapshots, UsageSyncLocalTtl);
            UsageSyncStatusLabel = Loc.UsageSyncReady;
            return _usageSync.SelectNewestQuotaSnapshot(read.Snapshots, UsageSyncApiTtl);
        }
        catch (Exception ex)
        {
            UsageSyncStatusLabel = Loc.UsageSyncFailed(ex.Message);
#if DEBUG
            Debug.WriteLine($"[MainViewModel] Claude usage sync failed: {ex}");
#endif
            return null;
        }
    }

    /// <summary>
    /// provider 한 개의 스냅샷을 공유 폴더에 쓰고, 다른 PC 것까지 읽어 합산 토큰과 최신 할당량을 돌려준다.
    /// Claude 와 달리 계정 키를 두지 않는다 — 이 provider 들은 로컬 로그만 보고 계정을 특정할 수 없다.
    /// </summary>
    private UsageSyncProviderResult TrySyncProviderSnapshot(
        string provider,
        ProviderUsageSnapshot snapshot)
    {
        if (!IsUsageSyncReady)
            return UsageSyncProviderResult.Empty;

        try
        {
            var localSnapshot = _usageSync.CreateSnapshot(
                provider,
                null,
                CreateProviderQuotaSnapshot(provider, snapshot),
                CreateLocalTotals(snapshot),
                ClassifyProviderError(snapshot.ErrorMessage));
            _usageSync.WriteSnapshot(UsageSyncFolderPath, localSnapshot);

            var today = DateOnly.FromDateTime(DateTime.Now);
            var read = _usageSync.ReadSnapshots(UsageSyncFolderPath, provider, null, today);
            UsageSyncStatusLabel = Loc.UsageSyncReady;
            var sharesQuota = UsageSyncSharesAccountQuota(provider);
            return new UsageSyncProviderResult(
                _usageSync.MergeLocalTotals(read.Snapshots, UsageSyncLocalTtl),
                // 할당량은 합산하지 않는다 — 계정 단위 값이라 가장 최근에 관측한 PC 것을 쓴다.
                // 기기별로 계산되는 Gemini percent 는 쓰지 않는다. OpenCode 는 공식 웹 할당량만 공유한다.
                sharesQuota
                    ? _usageSync.SelectNewestQuotaSnapshot(read.Snapshots, UsageSyncQuotaTtl(provider))
                    : null,
                // 유효시간이 지나 게이지로는 못 쓰더라도 "다른 PC 가 오늘 관측하긴 했다" 는 사실은 남는다.
                // 이걸 알아야 로그인이 풀린 것과 갱신이 늦는 것을 화면에서 구분할 수 있다.
                // 스냅샷 자체가 당일치라 하루보다 긴 유효시간은 의미가 없다.
                sharesQuota
                    ? _usageSync.SelectNewestQuotaSnapshot(read.Snapshots, TimeSpan.FromDays(1))
                    : null);
        }
        catch (Exception ex)
        {
            UsageSyncStatusLabel = Loc.UsageSyncFailed(ex.Message);
#if DEBUG
            Debug.WriteLine($"[MainViewModel] {provider} usage sync failed: {ex}");
#endif
            return UsageSyncProviderResult.Empty;
        }
    }

    /// <summary>
    /// 이 provider 의 할당량이 "계정 단위"라서 기기 간에 공유해도 되는가.
    /// Codex·Antigravity·OpenCode 공식 웹 할당량은 서버가 계정별로 내려주므로 어느 PC 에서 봐도 같은 값이다.
    /// Gemini CLI 의 percent 는 그 PC 의 로컬 토큰 합계를 그 PC 의 최근 최대치로 나눈 값이라
    /// 다른 PC 의 값을 가져다 쓰면 남의 기기 기준을 내 화면에 표시하는 셈이 된다 — 대신 합산 토큰으로 다시 계산한다.
    /// </summary>
    internal static bool UsageSyncSharesAccountQuota(string provider) =>
        provider is UsageProviderKind.Codex or UsageProviderKind.Antigravity or UsageProviderKind.OpenCode;

    /// <summary>
    /// 동기화 1회 결과 — 기기 합산 토큰과, 계정 단위 provider 라면 가장 최신 할당량.
    /// <paramref name="LastObservedQuota"/> 는 유효시간을 무시한 오늘의 최신 관측으로,
    /// 화면에 값을 그리는 용도가 아니라 "언제 마지막으로 관측됐는지" 안내에만 쓴다.
    /// </summary>
    private readonly record struct UsageSyncProviderResult(
        UsageSyncMergedLocalTotals? MergedTotals,
        UsageSyncSnapshot? RemoteQuota,
        UsageSyncSnapshot? LastObservedQuota = null)
    {
        public static UsageSyncProviderResult Empty => new(null, null, null);
    }

    private UsageSyncQuotaSnapshot? CreateClaudeQuotaSnapshot(UsageResponse? usage)
    {
        if (usage?.FiveHour is null && usage?.SevenDay is null)
            return null;

        var extra = usage.ExtraUsage;
        var extraHasLimit = extra?.MonthlyLimit.HasValue == true;
        var extraUsagePercent = extraHasLimit
            ? Math.Min(1.0, (extra?.Utilization ?? 0) / 100.0)
            : (double?)null;
        var extraCreditsLabel = extra is { UsedCredits: not null, MonthlyLimit: not null }
            ? Loc.ExtraCredits(extra.UsedCredits.Value, extra.MonthlyLimit.Value)
            : extra?.UsedCredits is double usedCredits
                ? Loc.ExtraCreditsUsedOnly(usedCredits)
                : "";

        return new UsageSyncQuotaSnapshot
        {
            HasData = usage.FiveHour is not null || usage.SevenDay is not null,
            ShortUsagePercent = usage.FiveHour?.UsagePercent ?? 0,
            ShortResetAt = usage.FiveHour?.ResetsAtParsed,
            LongUsagePercent = usage.SevenDay?.UsagePercent ?? 0,
            LongResetAt = usage.SevenDay?.ResetsAtParsed,
            ExtraUsageEnabled = extra?.IsEnabled == true,
            ExtraHasLimit = extra?.IsEnabled == true && extraHasLimit,
            ExtraUsagePercent = extraUsagePercent,
            ExtraCreditsLabel = extra?.IsEnabled == true ? extraCreditsLabel : "",
        };
    }

    internal static UsageSyncQuotaSnapshot? CreateProviderQuotaSnapshot(string provider, ProviderUsageSnapshot snapshot)
    {
        if (provider == UsageProviderKind.OpenCode)
        {
            var usage = snapshot.OpenCodeDetails?.WebUsage;
            if (usage is null)
                return null;

            return new UsageSyncQuotaSnapshot
            {
                HasData = true,
                ObservedAtUtc = usage.ObservedAtUtc,
                OpenCode = new UsageSyncOpenCodeQuota
                {
                    Rolling = CreateOpenCodeQuotaWindow(usage.Rolling),
                    Weekly = CreateOpenCodeQuotaWindow(usage.Weekly),
                    Monthly = CreateOpenCodeQuotaWindow(usage.Monthly),
                },
            };
        }

        // 기기 단위로 계산되는 percent 는 공유하지 않는다 — 남의 PC 기준이 내 화면에 뜨는 것을 막는다.
        if (!UsageSyncSharesAccountQuota(provider))
            return null;

        // 리셋 시각이 있어야 창을 특정할 수 있다. 사용률만 있고 창을 모르면 시간선을 그릴 수 없고,
        // 받는 쪽에서 "지금 창의 값"인지도 확인할 수 없으므로 공유하지 않는다.
        if (snapshot.ShortResetAt is null && snapshot.LongResetAt is null)
            return null;

        return new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortUsagePercent = snapshot.ShortUsagePercent,
            ShortResetAt = snapshot.ShortResetAt,
            IsShortResetEstimated = snapshot.IsShortResetEstimated,
            ShortWindowMinutes = snapshot.ShortWindowMinutes,
            LongUsagePercent = snapshot.LongUsagePercent,
            LongResetAt = snapshot.LongResetAt,
            LongWindowMinutes = snapshot.LongWindowMinutes,
            HasLongWindow = snapshot.LongWindowMinutes is not null || snapshot.LongResetAt is not null,
            PlanType = snapshot.PlanType ?? "",
        };
    }

    private static UsageSyncOpenCodeQuotaWindow CreateOpenCodeQuotaWindow(OpenCodeQuotaWindow window) =>
        new() { UsagePercent = window.UsagePercent, ResetAt = window.ResetAt };

    internal static OpenCodeWebUsage? CreateOpenCodeWebUsage(
        UsageSyncQuotaSnapshot? quota,
        DateTimeOffset? now = null)
    {
        var usage = quota?.OpenCode;
        if (usage is null || usage.Rolling.ResetAt <= (now ?? DateTimeOffset.Now))
            return null;

        return new OpenCodeWebUsage
        {
            ObservedAtUtc = quota!.ObservedAtUtc,
            Rolling = CreateOpenCodeQuotaWindow(usage.Rolling),
            Weekly = CreateOpenCodeQuotaWindow(usage.Weekly),
            Monthly = CreateOpenCodeQuotaWindow(usage.Monthly),
        };
    }

    private static OpenCodeQuotaWindow CreateOpenCodeQuotaWindow(UsageSyncOpenCodeQuotaWindow window) =>
        new() { UsagePercent = window.UsagePercent, ResetAt = window.ResetAt };

    private static UsageSyncLocalTotals CreateLocalTotals(SessionStats stats) =>
        new()
        {
            InputTokens = stats.TotalInputTokens,
            OutputTokens = stats.TotalOutputTokens,
            CacheReadTokens = stats.TotalCacheReadTokens,
            CacheWriteTokens = stats.TotalCacheWriteTokens,
            SessionCount = stats.SessionCount,
            HourlyTokens = CopyHourlyTokens(stats.HourlyTokens),
        };

    private static UsageSyncLocalTotals CreateLocalTotals(ProviderUsageSnapshot snapshot) =>
        new()
        {
            InputTokens = snapshot.TotalInputTokens,
            OutputTokens = snapshot.TotalOutputTokens,
            CacheReadTokens = snapshot.TotalCacheReadTokens,
            CacheWriteTokens = snapshot.TotalCacheWriteTokens,
            SessionCount = snapshot.SessionCount,
            RequestCount = snapshot.RequestCount,
            HourlyTokens = CopyHourlyTokens(snapshot.HourlyTokens),
        };

    private static long[] CopyHourlyTokens(long[] source)
    {
        var copy = new long[24];
        for (var i = 0; i < copy.Length && i < source.Length; i++)
        {
            copy[i] = source[i];
        }

        return copy;
    }

    private void ApplySyncedClaudeQuota(UsageSyncSnapshot snapshot)
    {
        if (snapshot.Quota is not { HasData: true } quota)
            return;

        ClaudeVm.HasError = false;
        ClaudeVm.ErrorMessage = "";
        ClaudeVm.HasQuotaData = true;

        ClaudeVm.ShortPercent = quota.ShortUsagePercent;
        _rawClaudeShortResetAt = quota.ShortResetAt;
        ClaudeVm.ShortReset = FormatResetLabel(quota.ShortResetAt, quota.IsShortResetEstimated);
        ClaudeVm.ShortSummary = Loc.UsageSummary(quota.ShortUsagePercent);
        ClaudeVm.ShortDepletion = "";
        ShortUsagePercent = quota.ShortUsagePercent;
        ShortResetLabel = ClaudeVm.ShortReset;
        _lastKnownShortPercent = quota.ShortUsagePercent;
        _lastKnownShortReset = ClaudeVm.ShortReset;

        ClaudeVm.LongPercent = quota.LongUsagePercent;
        _rawClaudeLongResetAt = quota.LongResetAt;
        ClaudeVm.LongReset = FormatResetLabel(quota.LongResetAt);
        ClaudeVm.LongSummary = Loc.UsageSummary(quota.LongUsagePercent);
        ClaudeVm.LongDepletion = "";
        LongUsagePercent = quota.LongUsagePercent;
        LongResetLabel = ClaudeVm.LongReset;
        _lastKnownLongPercent = quota.LongUsagePercent;
        _lastKnownLongReset = ClaudeVm.LongReset;

        SetClaudeExtraUsage(
            quota.ExtraUsageEnabled,
            quota.ExtraHasLimit,
            quota.ExtraUsagePercent ?? 0,
            quota.ExtraCreditsLabel);
        SetClaudeExtraOnlyMode(ExtraUsageEnabled && ClaudeVm.ShortPercent >= 1.0);

        var quotaObservedAt = quota.ObservedAtUtc ?? snapshot.ObservedAtUtc;
        ClaudeVm.ApiNote = Loc.UsageSyncQuotaFromDevice(
            snapshot.DeviceName,
            quotaObservedAt.ToLocalTime().ToString("HH:mm"));
        StatusText = $"{ClaudeVm.ShortPercent:P0} used";
    }

    /// <summary>이 PC 가 "지금 열려 있는" Codex 창을 실제로 알고 있는지.</summary>
    private bool HasLiveCodexQuota() =>
        _rawCodexShortResetAt > DateTimeOffset.Now || _rawCodexLongResetAt > DateTimeOffset.Now;

    /// <summary>
    /// 다른 PC 가 관측한 Codex 할당량을 이 화면에 반영한다.
    /// 창 길이(window_minutes)까지 함께 옮겨야 시간선 마커가 제자리에 선다 —
    /// 5시간으로 가정하면 주간 창 계정에서 위치가 통째로 어긋난다.
    /// </summary>
    private void ApplySyncedCodexQuota(UsageSyncSnapshot snapshot)
    {
        if (snapshot.Quota is not { HasData: true } quota)
            return;

        CodexHasError = false;
        CodexErrorMessage = "";

        CodexPercent = quota.ShortUsagePercent;
        _prevCodexPercent = quota.ShortUsagePercent;
        _rawCodexShortResetAt = quota.ShortResetAt;
        _rawCodexShortResetEstimated = quota.IsShortResetEstimated;
        CodexReset = FormatResetLabel(quota.ShortResetAt, quota.IsShortResetEstimated);
        CodexSummary = Loc.UsageSummary(quota.ShortUsagePercent);
        _rawCodexShortWindow = UsageCalculator.WindowSpan(quota.ShortWindowMinutes, TimeSpan.FromHours(5));
        CodexShortWindowLabel = Loc.CodexWindowLabel(quota.ShortWindowMinutes);

        CodexLongPercent = quota.LongUsagePercent;
        _rawCodexLongResetAt = quota.LongResetAt;
        CodexLongReset = FormatResetLabel(quota.LongResetAt);
        CodexLongSummary = Loc.UsageSummary(quota.LongUsagePercent);
        _rawCodexLongWindow = UsageCalculator.WindowSpan(quota.LongWindowMinutes, TimeSpan.FromDays(7));
        CodexLongWindowLabel = Loc.CodexWindowLabel(quota.LongWindowMinutes);
        IsCodexLongVisible = quota.HasLongWindow;

        if (PlanLabels.Codex(quota.PlanType) is { Length: > 0 } planLabel)
            CodexPlanLabel = planLabel;

        CodexDataSource = Loc.UsageSyncQuotaFromDevice(
            snapshot.DeviceName,
            (quota.ObservedAtUtc ?? snapshot.ObservedAtUtc).ToLocalTime().ToString("HH:mm"));
    }

    private static string ClassifyClaudeApiError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "";
        if (error == UsageApiService.NoTokenError)
            return "missing_token";
        if (error.Contains("429", StringComparison.OrdinalIgnoreCase))
            return "rate_limit";
        if (error.Contains("403", StringComparison.OrdinalIgnoreCase))
            return "permission";
        return "api_error";
    }

    private static string ClassifyProviderError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "";
        if (UsageCalculator.IsNoUsageInformational(error, UsageProviderKind.Codex) ||
            UsageCalculator.IsNoUsageInformational(error, UsageProviderKind.GeminiCli) ||
            UsageCalculator.IsNoUsageInformational(error, UsageProviderKind.OpenCode))
            return "no_usage";
        return "source_error";
    }

    private static string TokenOrDash(long tokens) =>
        tokens > 0 ? UsageCalculator.FormatTokenShort(tokens) : "—";

    private static string RequestCountOrDash(int count) =>
        count > 0
            ? Loc.CurrentLang == "ko" ? $"{count}회" : $"{count} req"
            : "—";

    private static string WithSyncNote(string text, UsageSyncMergedLocalTotals? merged)
    {
        if (merged is not { DeviceCount: > 1 })
            return text;

        var note = Loc.UsageSyncMergedDevices(merged.DeviceCount);
        return string.IsNullOrWhiteSpace(text) ? note : $"{text} · {note}";
    }

    /// <summary>
    /// 병합 합계를 화면 값으로 쓸지 여부.
    /// DeviceCount 는 "사용량이 있는 기기" 수라, &gt; 1 을 요구하면 이 PC 에 로컬 사용량이 없을 때
    /// (예: OpenCode 를 다른 PC 에서만 쓰는 경우) 다른 PC 값이 통째로 버려져
    /// HideInactiveProviders 와 맞물려 공급자 섹션이 아예 사라진다.
    /// 기기가 1대뿐이고 그게 이 PC 라면 병합값 = 로컬값이므로 그대로 써도 결과가 같다.
    /// </summary>
    internal static bool HasMergedDeviceTotals(UsageSyncMergedLocalTotals? merged) =>
        merged is { HasData: true };

    /// <summary>
    /// OpenCode 섹션을 화면에 남길지 여부.
    /// 동기화로 받아 온 공식 할당량(<paramref name="hasWebQuota"/>)과, 그 값이 시효를 넘겨 갱신을 기다리는
    /// 중이라는 안내(<paramref name="hasStaleSyncedQuota"/>)도 "보여 줄 것이 있다" 로 친다.
    /// 이 PC 에서 OpenCode 를 쓰지 않는 사용자는 오늘 토큰도 이번 달 기록도 없어 나머지 조건이 모두 거짓이라,
    /// 이 둘을 빼면 다른 PC 의 게이지를 손에 들고도 HideInactiveProviders 와 맞물려 섹션이 통째로 사라진다.
    /// </summary>
    internal static bool IsOpenCodeSectionActive(
        bool isEnabled,
        bool hideInactive,
        int requestCount,
        bool hasPeriodUsage,
        bool hasWebQuota,
        bool hasStaleSyncedQuota,
        bool hasError) =>
        isEnabled && (!hideInactive
            || requestCount > 0
            || hasPeriodUsage
            || hasWebQuota
            || hasStaleSyncedQuota
            || hasError);

    /// <summary>
    /// Gemini CLI 처럼 서버 할당량이 없어 "최근 최대 사용일" 대비로 막대를 그리는 provider 의
    /// 진행률을, 기기 합산 토큰 기준으로 다시 계산한다. 각 모니터의 계산식(출력 토큰 / 목표치)과 같아야 한다.
    /// </summary>
    private double MergedGoalPercent(string provider, long outputTokens)
    {
        var goal = Math.Max(10000, _history.GetRecentMaxTotalTokens(provider, null, 7));
        return Math.Clamp(outputTokens / (double)goal, 0, 1);
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
            // 트랜스크립트 스캔은 파일 I/O라 수백 ms 이상 걸린다. RefreshAsync 는 UI 스레드에서
            // 시작될 수 있으므로(계정 전환 직후 등) 반드시 스레드풀로 밀어낸다.
            var sessionStats = await Task.Run(_session.ScanTodayUsage);
            var syncErrorKind = skipApi ? "api_skipped" : ClassifyClaudeApiError(_api.LastError);
            var syncedClaudeQuota = TrySyncClaudeUsage(
                currentOrgUuid,
                usage,
                sessionStats,
                syncErrorKind,
                out var mergedClaudeTotals);
            var hasMergedClaudeTotals = HasMergedDeviceTotals(mergedClaudeTotals);
            var displayInputTokens = hasMergedClaudeTotals ? mergedClaudeTotals!.InputTokens : sessionStats.TotalInputTokens;
            var displayOutputTokens = hasMergedClaudeTotals ? mergedClaudeTotals!.OutputTokens : sessionStats.TotalOutputTokens;
            var displayCacheReadTokens = hasMergedClaudeTotals ? mergedClaudeTotals!.CacheReadTokens : sessionStats.TotalCacheReadTokens;
            var displayCacheWriteTokens = hasMergedClaudeTotals ? mergedClaudeTotals!.CacheWriteTokens : sessionStats.TotalCacheWriteTokens;
            var displaySessionCount = hasMergedClaudeTotals ? mergedClaudeTotals!.SessionCount : sessionStats.SessionCount;
            var displayHourlyTokens = hasMergedClaudeTotals ? mergedClaudeTotals!.HourlyTokens : sessionStats.HourlyTokens;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TodayInputTokens  = displayInputTokens;
                TodayOutputTokens = displayOutputTokens;
                TodayCacheRead    = displayCacheReadTokens;
                TodayCacheWrite   = displayCacheWriteTokens;
                SessionsLabel     = Loc.Sessions(displaySessionCount);
                ApplySessionList(sessionStats.Sessions, displaySessionCount);

                _history.RecordToday(displayInputTokens, displayOutputTokens,
                    displayCacheReadTokens, displayCacheWriteTokens,
                    displaySessionCount);
                
                // 히스토리와 시간대별 차트는 항상 Claude 기준
                HistoryData = _history.GetLast(7);
                HourlyTokens = displayHourlyTokens;

                TodayCostLabel = CalcCostLabel(displayInputTokens,
                    displayOutputTokens,
                    displayCacheReadTokens,
                    displayCacheWriteTokens);
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
                    ClaudeVm.HasQuotaData = true;
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

                    ClaudeVm.ApiNote = WithSyncNote(ClaudeVm.ApiNote, mergedClaudeTotals);
                }
                else if (syncedClaudeQuota?.Quota is { HasData: true })
                {
                    ApplySyncedClaudeQuota(syncedClaudeQuota);
                    ClaudeVm.ApiNote = WithSyncNote(ClaudeVm.ApiNote, mergedClaudeTotals);
                }
                else if (skipApi || _api.LastError != null)
                {
                    // 마지막으로 성공한 값이 있으면 유지하고, 한 번도 없으면 0% 로 단정하지 않는다.
                    ClaudeVm.HasQuotaData = _lastKnownShortPercent.HasValue || _lastKnownLongPercent.HasValue;

                    ClaudeVm.ShortPercent = _lastKnownShortPercent ?? 0;
                    ClaudeVm.ShortReset   = _lastKnownShortReset;
                    ClaudeVm.ShortSummary = _lastKnownShortPercent is { } shortPct
                        ? Loc.UsageSummary(shortPct)
                        : Loc.UsageSummaryUnknown;
                    ClaudeVm.LongPercent  = _lastKnownLongPercent ?? 0;
                    ClaudeVm.LongReset    = _lastKnownLongReset;
                    ClaudeVm.LongSummary  = _lastKnownLongPercent is { } longPct
                        ? Loc.UsageSummary(longPct)
                        : Loc.UsageSummaryUnknown;

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

                // 사용량·리셋 시각 반영 직후 시간 진행률(마커/초과 폭)을 함께 갱신 — 성공·캐시 폴백 모두 커버
                RecomputeClaudeTimeProgress(DateTimeOffset.Now);
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

        var sync = TrySyncProviderSnapshot(UsageProviderKind.Codex, CodexVm.LastSnapshot);
        var mergedTotals = sync.MergedTotals;

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
            _rawCodexShortWindow = UsageCalculator.WindowSpan(CodexVm.RawShortWindowMinutes, TimeSpan.FromHours(5));
            _rawCodexLongWindow = UsageCalculator.WindowSpan(CodexVm.RawLongWindowMinutes, TimeSpan.FromDays(7));
            CodexLongReset = CodexVm.LongReset;
            CodexLongSummary = CodexVm.LongSummary;
            IsCodexLongVisible = CodexVm.IsLongVisible;
            CodexPlanLabel = CodexVm.PlanLabel;
            CodexShortWindowLabel = CodexVm.ShortWindowLabel;
            CodexLongWindowLabel = CodexVm.LongWindowLabel;

            // 이 PC 에 지금 창을 설명하는 데이터가 없으면(오늘 요청이 없거나 로그의 창이 이미 끝남)
            // 다른 PC 가 올린 최신 할당량으로 채운다. Codex 의 rate_limits 는 계정 단위라 그대로 옮겨도 맞다.
            if (!HasLiveCodexQuota() && sync.RemoteQuota is { Quota.HasData: true } remoteQuota)
                ApplySyncedCodexQuota(remoteQuota);

            RecomputeCodexTimeProgress(DateTimeOffset.Now);
            CodexInputLabel = CodexVm.InputLabel;
            CodexOutputLabel = CodexVm.OutputLabel;
            CodexCacheReadLabel = CodexVm.CacheReadLabel;
            CodexCacheWriteLabel = CodexVm.CacheWriteLabel;

            // 토큰 4타일 표시 여부는 퍼센트가 아니라 실제 토큰 데이터 유무로 판단한다(Claude 와 동일 기준).
            var codexHasTokenData = CodexVm.LastSnapshot.HasData;
            CodexNote = WithSyncNote(Loc.ProviderCodexNote, mergedTotals);

            if (HasMergedDeviceTotals(mergedTotals))
            {
                CodexInputLabel = TokenOrDash(mergedTotals!.InputTokens);
                CodexOutputLabel = TokenOrDash(mergedTotals.OutputTokens);
                CodexCacheReadLabel = TokenOrDash(mergedTotals.CacheReadTokens);
                CodexCacheWriteLabel = TokenOrDash(mergedTotals.CacheWriteTokens);
                CodexDataSource = WithSyncNote(CodexDataSource, mergedTotals);
                codexHasTokenData = true;
            }

            _codexHasTokenData = codexHasTokenData;
            IsCodexUsageEmpty = !codexHasTokenData;
            IsCodexLoading = false;

            UpdateOverallStatus();
        });
    }

    private async Task RefreshGeminiCliInternalAsync()
    {
        await GeminiVm.RefreshAsync();

        var mergedTotals = TrySyncProviderSnapshot(UsageProviderKind.GeminiCli, GeminiVm.LastSnapshot).MergedTotals;

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
            GeminiNote = WithSyncNote(Loc.ProviderGeminiCliNote, mergedTotals);

            if (HasMergedDeviceTotals(mergedTotals))
            {
                GeminiRequestsLabel = RequestCountOrDash(mergedTotals!.RequestCount);
                GeminiInputLabel = TokenOrDash(mergedTotals.InputTokens);
                GeminiOutputTokensLabel = TokenOrDash(mergedTotals.OutputTokens);
                GeminiCacheReadLabel = TokenOrDash(mergedTotals.CacheReadTokens);
                GeminiCacheWriteLabel = TokenOrDash(mergedTotals.CacheWriteTokens);
                GeminiSummary = Loc.GeminiCliRequestSummary(mergedTotals.RequestCount, mergedTotals.OutputTokens);
                IsGeminiUsageEmpty = false;
                _lastGeminiRequestCount = mergedTotals.RequestCount;
                _lastGeminiOutputTokens = mergedTotals.OutputTokens;
                // 막대도 합산 기준으로 다시 계산한다. 안 하면 숫자는 여러 PC 합인데 막대만 이 PC 몫이라 어긋난다.
                GeminiPercent = MergedGoalPercent(UsageProviderKind.GeminiCli, mergedTotals.OutputTokens);
                _prevGeminiPercent = GeminiPercent;
            }

            UpdateOverallStatus();
        });
    }

    private async Task RefreshOpenCodeInternalAsync()
    {
        await OpenCodeVm.RefreshAsync();

        var syncResult = TrySyncProviderSnapshot(UsageProviderKind.OpenCode, OpenCodeVm.LastSnapshot);
        var mergedTotals = syncResult.MergedTotals;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!OpenCodeVm.HasWebQuota && CreateOpenCodeWebUsage(syncResult.RemoteQuota?.Quota) is { } syncedUsage)
                OpenCodeVm.ApplySyncedWebUsage(syncedUsage);

            ApplyOpenCodeSyncedQuotaNotice(syncResult.LastObservedQuota);

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
            _openCodeHasPeriodUsage = OpenCodeVm.HasPeriodUsage;
            OpenCodeNote = WithSyncNote(OpenCodeVm.Note, mergedTotals);
            OpenCodePlanLabel = OpenCodeVm.PlanLabel;

            if (HasMergedDeviceTotals(mergedTotals))
            {
                OpenCodeRequestCountLabel = RequestCountOrDash(mergedTotals!.RequestCount);
                OpenCodeInputLabel = TokenOrDash(mergedTotals.InputTokens);
                OpenCodeOutputLabel = TokenOrDash(mergedTotals.OutputTokens);
                OpenCodeCacheReadLabel = TokenOrDash(mergedTotals.CacheReadTokens);
                OpenCodeCacheWriteLabel = TokenOrDash(mergedTotals.CacheWriteTokens);
                OpenCodeSummary = Loc.CurrentLang == "ko"
                    ? $"오늘 {mergedTotals.RequestCount}회 · 입력 {UsageCalculator.FormatTokenShort(mergedTotals.InputTokens)} · 출력 {UsageCalculator.FormatTokenShort(mergedTotals.OutputTokens)}"
                    : $"Today {mergedTotals.RequestCount} req · in {UsageCalculator.FormatTokenShort(mergedTotals.InputTokens)} · out {UsageCalculator.FormatTokenShort(mergedTotals.OutputTokens)}";
                IsOpenCodeUsageEmpty = false;
                _lastOpenCodeRequestCount = mergedTotals.RequestCount;
                _lastOpenCodeInputTokens = mergedTotals.InputTokens;
                _lastOpenCodeOutputTokens = mergedTotals.OutputTokens;
                OpenCodePercent = OpenCodeVm.HasWebQuota ? OpenCodeVm.Percent : 0;
            }

            UpdateOverallStatus();
        });
    }

    /// <summary>
    /// 공식 값을 못 그리는 상태에서, 다른 PC 가 오늘 관측한 이력이 있으면 그 시각을 안내한다.
    /// OpenCode 를 이 PC 에서 쓰지 않는 사용자에게 로그인 버튼을 들이밀지 않기 위한 구분이다.
    /// 관측 이력이 없거나(=처음 쓰는 계정) 이 PC 가 직접 읽어냈으면 안내를 지우고 종전 동작으로 돌아간다.
    /// </summary>
    private void ApplyOpenCodeSyncedQuotaNotice(UsageSyncSnapshot? lastObserved)
    {
        if (OpenCodeVm.HasWebQuota || lastObserved?.Quota is not { HasData: true } quota)
        {
            OpenCodeVm.ClearStaleSyncedQuotaNotice();
            return;
        }

        // 이 PC 가 남긴 관측을 근거로 "다른 PC 대기 중" 이라 안내하면 앞뒤가 안 맞는다.
        if (string.Equals(lastObserved.DeviceId, _usageSync.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            OpenCodeVm.ClearStaleSyncedQuotaNotice();
            return;
        }

        OpenCodeVm.ApplyStaleSyncedQuotaNotice(
            lastObserved.DeviceName,
            quota.ObservedAtUtc ?? lastObserved.ObservedAtUtc);
    }

    /// <summary>"오늘 N개 세션" 라벨 클릭 — 목록 펼침/접기.</summary>
    [RelayCommand]
    private void ToggleSessionList() => IsSessionListExpanded = !IsSessionListExpanded;

    /// <summary>목록에 한 번에 보여줄 최대 줄 수. 나머지는 "+N개 더" 로만 알린다.</summary>
    private const int MaxSessionListRows = 8;

    /// <summary>
    /// 세션 목록을 최근 활동 순으로 다시 만든다.
    /// <paramref name="displayCount"/> 는 다른 PC 몫이 합쳐진 수라 이 PC 세션 수보다 클 수 있고,
    /// 그 차이는 목록으로 보여줄 방법이 없으므로(트랜스크립트가 이 PC 에 없다) 안내 문구로 남긴다.
    /// </summary>
    private void ApplySessionList(IReadOnlyList<SessionInfo> sessions, int displayCount)
    {
        var nowUtc = DateTime.UtcNow;
        var ordered = sessions.OrderByDescending(s => s.LastActivityUtc).ToList();

        TodaySessions = ordered
            .Take(MaxSessionListRows)
            .Select(s => new SessionListItem(s, nowUtc))
            .ToList();

        SessionListHiddenCount = ordered.Count - TodaySessions.Count;
        SessionListRemoteCount = Math.Max(0, displayCount - ordered.Count);
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

        // 시간 진행률은 리셋 시각만 있으면 매 순간 계산 가능 — 매초 갱신해 막대가 부드럽게 흐르게 한다.
        RecomputeClaudeTimeProgress(now);
        RecomputeCodexTimeProgress(now);
        OpenCodeVm.UpdateTimeProgress(now);
        AntigravityVm.UpdateTimeProgress(now);
    }

    /// <summary>
    /// Claude 5시간/7일 윈도우의 "시간 진행률"을 리셋 시각으로부터 역산한다.
    /// 윈도우 시작 = 리셋 - 윈도우 길이(5h / 7d) 이므로, 경과 비율 = 1 - 남은시간/윈도우길이.
    /// UsageCapped = min(사용량, 시간) 은 보라 레이어 폭 — 사용량이 시간을 앞지른 만큼만 주황으로 노출된다.
    /// </summary>
    private void RecomputeClaudeTimeProgress(DateTimeOffset now)
    {
        // 소진 예측과 동일한 하한(5h→5분, 7d→2시간)으로 윈도우 초반에는 페이스 판정을 유보한다.
        // 리셋 직후 1~2분 사용이 "거의 전부 초과(주황)"로 과장돼 보이는 것을 막는다(settled=false → 초과색·문구 억제).
        var shortTime = UsageCalculator.TimeProgress(_rawClaudeShortResetAt, TimeSpan.FromHours(5), now);
        bool shortSettled = IsPaceSettled(shortTime, TimeSpan.FromHours(5), TimeSpan.FromMinutes(5));
        ClaudeVm.HasShortTimeline = shortTime.HasValue;
        ClaudeVm.ShortTimePercent = shortTime ?? 0;
        ClaudeVm.ShortUsageCapped = shortSettled ? Math.Min(ClaudeVm.ShortPercent, shortTime!.Value) : ClaudeVm.ShortPercent;
        ClaudeVm.ShortPaceTip     = Loc.PaceTip(shortTime, ClaudeVm.ShortPercent, shortSettled);

        var longTime = UsageCalculator.TimeProgress(_rawClaudeLongResetAt, TimeSpan.FromDays(7), now);
        bool longSettled = IsPaceSettled(longTime, TimeSpan.FromDays(7), TimeSpan.FromHours(2));
        ClaudeVm.HasLongTimeline = longTime.HasValue;
        ClaudeVm.LongTimePercent = longTime ?? 0;
        ClaudeVm.LongUsageCapped = longSettled ? Math.Min(ClaudeVm.LongPercent, longTime!.Value) : ClaudeVm.LongPercent;
        ClaudeVm.LongPaceTip     = Loc.PaceTip(longTime, ClaudeVm.LongPercent, longSettled);
    }

    /// <summary>
    /// Codex 시간선(시간 진행률 마커)을 실제 창 길이로 역산한다.
    /// Claude 와 달리 창 길이가 고정이 아니므로(5시간 · 주간 …) 응답의 window_minutes 를 써야 한다.
    /// 5시간으로 하드코딩하면 주간 창에서 진행률이 음수 → 0 으로 잘려 마커가 왼쪽 끝에 숨는다.
    /// </summary>
    private void RecomputeCodexTimeProgress(DateTimeOffset now)
    {
        var shortTime = UsageCalculator.TimeProgress(_rawCodexShortResetAt, _rawCodexShortWindow, now);
        HasCodexShortTimeline = shortTime.HasValue;
        CodexShortTimePercent = shortTime ?? 0;
        CodexShortPaceTip = Loc.PaceTip(shortTime, CodexPercent,
            IsPaceSettled(shortTime, _rawCodexShortWindow, _rawCodexShortWindow / 60));

        var longTime = UsageCalculator.TimeProgress(_rawCodexLongResetAt, _rawCodexLongWindow, now);
        HasCodexLongTimeline = longTime.HasValue;
        CodexLongTimePercent = longTime ?? 0;
        CodexLongPaceTip = Loc.PaceTip(longTime, CodexLongPercent,
            IsPaceSettled(longTime, _rawCodexLongWindow, _rawCodexLongWindow / 60));
    }

    // 페이스 판정 기준은 Antigravity 행(AntigravityModelRow)도 같이 쓰므로 UsageCalculator 에 둔다.
    private static bool IsPaceSettled(double? timeProgress, TimeSpan window, TimeSpan minElapsed)
        => UsageCalculator.IsPaceSettled(timeProgress, window, minElapsed);

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

    internal static string ParseFriendlyError(string raw)
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
        var settings = _settingsService.Load();
        settings.UsageSyncEnabled = UsageSyncEnabled;
        settings.UsageSyncFolderPath = UsageSyncFolderPath.Trim();
        settings.UsageSyncApiSnapshotTtlMinutes = Math.Max(1, UsageSyncApiSnapshotTtlMinutes);
        settings.UsageSyncLocalSnapshotTtlHours = Math.Max(1, UsageSyncLocalSnapshotTtlHours);
        settings.KeepPopupAboveTaskbar = KeepPopupAboveTaskbar;
        settings.UsagePanelOpacity = UsagePanelOpacity;
        return settings;
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
        WeatherVm.ForecastSource = WeatherForecastSource;
        WeatherVm.ForecastModel = WeatherForecastModel;

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

    private void OnLanguageChanged()
    {
        // 종료 중이면 Application.Current 가 이미 null 이다 — 갱신할 화면도 없으므로 그냥 넘어간다.
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            OpenCodeVm.RefreshLocalizedLabels();
            AntigravityVm.RefreshLocalizedLabels();
            // 행 객체가 새로 만들어지므로 미러 프로퍼티도 다시 가리켜야 화면이 바뀐다.
            AntigravityModels = AntigravityVm.Models;
            // 안내·출처 문구는 값이 필드에 담겨 있어 다시 만들어야 바뀐다(다음 조회까지 기다리지 않도록).
            AntigravityNote = Loc.ProviderAntigravityNote;
            RefreshAntigravityDataSourceLabel();
            OnPropertyChanged(string.Empty);
        });
    }

    public void Dispose()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
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

        var remoteQuota = IsAntigravityEnabled ? TrySyncAntigravityQuota() : null;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            AntigravityNote = Loc.ProviderAntigravityNote;
            _antigravityQuotaOrigin = null;

            // 이 PC 에서 Antigravity 에 로그인하지 않았거나 조회가 실패하면 다른 PC 가 올린 값으로 채운다.
            // 할당량은 구글 계정 단위라 어느 PC 에서 받아도 같은 값이다.
            if (!AntigravityVm.HasData && remoteQuota is { Quota.HasData: true } snapshot)
            {
                AntigravityVm.ApplyQuota(
                    ToAntigravityModelQuotas(snapshot.Quota!.Models),
                    snapshot.Quota.TierName,
                    snapshot.Quota.PaidTierName);

                // 다른 PC 의 값을 보고 있다는 것은 Codex 와 같은 자리(오른쪽 출처)에 적는다.
                _antigravityQuotaOrigin = (
                    snapshot.DeviceName,
                    snapshot.Quota.ObservedAtUtc ?? snapshot.ObservedAtUtc);
            }
            RefreshAntigravityDataSourceLabel();

            AntigravityHasData = AntigravityVm.HasData;
            AntigravityHasError = AntigravityVm.HasError;
            AntigravityErrorMessage = AntigravityVm.ErrorMessage;
            AntigravityTierName = AntigravityVm.TierName;
            AntigravityPaidTierName = AntigravityVm.PaidTierName;
            AntigravityModels = AntigravityVm.Models;
            AntigravityPercent = AntigravityVm.Percent;
        });
    }

    /// <summary>
    /// Antigravity 할당량을 공유 폴더에 쓰고 다른 PC 것을 읽어 온다.
    /// 토큰 합계 개념이 없는 provider 라 <see cref="TrySyncProviderSnapshot"/> 대신 할당량만 주고받는다.
    ///
    /// 계정 키는 두지 않는다(Codex 등 다른 provider 와 동일). 이 기능이 쓰이는 상황이 바로
    /// "이 PC 는 Antigravity 에 로그인하지 않았다" 인데, 그러면 이메일을 몰라 계정 해시가 달라지고
    /// 정작 필요한 PC 가 상대 폴더를 못 읽는다. 공유 폴더 자체가 한 사용자 것이라는 전제로 묶는다.
    /// </summary>
    private UsageSyncSnapshot? TrySyncAntigravityQuota()
    {
        if (!IsUsageSyncReady)
            return null;

        try
        {
            var local = AntigravityVm.LastSnapshot;
            var snapshot = _usageSync.CreateSnapshot(
                UsageProviderKind.Antigravity,
                null,
                CreateAntigravityQuotaSnapshot(local),
                new UsageSyncLocalTotals(),
                local.HasData ? "" : ClassifyProviderError(local.ErrorMessage));
            _usageSync.WriteSnapshot(UsageSyncFolderPath, snapshot);

            var today = DateOnly.FromDateTime(DateTime.Now);
            var read = _usageSync.ReadSnapshots(UsageSyncFolderPath, UsageProviderKind.Antigravity, null, today);
            UsageSyncStatusLabel = Loc.UsageSyncReady;
            return _usageSync.SelectNewestQuotaSnapshot(read.Snapshots, UsageSyncApiTtl);
        }
        catch (Exception ex)
        {
            UsageSyncStatusLabel = Loc.UsageSyncFailed(ex.Message);
#if DEBUG
            Debug.WriteLine($"[MainViewModel] Antigravity usage sync failed: {ex}");
#endif
            return null;
        }
    }

    /// <summary>
    /// 출처 문구를 현재 언어로 만든다. 다른 PC 값을 보고 있지 않으면 빈 문자열 —
    /// 로컬 조회 결과에는 "어디서 왔는지" 적을 것이 없다.
    /// </summary>
    private void RefreshAntigravityDataSourceLabel() =>
        AntigravityDataSource = _antigravityQuotaOrigin is { } origin
            ? Loc.UsageSyncQuotaFromDevice(origin.Device, origin.ObservedAt.ToLocalTime().ToString("HH:mm"))
            : "";

    private static UsageSyncQuotaSnapshot? CreateAntigravityQuotaSnapshot(AntigravitySnapshot snapshot)
    {
        if (!snapshot.HasData || snapshot.Models.Count == 0)
            return null;

        return new UsageSyncQuotaSnapshot
        {
            HasData = true,
            TierName = snapshot.TierName ?? "",
            PaidTierName = snapshot.PaidTierName ?? "",
            Models = [.. snapshot.Models.Select(model => new UsageSyncModelQuota
            {
                ModelId = model.ModelId,
                RemainingFraction = model.RemainingFraction,
                ResetAt = model.ResetTime,
                DisplayName = model.DisplayName,
                Window = model.TokenType,
            })],
        };
    }

    private static IReadOnlyList<AntigravityModelQuota> ToAntigravityModelQuotas(UsageSyncModelQuota[] models) =>
        [.. models.Select(model => new AntigravityModelQuota
        {
            ModelId = model.ModelId,
            RemainingFraction = model.RemainingFraction,
            ResetTime = model.ResetAt,
            DisplayName = model.DisplayName,
            TokenType = model.Window,
        })];
}
