using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using ClaudeUsageTray.Models;
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
        LblTestNotificationHint.Text        = Loc.TestNotificationHint;
        LblThresholds.Text     = Loc.ThresholdsLabel;
        LblNtfyTitle.Text      = Loc.NtfyTitle;
        LblNtfyDesc.Text       = Loc.NtfyDesc;
        BtnNtfyDownload.Content = Loc.NtfyDownload;
        LblStep2.Text          = Loc.NtfyStep2;
        LblStep3.Text          = Loc.NtfyStep3;
        LblNtfyTopic.Text      = Loc.NtfyTopic;
        LblNtfyHint.Text       = Loc.NtfyPlaceholder;
        LblAccountsTitle.Text  = Loc.AccountsTitle;
        BtnAddAccount.Content  = Loc.AccountAdd;
        TxtNewAccountName.Tag  = Loc.AccountNamePlaceholder; // placeholder용
        LblAccountHint.Text    = Loc.AccountHint;
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
        RefreshAccountsList();
    }

    private void RefreshAccountsList()
    {
        var items = _vm.Accounts
            .Select((a, i) => new AccountListItem(i, a, i == _vm.ActiveAccountIndex))
            .ToList();
        AccountsList.ItemsSource = items;
    }

    private sealed record AccountListItem(int Index, AccountProfile Profile, bool IsActive)
    {
        public string DisplayLabel => IsActive
            ? $"✓  {Profile.Name}"
            : $"    {Profile.Name}";
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

    private void TxtNtfyTopic_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            _vm.NtfyTopic = TxtNtfyTopic.Text.Trim();
            _vm.SaveSettingsCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void BtnTestNotification_Click(object sender, RoutedEventArgs e)
    {
        var hasNtfy = !string.IsNullOrWhiteSpace(_vm.NtfyTopic);
        _vm.SendTestNotificationCommand.Execute(null);

        var original = BtnTestNotification.Content;
        BtnTestNotification.Content = hasNtfy ? Loc.TestNotificationSent : Loc.TestNotificationSentNoNtfy;
        BtnTestNotification.IsEnabled = false;
        await Task.Delay(2500);
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

    private void BtnAccountSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: int idx }) return;
        _ = _vm.SwitchAccountAsync(idx);
        RefreshAccountsList();
    }

    private void BtnAccountRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: int idx }) return;
        _vm.RemoveAccount(idx);
        RefreshAccountsList();
    }

    private void BtnAddAccount_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtNewAccountName.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        // 경로 선택 다이얼로그
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Claude 설정 폴더 선택 (~/.claude 또는 대체 경로)",
            UseDescriptionForTitle = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        string? selectedDir = null;
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            selectedDir = dialog.SelectedPath;

        _vm.AddAccount(new AccountProfile
        {
            Name = name,
            ClaudeBaseDir = selectedDir ?? ""
        });

        TxtNewAccountName.Text = "";
        RefreshAccountsList();
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
