using System.Windows;
using System.Windows.Input;
using ClaudeUsageTray.ViewModels;

namespace ClaudeUsageTray.Views;

public partial class UsagePopup : Window
{
    private readonly MainViewModel _vm;

    public UsagePopup(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // Close on click outside
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

    private void QuitBtn_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    public void ShowNearTray()
    {
        // Position near the taskbar (bottom-right)
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 8;
        Top = workArea.Bottom - ActualHeight - 8;

        Show();
        Activate();

        // Recalculate after render
        Dispatcher.InvokeAsync(() =>
        {
            Left = workArea.Right - Width - 8;
            Top = workArea.Bottom - ActualHeight - 8;
        }, System.Windows.Threading.DispatcherPriority.Render);
    }
}
