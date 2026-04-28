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

    // Codex Usage
    [ObservableProperty] private double _codexPercent = 0;
    [ObservableProperty] private string _codexReset = "";
    [ObservableProperty] private string _codexDataSource = "";
    [ObservableProperty] private bool _codexHasError = false;
    [ObservableProperty] private string _codexErrorMessage = "";
    [ObservableProperty] private string _codexNote = Loc.ProviderCodexNote;
    [ObservableProperty] private string _codexSummary = "";

    // Gemini Usage
    [ObservableProperty] private double _geminiPercent = 0;
    [ObservableProperty] private string _geminiReset = "";
    [ObservableProperty] private bool _geminiHasError = false;
    [ObservableProperty] private string _geminiErrorMessage = "";
    [ObservableProperty] private string _geminiNote = Loc.ProviderGeminiCliNote;
    [ObservableProperty] private string _geminiSummary = "";
    [ObservableProperty] private string _geminiRequestsLabel = "";

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
    public string LblExtraCredits    => Loc.ExtraCreditsLabel;
    public string LblCheckUpdate     => Loc.CheckUpdate;
    public string DisclaimerText     => SelectedProvider == UsageProviderKind.Claude ? Loc.Disclaimer : Loc.GenericDisclaimer;

    // Tooltips
    public string TipInput      => Loc.InputTooltip;
    public string TipOutput     => Loc.OutputTooltip;
    public string TipCacheRead  => Loc.CacheReadTooltip;
    public string TipCacheWrite => Loc.CacheWriteTooltip;

    public MainViewModel(UsageApiService api, CredentialService credentials,
                         SessionMonitor session, CodexUsageMonitor codex, GeminiCliUsageMonitor geminiCli,
                         NotificationService notifier, SettingsService settingsService,
                         UpdateService updater, HistoryService history)
    {
        _api = api;
        _credentials = credentials;
        _session = session;
        _codex = codex;
        _geminiCli = geminiCli;
        _notifier = notifier;
        _settingsService = settingsService;
        _updater = updater;
        _history = history;

        // 계정 전환 자동 감지: credentials 파일 변경 → 새로고침
        _credentials.CredentialsChanged += OnCredentialsChanged;

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
            UsageProviderKind.Codex => Loc.ProviderCodexNote,
            UsageProviderKind.GeminiCli => Loc.ProviderGeminiCliNote,
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

        // Preserve SkippedVersion from disk
        var existing = _settingsService.Load();

        _settingsService.Save(new NotificationSettings
        {
            SelectedProvider = SelectedProvider,
            Enabled = NotificationsEnabled,
            NotifyOnRateLimit = NotifyRateLimit,
            NotifyOnQuotaReset = NotifyOnQuotaReset,
            Thresholds = thresholds,
            NtfyTopic = NtfyTopic.Trim(),
            NtfySendFromThisPc = NtfySendFromThisPc,
            StartWithWindows = StartWithWindows,
            SkippedVersion = existing.SkippedVersion,
            PollingIntervalMinutes = PollingIntervalMinutes,
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
                RefreshGeminiCliInternalAsync()
            };

            await Task.WhenAll(tasks);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 공통 정보 업데이트
                UpdateOverallStatus();
                
                LastUpdatedLabel = (ClaudeHasError || CodexHasError || GeminiHasError)
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

    private void UpdateOverallStatus()
    {
        if (ClaudeHasError && CodexHasError && GeminiHasError)
        {
            StatusText = "All Providers Error";
            HasError = true;
        }
        else if (ClaudeHasError || CodexHasError || GeminiHasError)
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
            // 현재 트레이 기준을 대괄호로 강조
            string baseInfo = SelectedProvider switch
            {
                UsageProviderKind.Codex => $"[Codex {CodexPercent:P0}]",
                UsageProviderKind.GeminiCli => GeminiRequestsLabel.Length > 0 ? $"[Gemini {GeminiRequestsLabel}]" : "[Gemini]",
                _ => $"[Claude {ClaudeShortPercent:P0}]"
            };
            
            StatusText = baseInfo;
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

                    ClaudeHasError = true;
                    ClaudeErrorMessage = skipApi && _apiRetryAfter > DateTimeOffset.MinValue
                        ? Loc.RateLimitedUntil(_apiRetryAfter.ToLocalTime().ToString("HH:mm:ss"))
                        : _api.LastError != null ? ParseFriendlyError(_api.LastError) : Loc.RateLimited;
                }
            });
        }
        catch (Exception ex)
        {
            ClaudeHasError = true;
            ClaudeErrorMessage = ex.Message;
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
                CodexHasError = !snapshot.HasData && !string.IsNullOrWhiteSpace(snapshot.ErrorMessage);
                CodexErrorMessage = snapshot.ErrorMessage ?? "";
                CodexSummary = Loc.UsageSummary(newPercent);
                
                if (NotificationsEnabled && _prevCodexPercent >= 0)
                {
                    CheckProviderThresholds(UsageProviderKind.Codex, newPercent, CodexReset, NtfyTopicEffective);
                }
                
                CodexPercent = newPercent;
                _prevCodexPercent = newPercent;
            });
        }
        catch (Exception ex)
        {
            CodexHasError = true;
            CodexErrorMessage = ex.Message;
        }
    }

    private async Task RefreshGeminiCliInternalAsync()
    {
        try
        {
            var snapshot = _geminiCli.GetTodaySnapshot();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                GeminiHasError = !snapshot.HasData && !string.IsNullOrWhiteSpace(snapshot.ErrorMessage);
                GeminiErrorMessage = snapshot.ErrorMessage ?? "";
                GeminiRequestsLabel = snapshot.RequestCount > 0
                    ? Loc.CurrentLang == "ko" ? $"{snapshot.RequestCount}회 요청" : $"{snapshot.RequestCount} req"
                    : "";
                GeminiSummary = snapshot.HasData
                    ? Loc.GeminiCliRequestSummary(snapshot.RequestCount, snapshot.TotalOutputTokens)
                    : snapshot.ErrorMessage ?? "";
                GeminiPercent = 0;
                _prevGeminiPercent = 0;
            });
        }
        catch (Exception ex)
        {
            GeminiHasError = true;
            GeminiErrorMessage = ex.Message;
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
                    return Loc.ApiError(msgText.Length > 80 ? msgText[..80] + "…" : msgText);
                }
            }
        }
        catch { }
#endif
        return Loc.ApiError(raw.Length > 80 ? raw[..80] + "…" : raw);
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
