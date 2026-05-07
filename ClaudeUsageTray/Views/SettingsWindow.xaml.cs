using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Views;

public partial class SettingsWindow : Window, IDisposable
{
    private readonly MainViewModel _vm;
    private bool _disposed = false;
    private bool _isLoadingValues = false;

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        MouseLeftButtonDown += (_, e) => DragMove();
        Deactivated += (_, _) => Hide();
        PreviewKeyDown += OnPreviewKeyDown;

        Loc.LanguageChanged += OnLanguageChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;

        // v1.27.0: 컨텐츠 크기 변경(탭 전환/언어 변경/표시 옵션 추가 등) 시 우하단 앵커 자동 유지
        // — popup 과 동일하게 작업표시줄 침범 방지 (UsagePopup 의 OnSizeChangedKeepAnchor 와 같은 패턴)
        SizeChanged += OnSizeChangedKeepAnchor;

        ApplyLocalization();
        LoadValues();
    }

    /// <summary>
    /// 설정 창 사이즈가 변경되면(탭 전환으로 컨텐츠 높이 변경 등) 우하단 모서리 앵커를 다시 적용.
    /// </summary>
    private void OnSizeChangedKeepAnchor(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || !IsVisible) return;
        AnchorToTrayCorner();
    }

    /// <summary>
    /// 설정 창을 우하단(작업표시줄 위) 8px 마진 위치로 정렬. 화면 work area 밖으로 밀려나지 않도록 클램프.
    /// </summary>
    private void AnchorToTrayCorner()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 8;
        Top  = Math.Max(workArea.Top, workArea.Bottom - ActualHeight - 8);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.EffectiveTrayProvider))
        {
            Dispatcher.Invoke(UpdateTrayAutoHelp);
        }
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            ApplyLocalization();
            LangItemSystem.Content = Loc.LanguageSystem;
        });
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        bool isEsc   = e.Key == System.Windows.Input.Key.Escape;
        bool isCtrlW = e.Key == System.Windows.Input.Key.W && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        bool isAltF4 = e.Key == System.Windows.Input.Key.F4 && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0;

        if (isEsc || isCtrlW || isAltF4)
        {
            Hide();
            e.Handled = true;
        }
    }

    private const string StartupRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRegName = "ClaudeUsageTray";

    private static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, writable: true);
        if (key is null) return;
        if (enable)
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                          ?? System.AppContext.BaseDirectory + "ClaudeUsageTray.exe";
            key.SetValue(StartupRegName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(StartupRegName, throwOnMissingValue: false);
        }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey);
        return key?.GetValue(StartupRegName) is not null;
    }

    private void ApplyLocalization()
    {
        TitleText.Text                      = Loc.Notifications;
        LblGeneral.Text                     = Loc.NotificationsEnabled;
        ChkEnabled.Content                  = Loc.NotificationsEnabled;
        ChkRateLimit.Content                = Loc.NotifyRateLimit;
        ChkQuotaReset.Content               = Loc.NotifyQuotaReset;
        ChkStartWithWindows.Content         = Loc.StartWithWindows;
        BtnTestNotification.Content         = Loc.TestNotification;
        LblTestNotificationHint.Text        = Loc.TestNotificationHint;
        LblThresholds.Text                  = Loc.ThresholdsLabel;
        LblNtfyTitle.Text                   = Loc.NtfyTitle;
        LblNtfyDesc.Text                   = Loc.NtfyDesc;
        LblStep2.Text                      = Loc.NtfyStep2;
        LblStep3.Text                      = Loc.NtfyStep3;
        LblNtfyTopic.Text                   = Loc.NtfyTopic;
        LblNtfyHint.Text                   = Loc.NtfyPlaceholder;
        LblNtfySecurityWarning.Text        = Loc.NtfySecurityWarning;
        ChkNtfySendFromThisPc.Content      = Loc.NtfySendFromThisPc;
        LblNtfySendFromThisPcHint.Text     = Loc.NtfySendFromThisPcHint;
        LblDisclaimer.Text                  = _vm.DisclaimerText;
        LblPollingInterval.Text             = Loc.PollingInterval;
        LblLanguageSection.Text             = Loc.LanguageSection;
        LangItemSystem.Content              = Loc.LanguageSystem;
        LblTrayDisplayMode.Text             = Loc.TrayDisplayMode;
        ChkHideInactive.Content             = Loc.HideInactiveProviders;
        LblVisibleProviders.Text            = Loc.VisibleProviders;
        ChkVisibleClaude.Content            = "Claude";
        ChkVisibleCodex.Content             = "Codex";
        ChkVisibleGemini.Content            = "Gemini CLI";
        ChkVisibleOpenCode.Content          = "OpenCode";

        // v1.27.0 표시 옵션 토글
        LblDisplayOptions.Text              = Loc.DisplayOptionsSection;
        ChkShowCodexPlanBadge.Content       = Loc.ShowCodexPlanBadge;
        ChkShowAbsoluteResetTime.Content    = Loc.ShowAbsoluteResetTime;
        LblShowAbsoluteResetTimeHint.Text   = Loc.ShowAbsoluteResetTimeHint;
        TrayItemAuto.Content                = Loc.CurrentLang switch
        {
            "ko" => "자동",
            "zh" => "自动",
            "ja" => "自動",
            _ => "Auto"
        };
        TabGeneral.Header = Loc.CurrentLang switch
        {
            "ko" => "일반",
            "zh" => "常规",
            "ja" => "一般",
            _ => "General"
        };
        TabTray.Header = Loc.CurrentLang switch
        {
            "ko" => "트레이",
            "zh" => "托盘",
            "ja" => "トレイ",
            _ => "Tray"
        };
        TabAlerts.Header = Loc.CurrentLang switch
        {
            "ko" => "알림",
            "zh" => "提醒",
            "ja" => "通知",
            _ => "Alerts"
        };
        TabNtfy.Header = "ntfy";

        // 새 ntfy 가이드 링크 — 이전 3줄 가이드를 단일 링크로 압축
        BtnNtfyDownload.Content = Loc.CurrentLang switch
        {
            "ko" => "ntfy 가이드 ↗",
            "zh" => "ntfy 指南 ↗",
            "ja" => "ntfy ガイド ↗",
            _ => "ntfy guide ↗"
        };

        // Footer "기본값 복원"
        BtnResetDefaults.Content = Loc.CurrentLang switch
        {
            "ko" => "기본값 복원",
            "zh" => "恢复默认",
            "ja" => "既定値に戻す",
            _ => "Reset defaults"
        };

        // 저장됨 인디케이터 툴팁 (시각적 ✓는 동일, 다국어 보조)
        LblSavedIndicator.ToolTip = Loc.CurrentLang switch
        {
            "ko" => "저장됨",
            "zh" => "已保存",
            "ja" => "保存済み",
            _ => "Saved"
        };
    }

    private void LoadValues()
    {
        _isLoadingValues = true;
        ChkEnabled.IsChecked          = _vm.NotificationsEnabled;
        ChkRateLimit.IsChecked        = _vm.NotifyRateLimit;
        ChkQuotaReset.IsChecked       = _vm.NotifyOnQuotaReset;
        Chk50.IsChecked               = _vm.Threshold50;
        Chk75.IsChecked               = _vm.Threshold75;
        Chk90.IsChecked               = _vm.Threshold90;
        Chk100.IsChecked              = _vm.Threshold100;
        TxtNtfyTopic.Text               = _vm.NtfyTopic;
        ChkNtfySendFromThisPc.IsChecked = _vm.NtfySendFromThisPc;
        ChkStartWithWindows.IsChecked   = IsStartupEnabled();
        SliderPolling.Value = _vm.PollingIntervalMinutes > 0 ? _vm.PollingIntervalMinutes : 2;
        UpdatePollingLabel((int)SliderPolling.Value);
        UpdateTrayAutoHelp();

        // Load Tray Display Mode
        foreach (ComboBoxItem item in CmbTrayDisplayMode.Items)
        {
            if (item.Tag?.ToString() == _vm.TrayDisplayMode)
            {
                CmbTrayDisplayMode.SelectedItem = item;
                break;
            }
        }
        if (CmbTrayDisplayMode.SelectedItem == null)
            CmbTrayDisplayMode.SelectedItem = TrayItemAuto;

        // Load Language
        var savedLang = _vm.SelectedLanguage ?? "system";
        foreach (ComboBoxItem item in CmbLanguage.Items)
        {
            if (item.Tag?.ToString() == savedLang)
            {
                CmbLanguage.SelectedItem = item;
                break;
            }
        }
        if (CmbLanguage.SelectedItem == null)
            CmbLanguage.SelectedItem = LangItemSystem;

        ChkHideInactive.IsChecked = _vm.HideInactiveProviders;

        ChkVisibleClaude.IsChecked   = _vm.IsClaudeEnabled;
        ChkVisibleCodex.IsChecked    = _vm.IsCodexEnabled;
        ChkVisibleGemini.IsChecked   = _vm.IsGeminiEnabled;
        ChkVisibleOpenCode.IsChecked = _vm.IsOpenCodeEnabled;

        ChkShowCodexPlanBadge.IsChecked    = _vm.ShowCodexPlanBadge;
        ChkShowAbsoluteResetTime.IsChecked = _vm.ShowAbsoluteResetTime;

        UpdateTrayModeAvailability();

        _isLoadingValues = false;
    }

    private void UpdatePollingLabel(int minutes)
    {
        TxtPollingValue.Text = minutes == 1 ? "1 min" : $"{minutes} min";
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingValues) return;

        _vm.NotificationsEnabled  = ChkEnabled.IsChecked == true;
        _vm.NotifyRateLimit       = ChkRateLimit.IsChecked == true;
        _vm.NotifyOnQuotaReset    = ChkQuotaReset.IsChecked == true;
        _vm.Threshold50           = Chk50.IsChecked == true;
        _vm.Threshold75           = Chk75.IsChecked == true;
        _vm.Threshold90           = Chk90.IsChecked == true;
        _vm.Threshold100          = Chk100.IsChecked == true;
        _vm.NtfySendFromThisPc    = ChkNtfySendFromThisPc.IsChecked == true;

        if (CmbTrayDisplayMode.SelectedItem is ComboBoxItem modeItem)
            _vm.TrayDisplayMode = modeItem.Tag?.ToString() ?? UsageProviderKind.Auto;

        UpdateTrayAutoHelp();
        _vm.HideInactiveProviders = ChkHideInactive.IsChecked == true;

        _vm.IsClaudeEnabled   = ChkVisibleClaude.IsChecked == true;
        _vm.IsCodexEnabled    = ChkVisibleCodex.IsChecked == true;
        _vm.IsGeminiEnabled   = ChkVisibleGemini.IsChecked == true;
        _vm.IsOpenCodeEnabled = ChkVisibleOpenCode.IsChecked == true;

        _vm.ShowCodexPlanBadge    = ChkShowCodexPlanBadge.IsChecked == true;
        _vm.ShowAbsoluteResetTime = ChkShowAbsoluteResetTime.IsChecked == true;

        UpdateTrayModeAvailability();
        _vm.SaveSettingsCommand.Execute(null);
        FlashSavedIndicator();
        _ = _vm.RefreshAsync();
    }

    private void NumberValidationTextBox(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        Regex regex = new("[^0-9]+");
        e.Handled = regex.IsMatch(e.Text);
    }

    private void TxtNtfyTopic_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ValidateAndSaveNtfyTopic())
            _vm.SaveSettingsCommand.Execute(null);
    }

    private void TxtNtfyTopic_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            if (ValidateAndSaveNtfyTopic())
                _vm.SaveSettingsCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static readonly Regex ValidTopicChars = new(@"^[a-z0-9_\-@.]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private bool ValidateAndSaveNtfyTopic()
    {
        var topic = TxtNtfyTopic.Text.Trim();

        if (string.IsNullOrEmpty(topic))
        {
            LblNtfySecurityWarning.Visibility = Visibility.Collapsed;
            _vm.NtfyTopic = "";
            return true;
        }

        if (!ValidTopicChars.IsMatch(topic))
        {
            LblNtfySecurityWarning.Text = Loc.NtfyTopicInvalidChars;
            LblNtfySecurityWarning.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(239, 68, 68));
            LblNtfySecurityWarning.Visibility = Visibility.Visible;
            return false;
        }

        if (topic.Length < 20)
        {
            LblNtfySecurityWarning.Text = Loc.NtfyTopicTooShort;
            LblNtfySecurityWarning.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(251, 191, 36));
            LblNtfySecurityWarning.Visibility = Visibility.Visible;
            return false;
        }

        LblNtfySecurityWarning.Visibility = Visibility.Collapsed;
        _vm.NtfyTopic = topic;
        return true;
    }

    private async void BtnTestNotification_Click(object sender, RoutedEventArgs e)
    {
        var hasNtfy = !string.IsNullOrWhiteSpace(_vm.NtfyTopic);
        var sendsNtfyFromThisPc = hasNtfy && _vm.NtfySendFromThisPc;

        var original = BtnTestNotification.Content;
        BtnTestNotification.IsEnabled = false;

        var result = await _vm.SendTestNotificationAsync();
        BtnTestNotification.Content = result.NtfyAttempted
            ? result.NtfySucceeded ? Loc.TestNotificationSent : Loc.TestNotificationFailedNtfy
            : hasNtfy && !sendsNtfyFromThisPc
                ? Loc.TestNotificationSentNtfyDisabled
                : Loc.TestNotificationSentNoNtfy;

        await Task.Delay(AppConstants.UiFeedbackDelayMs);
        BtnTestNotification.Content = original;
        BtnTestNotification.IsEnabled = true;
    }

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingValues) return;

        var enable = ChkStartWithWindows.IsChecked == true;
        SetStartup(enable);
        _vm.StartWithWindows = enable;
        _vm.SaveSettingsCommand.Execute(null);
    }

    private void BtnNtfyDownload_Click(object sender, RoutedEventArgs e)
    {
        const string GuideUrl = "https://github.com/jeiel85/claude-usage-tray-windows#ntfy-guide";
        Process.Start(new ProcessStartInfo(GuideUrl) { UseShellExecute = true });
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingValues) return;
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag?.ToString() is string langCode)
        {
            _vm.SelectedLanguage = langCode;
            Loc.SetLanguage(langCode);
            _vm.SaveSettingsCommand.Execute(null);
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();

    private void SliderPolling_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vm == null || _isLoadingValues) return;

        int minutes = (int)SliderPolling.Value;
        UpdatePollingLabel(minutes);
        _vm.PollingIntervalMinutes = minutes;
        _vm.SaveSettingsCommand.Execute(null);
        _vm.ApplyPollingInterval();
    }

    private void UpdateTrayAutoHelp()
    {
        if (LblTrayAutoHelp == null) return;

        bool isAuto = _vm.TrayDisplayMode == UsageProviderKind.Auto;
        if (!isAuto)
        {
            LblTrayAutoHelp.Visibility = Visibility.Collapsed;
            return;
        }

        string providerName = _vm.EffectiveTrayProvider switch
        {
            UsageProviderKind.Claude => "Claude",
            UsageProviderKind.Codex => "Codex",
            UsageProviderKind.GeminiCli => "Gemini CLI",
            UsageProviderKind.OpenCode => "OpenCode",
            _ => "Claude"
        };

        LblTrayAutoHelp.Text = Loc.CurrentLang switch
        {
            "ko" => $"자동 모드: 현재 '{providerName}' 표시 중",
            "zh" => $"自动模式：正在显示 '{providerName}'",
            "ja" => $"自動モード：'{providerName}' を表示中",
            _ => $"Auto mode: Showing '{providerName}'"
        };
        LblTrayAutoHelp.Visibility = Visibility.Visible;
    }

    public void ShowNearTray()
    {
        AnchorToTrayCorner();
        Show();
        Activate();
        // 첫 레이아웃 패스 후 ActualHeight 가 안정될 때 한 번 더 정렬 (showing 시점엔 부정확할 수 있음)
        Dispatcher.InvokeAsync(AnchorToTrayCorner,
            System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>
    /// 저장됨 인디케이터 ✓ 를 페이드인 → 1초 유지 → 페이드아웃 으로 잠깐 보여준다.
    /// 동일한 시점에 여러 변경이 발생해도 부드럽게 다시 트리거되도록 단일 애니메이션을 갱신한다.
    /// </summary>
    private void FlashSavedIndicator()
    {
        if (LblSavedIndicator == null) return;

        // 진행 중이던 애니메이션이 있다면 즉시 1로 고정시킨 뒤 새 사이클 시작
        LblSavedIndicator.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
        LblSavedIndicator.Opacity = 0;

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(150))
        };
        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(900),
            Duration = new Duration(TimeSpan.FromMilliseconds(400))
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(fadeIn, LblSavedIndicator);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(System.Windows.UIElement.OpacityProperty));
        Storyboard.SetTarget(fadeOut, LblSavedIndicator);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(System.Windows.UIElement.OpacityProperty));
        sb.Children.Add(fadeIn);
        sb.Children.Add(fadeOut);
        sb.Begin();
    }

    /// <summary>
    /// "Visible providers" 체크 상태에 따라 트레이 표시 기준 콤보의 해당 항목을 enable/disable.
    /// 켜져 있지 않은 공급자가 트레이 모드로 선택되어 있으면 자동으로 "자동" 으로 폴백.
    /// </summary>
    private void UpdateTrayModeAvailability()
    {
        if (TrayItemClaude   == null || TrayItemCodex     == null
         || TrayItemGemini   == null || TrayItemOpenCode  == null) return;

        TrayItemClaude.IsEnabled   = ChkVisibleClaude.IsChecked   == true;
        TrayItemCodex.IsEnabled    = ChkVisibleCodex.IsChecked    == true;
        TrayItemGemini.IsEnabled   = ChkVisibleGemini.IsChecked   == true;
        TrayItemOpenCode.IsEnabled = ChkVisibleOpenCode.IsChecked == true;

        // 현재 선택된 모드가 disable 상태가 되면 자동(Auto) 으로 폴백
        if (CmbTrayDisplayMode.SelectedItem is ComboBoxItem item &&
            item.IsEnabled == false)
        {
            CmbTrayDisplayMode.SelectedItem = TrayItemAuto;
        }
    }

    private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        // 안전한 기본값으로 일괄 복원 — 토픽 같은 사용자 입력값은 보존
        var preservedNtfyTopic = _vm.NtfyTopic;

        _isLoadingValues = true;
        try
        {
            // ViewModel 기본값 (NotificationSettings 의 디폴트와 정렬)
            _vm.NotificationsEnabled  = false;
            _vm.NotifyRateLimit       = true;
            _vm.NotifyOnQuotaReset    = false;
            _vm.Threshold50           = false;
            _vm.Threshold75           = true;
            _vm.Threshold90           = true;
            _vm.Threshold100          = true;
            _vm.PollingIntervalMinutes = 2;
            _vm.TrayDisplayMode       = UsageProviderKind.Auto;
            _vm.HideInactiveProviders = true;
            _vm.IsClaudeEnabled       = true;
            _vm.IsCodexEnabled        = true;
            _vm.IsGeminiEnabled       = true;
            _vm.IsOpenCodeEnabled     = true;
            _vm.NtfySendFromThisPc    = true;
            _vm.NtfyTopic             = preservedNtfyTopic; // 사용자 토픽 보존

            // UI 동기화
            ChkEnabled.IsChecked        = _vm.NotificationsEnabled;
            ChkRateLimit.IsChecked      = _vm.NotifyRateLimit;
            ChkQuotaReset.IsChecked     = _vm.NotifyOnQuotaReset;
            Chk50.IsChecked             = _vm.Threshold50;
            Chk75.IsChecked             = _vm.Threshold75;
            Chk90.IsChecked             = _vm.Threshold90;
            Chk100.IsChecked            = _vm.Threshold100;
            SliderPolling.Value         = _vm.PollingIntervalMinutes;
            UpdatePollingLabel(_vm.PollingIntervalMinutes);
            ChkHideInactive.IsChecked   = _vm.HideInactiveProviders;
            ChkVisibleClaude.IsChecked  = _vm.IsClaudeEnabled;
            ChkVisibleCodex.IsChecked   = _vm.IsCodexEnabled;
            ChkVisibleGemini.IsChecked  = _vm.IsGeminiEnabled;
            ChkVisibleOpenCode.IsChecked= _vm.IsOpenCodeEnabled;
            ChkNtfySendFromThisPc.IsChecked = _vm.NtfySendFromThisPc;
            CmbTrayDisplayMode.SelectedItem = TrayItemAuto;

            UpdateTrayModeAvailability();
            UpdateTrayAutoHelp();
        }
        finally
        {
            _isLoadingValues = false;
        }

        _vm.SaveSettingsCommand.Execute(null);
        FlashSavedIndicator();
        _ = _vm.RefreshAsync();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _disposed = true;
    }
}
