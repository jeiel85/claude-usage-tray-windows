using System.Text.Json;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
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
    private readonly NotificationService _notifier;
    private readonly SettingsService _settingsService;
    private readonly UpdateService _updater;
    private readonly HistoryService _history;
    private readonly Timer _timer;
    private readonly Timer _countdownTimer;
    private readonly Timer _updateTimer;
    private int _secondsUntilRefresh = 0;

    // Tracks previous 5h usage to detect threshold crossings
    private double _prevShortPercent = -1;
    private double _prevCodexPercent = -1;
    private double _prevGeminiPercent = -1;
    private double _prevExtraPercent = -1;
    private bool _prevHadRateLimit = false;

    // Last known good API data (kept when rate-limited so UI doesn't reset to 0)
    private double _lastKnownShortPercent = 0;
    private double _lastKnownLongPercent = 0;
    private string _lastKnownShortReset = "";
    private string _lastKnownLongReset = "";

    // Rate limit backoff — skip API calls until this time
    private DateTimeOffset _apiRetryAfter = DateTimeOffset.MinValue;

    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private string _nextRefreshLabel = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _lastUpdatedLabel = "";
    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";

    // Claude Usage (5h/7d)
    [ObservableProperty] private double _claudeShortPercent = 0;
    [ObservableProperty] private string _claudeShortReset = "";
    [ObservableProperty] private double _claudeLongPercent = 0;
    [ObservableProperty] private string _claudeLongReset = "";
    [ObservableProperty] private string _claudeShortSummary = "";
    [ObservableProperty] private string _claudeLongSummary = "";
    [ObservableProperty] private string _claudeShortDepletion = "";
    [ObservableProperty] private string _claudeLongDepletion = "";
    [ObservableProperty] private bool _claudeHasError = false;
    [ObservableProperty] private string _claudeErrorMessage = "";
    // 쿨다운(API 일시 제한) 안내: 에러가 아니라 재시도 시점만 부드럽게 표시
    [ObservableProperty] private string _claudeApiNote = "";

    // Codex Usage
    [ObservableProperty] private double _codexPercent = 0;
    [ObservableProperty] private string _codexReset = "";
    [ObservableProperty] private string _codexDataSource = "";
    [ObservableProperty] private bool _codexHasError = false;
    [ObservableProperty] private string _codexErrorMessage = "";
    [ObservableProperty] private string _codexNote = Loc.ProviderCodexNote;
    [ObservableProperty] private string _codexSummary = "";
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

    [ObservableProperty] private double _openCodePercent = 0;
    [ObservableProperty] private double _trayUsagePercent = 0;

    // Visibility control
    [ObservableProperty] private bool _isClaudeActive = true;
    [ObservableProperty] private bool _isCodexActive = false;
    [ObservableProperty] private bool _isGeminiActive = false;
    [ObservableProperty] private bool _isOpenCodeActive = false;
    [ObservableProperty] private bool _isClaudeUsageEmpty = true;
    [ObservableProperty] private bool _isCodexUsageEmpty = true;
    [ObservableProperty] private bool _isGeminiUsageEmpty = true;
    [ObservableProperty] private bool _isOpenCodeUsageEmpty = true;

    private string _updateDownloadUrl = "";
    private string _updateSha256Url = "";
    public string CurrentVersionLabel => $"v{UpdateService.CurrentVersion.ToString(3)}";

    public string? RawApiResponse { get; private set; }

    // Localized static labels
    public string LblAppTitle        => Loc.AgentUsageTitle;
    public string LblApiQuota        => Loc.ApiQuota;
    public string LblTodayTokens     => Loc.TodayTokens;
    public string LblFiveHour        => Loc.FiveHourWindow;
    public string LblSevenDay        => Loc.SevenDayWindow;
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
                         OpenCodeUsageMonitor openCode,
                         NotificationService notifier, SettingsService settingsService,
                         UpdateService updater, HistoryService history)
    {
        _api = api;
        _credentials = credentials;
        _session = session;
        _codex = codex;
        _geminiCli = geminiCli;
        _openCode = openCode;
        _notifier = notifier;
        _settingsService = settingsService;
        _updater = updater;
        _history = history;

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
            System.Windows.Application.Current.Dispatcher.Invoke(() => NextRefreshLabel = label);
        };
        _countdownTimer.AutoReset = true;

        _updateTimer = new Timer(AppConstants.UpdateCheckIntervalMs); // 24 hours
        _updateTimer.Elapsed += async (_, _) => await CheckForUpdateAsync();
        _updateTimer.AutoReset = true;
    }

    private void OnCredentialsChanged()
    {
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

        // 현재 로그인된 계정의 orgUuid로 히스토리 경로 초기화
        ApplySelectedProviderScope();

        // Apply polling interval
        ApplyPollingInterval();
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

        UpdateCheckLabel = Loc.CheckingUpdate;

        UpdateService.UpdateInfo? result;
        try
        {
            result = await _updater.CheckForUpdateAsync();
        }
        catch
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                UpdateCheckLabel = Loc.UpdateCheckFailed;
                await Task.Delay(3000);
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
            var versionStr = info.version.ToString(3);
            UpdateCheckLabel = "";

            var settings = _settingsService.Load();
            if (settings.SkippedVersion == versionStr)
            {
                settings.SkippedVersion = "";
                _settingsService.Save(settings);
            }

            _updateDownloadUrl = info.downloadUrl;
            _updateSha256Url = info.sha256Url;
            UpdateLabel = Loc.UpdateAvailable($"v{versionStr}");
            UpdateAvailable = true;

            ShowUpdateDialog(versionStr, info.releaseNotes);
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
        var versionStr = info.version.ToString(3);

        var settings = _settingsService.Load();
        if (settings.SkippedVersion == versionStr) return;

        _updateDownloadUrl = info.downloadUrl;
        _updateSha256Url = info.sha256Url;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateLabel = Loc.UpdateAvailable($"v{versionStr}");
            UpdateAvailable = true;
            ShowUpdateDialog(versionStr, info.releaseNotes);
        });
    }

    private void ShowUpdateDialog(string version, string notes)
    {
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
        dialog.Show();
        dialog.Activate();
    }

    [RelayCommand]
    public void StartUpdate()
    {
        // This is called from the banner. Since we want to show release notes, 
        // we'll just re-trigger the check which will show the dialog.
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
                RefreshOpenCodeInternalAsync()
            };

            await Task.WhenAll(tasks);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 공통 정보 업데이트
                UpdateOverallStatus();
                
                LastUpdatedLabel = (ClaudeHasError || CodexHasError || GeminiHasError || OpenCodeHasError)
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
        // 1. 각 공급자 활성 상태 판단 (데이터가 있거나 에러가 있는 경우 활성으로 간주, 단 설정에 따라 숨김)
        var settings = _settingsService.Load();
        var hideInactive = settings.HideInactiveProviders;

        IsClaudeActive = IsClaudeEnabled && (!hideInactive || TodayInputTokens + TodayOutputTokens > 0 || ClaudeShortPercent > 0 || ClaudeHasError);
        IsCodexActive = IsCodexEnabled && (!hideInactive || CodexPercent > 0 || CodexHasError);
        IsGeminiActive = IsGeminiEnabled && (!hideInactive || _lastGeminiRequestCount > 0 || GeminiHasError);
        IsOpenCodeActive = IsOpenCodeEnabled && (!hideInactive || _lastOpenCodeRequestCount > 0 || OpenCodeHasError);

        IsClaudeUsageEmpty = TodayInputTokens + TodayOutputTokens == 0;
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
            UsageProviderKind.Claude => ClaudeShortPercent,
            UsageProviderKind.Codex => CodexPercent,
            UsageProviderKind.GeminiCli => GeminiPercent,
            UsageProviderKind.OpenCode => OpenCodePercent,
            _ => ClaudeShortPercent
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

        if (ClaudeHasError && CodexHasError && GeminiHasError && OpenCodeHasError)
        {
            StatusText = "All Providers Error";
            HasError = true;
        }
        else if (ClaudeHasError || CodexHasError || GeminiHasError || OpenCodeHasError)
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
                _ => Loc.TrayStatusClaude(ClaudeShortPercent)
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
                    ClaudeHasError = false;
                    ClaudeErrorMessage = "";
                    ClaudeApiNote = "";

                    if (usage.FiveHour != null && usage.FiveHour.UsagePercent < 1.0)
                    {
                        HasRateLimitHit = false;
                        RateLimitInfo = "";
                    }

                    if (usage!.FiveHour != null)
                    {
                        var newPercent = usage.FiveHour.UsagePercent;
                        ClaudeShortReset = FormatResetLabel(usage.FiveHour.ResetsAtParsed);
                        ClaudeShortSummary = Loc.UsageSummary(newPercent);
                        ClaudeShortDepletion = CalcDepletionLabel(usage.FiveHour);

                        if (NotificationsEnabled && _prevShortPercent >= 0)
                        {
                            CheckThresholds(newPercent, ClaudeShortReset, NtfyTopicEffective);
                        }

                        ClaudeShortPercent = newPercent;
                        _prevShortPercent = newPercent;
                        _lastKnownShortPercent = newPercent;
                        _lastKnownShortReset = ClaudeShortReset;
                        
                        // Compatibility for legacy bindings if any
                        ShortUsagePercent = newPercent;
                        ShortResetLabel = ClaudeShortReset;
                    }

                    if (usage.SevenDay != null)
                    {
                        ClaudeLongPercent    = usage.SevenDay.UsagePercent;
                        ClaudeLongReset      = FormatResetLabel(usage.SevenDay.ResetsAtParsed);
                        ClaudeLongSummary    = Loc.UsageSummary(ClaudeLongPercent);
                        ClaudeLongDepletion  = CalcLongDepletionLabel(usage.SevenDay);
                        _lastKnownLongPercent = ClaudeLongPercent;
                        _lastKnownLongReset = ClaudeLongReset;
                        
                        LongUsagePercent = ClaudeLongPercent;
                        LongResetLabel = ClaudeLongReset;
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
                        ExtraUsageEnabled = true;
                        ExtraHasLimit     = eu.MonthlyLimit.HasValue;
                        ExtraUsagePercent = eu.MonthlyLimit.HasValue
                            ? Math.Min(1.0, (eu.Utilization ?? 0) / 100.0)
                            : 0;
                        ExtraCreditsLabel = (eu.UsedCredits.HasValue && eu.MonthlyLimit.HasValue)
                            ? Loc.ExtraCredits(eu.UsedCredits.Value, eu.MonthlyLimit.Value)
                            : eu.UsedCredits.HasValue
                                ? Loc.ExtraCreditsUsedOnly(eu.UsedCredits.Value)
                                : "";

                        if (_prevExtraPercent < 0) _prevExtraPercent = ExtraUsagePercent;
                    }
                    else
                    {
                        ExtraUsageEnabled = false;
                    }

                    if (ExtraUsageEnabled && ClaudeShortPercent >= 1.0)
                    {
                        IsExtraOnlyMode = true;
                        StatusText = Loc.ExtraUsageExhausted;
                    }
                    else if (ClaudeShortPercent < 0.5)
                    {
                        IsExtraOnlyMode = false;
                        StatusText = $"{ClaudeShortPercent:P0} used";
                    }
                    else if (!IsExtraOnlyMode)
                    {
                        StatusText = $"{ClaudeShortPercent:P0} used";
                    }
                }
                else if (skipApi || _api.LastError != null)
                {
                    ClaudeShortPercent = _lastKnownShortPercent;
                    ClaudeShortReset   = _lastKnownShortReset;
                    ClaudeShortSummary = Loc.UsageSummary(_lastKnownShortPercent);
                    ClaudeLongPercent  = _lastKnownLongPercent;
                    ClaudeLongReset    = _lastKnownLongReset;
                    ClaudeLongSummary  = Loc.UsageSummary(_lastKnownLongPercent);

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
                        ClaudeHasError = false;
                        ClaudeErrorMessage = "";
                        ClaudeApiNote = isOAuthNotAllowed
                            ? Loc.ApiOAuthNotAllowedNote
                            : Loc.ApiPermissionDeniedNote;
                    }
                    else if (isCooldown)
                    {
                        // 일시 제한은 에러 아닌 안내로만 — 빨간 박스 대신 회색 톤 자동 재시도 안내
                        ClaudeHasError = false;
                        ClaudeErrorMessage = "";
                        ClaudeApiNote = Loc.ApiCooldownNote(_apiRetryAfter.ToLocalTime().ToString("HH:mm"));
                    }
                    else
                    {
                        ClaudeHasError = true;
                        ClaudeErrorMessage = _api.LastError != null
                            ? ParseFriendlyError(_api.LastError)
                            : Loc.RateLimited;
                        ClaudeApiNote = "";
                    }
                }
            });
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ClaudeHasError = true;
                ClaudeErrorMessage = ex.Message;
                ClaudeApiNote = "";
                UpdateOverallStatus();
            });
        }
    }

    private async Task RefreshCodexInternalAsync()
    {
        try
        {
            var snapshot = await _codex.GetTodaySnapshotAsync();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var newPercent = snapshot.ShortUsagePercent;
                CodexReset = FormatResetLabel(snapshot.ShortResetAt, snapshot.IsShortResetEstimated);
                CodexDataSource = snapshot.DataSource ?? "";
                // "오늘 사용 기록 없음"은 정보성 메시지이므로 회색 placeholder만 표시 — 빨간 에러 중복 방지
                var codexInformational = IsNoUsageInformational(snapshot.ErrorMessage, UsageProviderKind.Codex);
                CodexHasError = !snapshot.HasData && !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) && !codexInformational;
                CodexErrorMessage = codexInformational ? "" : (snapshot.ErrorMessage ?? "");
                CodexSummary = Loc.UsageSummary(newPercent);

                if (NotificationsEnabled && _prevCodexPercent >= 0)
                {
                    CheckProviderThresholds(UsageProviderKind.Codex, newPercent, CodexReset, NtfyTopicEffective);
                }

                CodexPercent = newPercent;
                _prevCodexPercent = newPercent;

                // 오늘의 토큰 4타일 라벨 채우기
                CodexInputLabel      = snapshot.TotalInputTokens      > 0 ? FormatTokenShort(snapshot.TotalInputTokens)      : "—";
                CodexOutputLabel     = snapshot.TotalOutputTokens     > 0 ? FormatTokenShort(snapshot.TotalOutputTokens)     : "—";
                CodexCacheReadLabel  = snapshot.TotalCacheReadTokens  > 0 ? FormatTokenShort(snapshot.TotalCacheReadTokens)  : "—";
                CodexCacheWriteLabel = snapshot.TotalCacheWriteTokens > 0 ? FormatTokenShort(snapshot.TotalCacheWriteTokens) : "—";

                // Codex 자체 scope에 오늘 일별 history 기록 (활성 scope와 무관)
                _history.RecordToday(UsageProviderKind.Codex, null,
                    snapshot.TotalInputTokens, snapshot.TotalOutputTokens,
                    snapshot.TotalCacheReadTokens, snapshot.TotalCacheWriteTokens,
                    snapshot.SessionCount);

                // 전체 상태 갱신
                UpdateOverallStatus();
            });
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CodexHasError = true;
                CodexErrorMessage = ex.Message;
                UpdateOverallStatus();
            });
        }
    }

    private async Task RefreshGeminiCliInternalAsync()
    {
        try
        {
            var snapshot = _geminiCli.GetTodaySnapshot();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var geminiInformational = IsNoUsageInformational(snapshot.ErrorMessage, UsageProviderKind.GeminiCli);
                GeminiHasError = !snapshot.HasData && !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) && !geminiInformational;
                GeminiErrorMessage = geminiInformational ? "" : (snapshot.ErrorMessage ?? "");
                
                _lastGeminiRequestCount = snapshot.RequestCount;
                _lastGeminiOutputTokens = snapshot.TotalOutputTokens;

                GeminiRequestsLabel = snapshot.RequestCount > 0
                    ? Loc.CurrentLang == "ko" ? $"{snapshot.RequestCount}회" : $"{snapshot.RequestCount} req"
                    : "—";
                GeminiInputLabel = snapshot.TotalInputTokens > 0
                    ? FormatTokenShort(snapshot.TotalInputTokens)
                    : "—";
                GeminiOutputTokensLabel = snapshot.TotalOutputTokens > 0
                    ? FormatTokenShort(snapshot.TotalOutputTokens)
                    : "—";
                GeminiCacheReadLabel = snapshot.TotalCacheReadTokens > 0
                    ? FormatTokenShort(snapshot.TotalCacheReadTokens)
                    : "—";
                GeminiCacheWriteLabel = "—"; // Gemini는 cache write 개념 없음
                GeminiSummary = snapshot.HasData
                    ? Loc.GeminiCliRequestSummary(snapshot.RequestCount, snapshot.TotalOutputTokens)
                    : snapshot.ErrorMessage ?? "";

                // Gemini 자체 scope의 최근 7일 최대치 대비 게이지 비율 계산
                var max = _history.GetRecentMaxTotalTokens(UsageProviderKind.GeminiCli, null, 7);
                var goal = Math.Max(10000, max);
                var percent = Math.Clamp(snapshot.TotalOutputTokens / (double)goal, 0, 1);
                GeminiPercent = percent;
                _prevGeminiPercent = percent;

                // Gemini 자체 scope에 오늘 일별 history 기록
                _history.RecordToday(UsageProviderKind.GeminiCli, null,
                    snapshot.TotalInputTokens, snapshot.TotalOutputTokens,
                    snapshot.TotalCacheReadTokens, snapshot.TotalCacheWriteTokens,
                    snapshot.SessionCount);

                // 전체 상태 갱신
                UpdateOverallStatus();
            });
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                GeminiHasError = true;
                GeminiErrorMessage = ex.Message;
                UpdateOverallStatus();
            });
        }
    }

    private void CheckProviderThresholds(string provider, double newPercent, string resetLabel, string ntfyTopic)
    {
        var settings = _settingsService.Load();
        double prevPercent = provider switch
        {
            UsageProviderKind.Codex => _prevCodexPercent,
            UsageProviderKind.GeminiCli => _prevGeminiPercent,
            _ => -1
        };

        if (prevPercent < 0) return;

        // 1. 할당량 초기화 감지
        if (NotifyOnQuotaReset && prevPercent >= 1.0 && newPercent < 1.0)
        {
            _notifier.ShowQuotaResetAlert(ntfyTopic);
        }

        // 2. 임계값 알림
        foreach (var t in settings.Thresholds.OrderBy(x => x))
        {
            double tf = t / 100.0;
            if (prevPercent < tf && newPercent >= tf)
            {
                _notifier.ShowUsageAlert(t, provider, resetLabel, ntfyTopic);
            }
        }
    }

    private async Task RefreshOpenCodeInternalAsync()
    {
        try
        {
            // OpenCode 자체 scope의 최근 7일 최대치 대비 게이지 비율 계산
            var max = _history.GetRecentMaxTotalTokens(UsageProviderKind.OpenCode, null, 7);
            var goal = Math.Max(10000, max);
            var snapshot = _openCode.GetTodaySnapshot(goal);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var openCodeInformational = IsNoUsageInformational(snapshot.ErrorMessage, UsageProviderKind.OpenCode);
                OpenCodeHasError = !snapshot.HasData && !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) && !openCodeInformational;
                OpenCodeErrorMessage = openCodeInformational ? "" : (snapshot.ErrorMessage ?? "");

                _lastOpenCodeRequestCount = snapshot.RequestCount;
                _lastOpenCodeInputTokens  = snapshot.TotalInputTokens;
                _lastOpenCodeOutputTokens = snapshot.TotalOutputTokens;

                OpenCodeRequestCountLabel = snapshot.RequestCount > 0
                    ? Loc.CurrentLang == "ko" ? $"{snapshot.RequestCount}회" : $"{snapshot.RequestCount} req"
                    : "—";
                OpenCodeInputLabel      = snapshot.TotalInputTokens      > 0 ? FormatTokenShort(snapshot.TotalInputTokens)      : "—";
                OpenCodeOutputLabel     = snapshot.TotalOutputTokens     > 0 ? FormatTokenShort(snapshot.TotalOutputTokens)     : "—";
                OpenCodeCacheReadLabel  = snapshot.TotalCacheReadTokens  > 0 ? FormatTokenShort(snapshot.TotalCacheReadTokens)  : "—";
                OpenCodeCacheWriteLabel = snapshot.TotalCacheWriteTokens > 0 ? FormatTokenShort(snapshot.TotalCacheWriteTokens) : "—";
                OpenCodeSummary = snapshot.HasData
                    ? Loc.CurrentLang == "ko"
                        ? $"오늘 {snapshot.RequestCount}회 · 입력 {FormatTokenShort(snapshot.TotalInputTokens)} · 출력 {FormatTokenShort(snapshot.TotalOutputTokens)}"
                        : $"Today {snapshot.RequestCount} req · in {FormatTokenShort(snapshot.TotalInputTokens)} · out {FormatTokenShort(snapshot.TotalOutputTokens)}"
                    : snapshot.ErrorMessage ?? "";
                OpenCodePercent = snapshot.ShortUsagePercent;

                // OpenCode 자체 scope에 오늘 일별 history 기록
                _history.RecordToday(UsageProviderKind.OpenCode, null,
                    snapshot.TotalInputTokens, snapshot.TotalOutputTokens,
                    snapshot.TotalCacheReadTokens, snapshot.TotalCacheWriteTokens,
                    snapshot.SessionCount);

                UpdateOverallStatus();
            });
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OpenCodeHasError = true;
                OpenCodeErrorMessage = ex.Message;
                UpdateOverallStatus();
            });
        }
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
            ClaudeShortSummary = Loc.UsageSummary(snapshot.ShortUsagePercent);
            ClaudeLongSummary = Loc.UsageSummary(snapshot.LongUsagePercent);
            ShortDepletionLabel = snapshot.ShortResetAt is null ? "" : "";
            LongDepletionLabel = snapshot.LongResetAt is null ? "" : "";

            ExtraUsageEnabled = false;
            ExtraHasLimit = false;
            ExtraCreditsLabel = "";
            IsExtraOnlyMode = false;
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
                _notifier.ShowUsageAlert(t, Loc.FiveHourWindow, resetLabel, ntfyTopic);
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
                    _notifier.ShowUsageAlert(t, Loc.ExtraUsageTitle, "", ntfyTopic);
                }
            }
            _prevExtraPercent = ExtraUsagePercent;
        }
    }

    private static string FormatResetLabel(DateTimeOffset? resetAt, bool isEstimated = false)
    {
        if (resetAt is null) return "";
        var diff = resetAt.Value - DateTimeOffset.Now;
        if (diff.TotalSeconds <= 0) return "";
        string time;
        if (diff.TotalHours < 1) time = $"{(int)diff.TotalMinutes}m";
        else if (diff.TotalDays < 1) time = $"{(int)diff.TotalHours}h {diff.Minutes}m";
        else time = $"{(int)diff.TotalDays}d {diff.Hours}h";
        return isEstimated ? Loc.ResetsInEstimated(time) : Loc.ResetsIn(time);
    }

    private static string ParseFriendlyError(string raw)
    {
        if (raw.Contains("429") || raw.Contains("rate_limit"))
            return Loc.RateLimited;
#if DEBUG
        try
        {
            var start = raw.IndexOf('{');
            if (start >= 0)
            {
                var json = raw[start..];
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("message", out var msg))
                {
                    var msgText = msg.GetString() ?? raw;
                    return Loc.ApiError(msgText.Length > 200 ? msgText[..200] + "…" : msgText);
                }
            }
        }
        catch { }
#endif
        return Loc.ApiError(raw.Length > 200 ? raw[..200] + "…" : raw);
    }

    private static string CalcDepletionLabel(Models.UsageWindow w)
    {
        if (w.ResetsAtParsed is null || w.UsagePercent <= 0.02 || w.UsagePercent >= 1.0) return "";

        var windowStart = w.ResetsAtParsed.Value - TimeSpan.FromHours(5);
        var elapsed = DateTimeOffset.Now - windowStart;
        if (elapsed.TotalMinutes < 5) return "";

        double ratePerHour = w.UsagePercent / elapsed.TotalHours;
        if (ratePerHour <= 0) return "";

        double hoursToFull = (1.0 - w.UsagePercent) / ratePerHour;
        var remaining = w.ResetsAtParsed.Value - DateTimeOffset.Now;

        // 윈도우 내에 소진되지 않으면 표시 불필요
        if (hoursToFull >= remaining.TotalHours) return "";

        var depletionAt = DateTimeOffset.Now.AddHours(hoursToFull).ToLocalTime();
        return Loc.DepletionAt(depletionAt.ToString("HH:mm"));
    }

    private static string CalcLongDepletionLabel(Models.UsageWindow w)
    {
        if (w.ResetsAtParsed is null || w.UsagePercent <= 0.02 || w.UsagePercent >= 1.0) return "";

        var windowStart = w.ResetsAtParsed.Value - TimeSpan.FromDays(7);
        var elapsed = DateTimeOffset.Now - windowStart;
        if (elapsed.TotalHours < 2) return ""; // 데이터 부족

        double ratePerDay = w.UsagePercent / elapsed.TotalDays;
        if (ratePerDay <= 0) return "";

        double daysToFull = (1.0 - w.UsagePercent) / ratePerDay;
        var remaining = w.ResetsAtParsed.Value - DateTimeOffset.Now;

        // 윈도우 내에 소진되지 않으면 표시 불필요
        if (daysToFull >= remaining.TotalDays) return "";

        var depletionAt = DateTimeOffset.Now.AddDays(daysToFull).ToLocalTime();
        var timeStr = daysToFull < 1
            ? depletionAt.ToString("HH:mm")
            : depletionAt.ToString("M/d HH:mm");
        return Loc.DepletionAt(timeStr);
    }

    private static string FormatTokenShort(long tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000.0:F1}M" :
        tokens >= 1_000     ? $"{tokens / 1_000.0:F1}K" :
        tokens.ToString();

    // 회색 placeholder로 이미 표시되는 "오늘 사용 기록 없음" 류는 빨간 에러로 중복 표시하지 않는다
    private static bool IsNoUsageInformational(string? message, string providerKey)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        return providerKey switch
        {
            UsageProviderKind.Codex     => message == Loc.CodexNoUsageToday,
            UsageProviderKind.GeminiCli => message == Loc.GeminiCliNoUsageToday
                                       || message == Loc.GeminiCliEstimateOnly,
            UsageProviderKind.OpenCode  => message == Loc.OpenCodeNoUsageToday,
            _ => false,
        };
    }

    private static string CalcCostLabel(long input, long output, long cacheRead, long cacheWrite)
    {
        // Sonnet 3.5/3.7 API 가격 기준 참고값 (Claude Code는 구독제이므로 실제 과금 아님)
        var cost = input * 3e-6
                 + output * 15e-6
                 + cacheRead * 0.3e-6
                 + cacheWrite * 3.75e-6;
        if (cost < 0.001) return "";
        return Loc.CostEstimate(cost);
    }

    public void Dispose()
    {
        _credentials.CredentialsChanged -= OnCredentialsChanged;
        _credentials.Dispose();
        _timer.Dispose();
        _countdownTimer.Dispose();
        _updateTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
