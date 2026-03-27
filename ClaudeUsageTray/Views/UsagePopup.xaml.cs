using System.Windows;
using System.Windows.Input;
using ClaudeUsageTray.ViewModels;

namespace ClaudeUsageTray.Views;

public partial class UsagePopup : Window
{
    private readonly MainViewModel _vm;
    private SettingsWindow? _settingsWindow;

    public UsagePopup(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        Deactivated += (_, _) => Hide();
        MouseLeftButtonDown += (_, e) => DragMove();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await _vm.RefreshAsync();
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null)
            _settingsWindow = new SettingsWindow(_vm);

        if (_settingsWindow.IsVisible)
        {
            _settingsWindow.Hide();
        }
        else
        {
            Hide(); // 메인 팝업 닫고 설정 창 열기
            _settingsWindow.ShowNearTray();
        }
    }

    private async void UpdateBanner_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        await _vm.ApplyUpdateCommand.ExecuteAsync(null);
    }

    private void QuitBtn_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
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
}
