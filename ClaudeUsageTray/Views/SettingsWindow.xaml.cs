using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        MouseLeftButtonDown += (_, e) => DragMove();
        Deactivated += (_, _) => Hide();

        ApplyLocalization();
        LoadValues();
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
        TitleText.Text                  = Loc.Notifications;
        LblGeneral.Text                 = Loc.NotificationsEnabled;
        ChkEnabled.Content              = Loc.NotificationsEnabled;
        ChkRateLimit.Content            = Loc.NotifyRateLimit;
        ChkStartWithWindows.Content         = Loc.StartWithWindows;
        BtnTestNotification.Content         = Loc.TestNotification;
        LblThresholds.Text     = Loc.ThresholdsLabel;
        LblNtfyTitle.Text      = Loc.NtfyTitle;
        LblNtfyDesc.Text       = Loc.NtfyDesc;
        BtnNtfyDownload.Content = Loc.NtfyDownload;
        LblStep2.Text          = Loc.NtfyStep2;
        LblStep3.Text          = Loc.NtfyStep3;
        LblNtfyTopic.Text      = Loc.NtfyTopic;
        LblNtfyHint.Text       = Loc.NtfyPlaceholder;
    }

    private void LoadValues()
    {
        ChkEnabled.IsChecked   = _vm.NotificationsEnabled;
        ChkRateLimit.IsChecked = _vm.NotifyRateLimit;
        Chk50.IsChecked        = _vm.Threshold50;
        Chk75.IsChecked        = _vm.Threshold75;
        Chk90.IsChecked        = _vm.Threshold90;
        Chk100.IsChecked       = _vm.Threshold100;
        TxtNtfyTopic.Text              = _vm.NtfyTopic;
        ChkStartWithWindows.IsChecked  = IsStartupEnabled();
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        _vm.NotificationsEnabled = ChkEnabled.IsChecked == true;
        _vm.NotifyRateLimit      = ChkRateLimit.IsChecked == true;
        _vm.Threshold50          = Chk50.IsChecked == true;
        _vm.Threshold75          = Chk75.IsChecked == true;
        _vm.Threshold90          = Chk90.IsChecked == true;
        _vm.Threshold100         = Chk100.IsChecked == true;
        _vm.SaveSettingsCommand.Execute(null);
    }

    private void TxtNtfyTopic_LostFocus(object sender, RoutedEventArgs e)
    {
        _vm.NtfyTopic = TxtNtfyTopic.Text.Trim();
        _vm.SaveSettingsCommand.Execute(null);
    }

    private void BtnTestNotification_Click(object sender, RoutedEventArgs e)
    {
        _vm.SendTestNotificationCommand.Execute(null);
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
}
