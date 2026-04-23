using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.Views;

public partial class SettingsWindow : Window, IDisposable
{
    private readonly MainViewModel _vm;
    private bool _disposed = false;

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        MouseLeftButtonDown += (_, e) => DragMove();
        Deactivated += (_, _) => Hide();
        PreviewKeyDown += OnPreviewKeyDown;

        ApplyLocalization();
        LoadValues();
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
        LblDisclaimer.Text                  = Loc.Disclaimer;
        LblPollingInterval.Text             = Loc.PollingInterval;
    }

    private void LoadValues()
    {
        ChkEnabled.IsChecked          = _vm.NotificationsEnabled;
        ChkRateLimit.IsChecked        = _vm.NotifyRateLimit;
        ChkQuotaReset.IsChecked       = _vm.NotifyQuotaReset;
        Chk50.IsChecked               = _vm.Threshold50;
        Chk75.IsChecked               = _vm.Threshold75;
        Chk90.IsChecked               = _vm.Threshold90;
        Chk100.IsChecked              = _vm.Threshold100;
        TxtNtfyTopic.Text             = _vm.NtfyTopic;
        ChkStartWithWindows.IsChecked = IsStartupEnabled();
        SliderPolling.Value = _vm.PollingIntervalMinutes > 0 ? _vm.PollingIntervalMinutes : 2;
        UpdatePollingLabel((int)SliderPolling.Value);
    }

    private void UpdatePollingLabel(int minutes)
    {
        TxtPollingValue.Text = minutes == 1 ? "1 min" : $"{minutes} min";
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        _vm.NotificationsEnabled = ChkEnabled.IsChecked == true;
        _vm.NotifyRateLimit      = ChkRateLimit.IsChecked == true;
        _vm.NotifyQuotaReset     = ChkQuotaReset.IsChecked == true;
        _vm.Threshold50          = Chk50.IsChecked == true;
        _vm.Threshold75          = Chk75.IsChecked == true;
        _vm.Threshold90          = Chk90.IsChecked == true;
        _vm.Threshold100         = Chk100.IsChecked == true;
        _vm.SaveSettingsCommand.Execute(null);
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
        _vm.SendTestNotificationCommand.Execute(null);

        var original = BtnTestNotification.Content;
        BtnTestNotification.Content = hasNtfy ? Loc.TestNotificationSent : Loc.TestNotificationSentNoNtfy;
        BtnTestNotification.IsEnabled = false;
        await Task.Delay(AppConstants.UiFeedbackDelayMs);
        BtnTestNotification.Content = original;
        BtnTestNotification.IsEnabled = true;
    }

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        var enable = ChkStartWithWindows.IsChecked == true;
        SetStartup(enable);
        _vm.StartWithWindows = enable;
        _vm.SaveSettingsCommand.Execute(null);
    }

    private void BtnNtfyDownload_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://ntfy.sh") { UseShellExecute = true });
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();

    private void SliderPolling_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vm == null) return;
        int minutes = (int)SliderPolling.Value;
        UpdatePollingLabel(minutes);
        _vm.PollingIntervalMinutes = minutes;
        _vm.SaveSettingsCommand.Execute(null);
        _vm.ApplyPollingInterval();
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
            // 정리할 리소스가 있으면 여기서 처리
        }

        _disposed = true;
    }
}
