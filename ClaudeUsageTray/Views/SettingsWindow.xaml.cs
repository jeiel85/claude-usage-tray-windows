using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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

        ApplyLocalization();
        LoadValues();
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
        BtnNtfyDownload.Content             = Loc.NtfyDownload;
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
        LblDailyGoalGemini.Text             = Loc.DailyTokenGoal;
        LblDailyGoalOpenCode.Text           = Loc.DailyTokenGoal;
        ChkHideInactive.Content             = Loc.HideInactiveProviders;
        TrayItemAuto.Content                = Loc.CurrentLang switch
        {
            "ko" => "자동",
            "zh" => "自动",
            "ja" => "自動",
            _ => "Auto"
        };
        TabAlerts.Header = Loc.CurrentLang switch
        {
            "ko" => "알림",
            "zh" => "提醒",
            "ja" => "通知",
            _ => "Alerts"
        };
        TabNtfy.Header = "ntfy";
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
        TxtGeminiGoal.Text = _vm.GeminiDailyTokenGoal.ToString();
        TxtOpenCodeGoal.Text = _vm.OpenCodeDailyTokenGoal.ToString();
        ChkHideInactive.IsChecked = _vm.HideInactiveProviders;

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

        // Select the saved language
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

        _vm.SaveSettingsCommand.Execute(null);
        _ = _vm.RefreshAsync();
    }

    private void TxtGoal_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoadingValues) return;
        if (long.TryParse(TxtGeminiGoal.Text, out var gGoal)) _vm.GeminiDailyTokenGoal = gGoal;
        if (long.TryParse(TxtOpenCodeGoal.Text, out var oGoal)) _vm.OpenCodeDailyTokenGoal = oGoal;
        _vm.SaveSettingsCommand.Execute(null);
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
        Process.Start(new ProcessStartInfo("https://ntfy.sh") { UseShellExecute = true });
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

        string providerName = _vm.SelectedProvider switch
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
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 8;
        Top  = workArea.Bottom - ActualHeight - 8;
        Show();
        Activate();
        Dispatcher.InvokeAsync(() =>
        {
            Left = workArea.Right - Width - 8;
            Top  = workArea.Bottom - ActualHeight - 8;
        }, System.Windows.Threading.DispatcherPriority.Render);
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
        }

        _disposed = true;
    }
}
