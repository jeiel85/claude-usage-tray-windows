using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using WColor  = System.Windows.Media.Color;
using WColors = System.Windows.Media.Colors;
using WPoint  = System.Windows.Point;
using WRect   = System.Windows.Shapes.Rectangle;
using LGBB    = System.Windows.Media.LinearGradientBrush;
using GSB     = System.Windows.Media.GradientStop;
using SCB     = System.Windows.Media.SolidColorBrush;

namespace ClaudeUsageTray.Views;

public partial class UsagePopup : Window
{
    private readonly MainViewModel _vm;
    private SettingsWindow? _settingsWindow;
    private bool _showHourly = false;
    private bool _settingsOpen = false;

    public UsagePopup(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        Deactivated += (_, _) => { if (!_settingsOpen) Hide(); };
        MouseLeftButtonDown += (_, e) => DragMove();
        PreviewKeyDown += OnPreviewKeyDown;

        vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += (_, _) => RefreshChart();
        UpdateToggleStyle();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.HistoryData) or nameof(MainViewModel.HourlyTokens))
            Dispatcher.Invoke(RefreshChart);
    }

    private void Toggle7DayBtn_Click(object sender, RoutedEventArgs e)
    {
        _showHourly = false;
        UpdateToggleStyle();
        RefreshChart();
    }

    private void ToggleHourlyBtn_Click(object sender, RoutedEventArgs e)
    {
        _showHourly = true;
        UpdateToggleStyle();
        RefreshChart();
    }

    private void UpdateToggleStyle()
    {
        var activeBg   = new SCB(WColor.FromRgb(45, 47, 69));
        var inactiveBg = new SCB(WColor.FromArgb(0, 0, 0, 0));
        var activeFg   = new SCB(WColor.FromRgb(167, 139, 250));
        var inactiveFg = new SCB(WColor.FromRgb(61, 66, 102));

        Toggle7DayBtn.Background   = _showHourly ? inactiveBg : activeBg;
        Toggle7DayText.Foreground  = _showHourly ? inactiveFg : activeFg;
        ToggleHourlyBtn.Background = _showHourly ? activeBg : inactiveBg;
        ToggleHourlyText.Foreground = _showHourly ? activeFg : inactiveFg;

        ChartTitleLabel.Text = _showHourly
            ? Services.Loc.HourlyChartTitle
            : Services.Loc.HistoryTitle;
    }

    private void RefreshChart()
    {
        if (_showHourly) DrawHourlyChart();
        else DrawHistoryChart();
    }

    private void DrawHourlyChart()
    {
        HistoryCanvas.Children.Clear();
        var data = _vm.HourlyTokens;
        if (data == null) return;

        const double canvasH  = 60;
        const double barAreaH = 46;
        double canvasW = HistoryCanvas.ActualWidth;
        if (canvasW < 10) canvasW = 288;

        int currentHour = DateTime.Now.Hour;
        int slotCount   = currentHour + 1; // 0 ~ 현재시각

        long maxVal = 0;
        for (int h = 0; h <= currentHour; h++)
            if (data[h] > maxVal) maxVal = data[h];
        if (maxVal == 0) maxVal = 1;

        double slot = canvasW / slotCount;
        double gap  = Math.Max(1, slot * 0.12);
        double barW = slot - gap;

        var grad = new LGBB(
            WColor.FromRgb(139, 92, 246),
            WColor.FromRgb(99, 102, 241),
            new WPoint(0, 0), new WPoint(0, 1));

        for (int h = 0; h <= currentHour; h++)
        {
            double ratio = (double)data[h] / maxVal;
            double barH  = Math.Max(data[h] > 0 ? 3 : 0, ratio * barAreaH);
            double x     = h * slot + gap / 2;
            bool isNow   = h == currentHour;

            // Background bar
            var bg = new WRect
            {
                Width = barW, Height = barAreaH,
                Fill = new SCB(WColor.FromRgb(45, 47, 69)),
                RadiusX = 2, RadiusY = 2
            };
            Canvas.SetLeft(bg, x); Canvas.SetTop(bg, 0);
            HistoryCanvas.Children.Add(bg);

            // Fill bar
            if (barH > 0)
            {
                var fill = new WRect
                {
                    Width = barW, Height = barH,
                    Fill = isNow
                        ? (System.Windows.Media.Brush)grad
                        : new SCB(WColor.FromArgb(160, 99, 102, 241)),
                    RadiusX = 2, RadiusY = 2
                };
                Canvas.SetLeft(fill, x); Canvas.SetTop(fill, barAreaH - barH);
                HistoryCanvas.Children.Add(fill);
            }

            // Hour label — 0, 6, 12, 18시 + 현재 시간
            bool showLabel = h % 6 == 0 || isNow;
            if (showLabel && slot >= 8)
            {
                var label = new TextBlock
                {
                    Text = $"{h}",
                    FontSize = 9,
                    Foreground = isNow
                        ? new SCB(WColor.FromRgb(167, 139, 250))
                        : new SCB(WColor.FromRgb(61, 66, 102)),
                    Width = slot,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(label, h * slot);
                Canvas.SetTop(label, barAreaH + 2);
                HistoryCanvas.Children.Add(label);
            }
        }

        HistoryCanvas.Height = canvasH;
    }

    private void DrawHistoryChart()
    {
        HistoryCanvas.Children.Clear();
        var data = _vm.HistoryData;
        if (data == null || data.Count == 0) return;

        const double canvasH   = 60;
        const double barAreaH  = 46;
        const double labelH    = 14;
        double canvasW         = HistoryCanvas.ActualWidth;
        if (canvasW < 10) canvasW = 288; // fallback before layout

        int count   = data.Count;
        double slot = canvasW / count;
        double gap  = Math.Max(2, slot * 0.15);
        double barW = slot - gap;

        long maxTotal = data.Max(s => s.InputTokens + s.OutputTokens + s.CacheReadTokens + s.CacheWriteTokens);
        if (maxTotal == 0) maxTotal = 1;

        var grad = new LGBB(
            WColor.FromRgb(139, 92, 246),
            WColor.FromRgb(99, 102, 241),
            new WPoint(0, 0), new WPoint(0, 1));

        var todayKey = DateTime.UtcNow.ToString("yyyy-MM-dd");

        for (int i = 0; i < count; i++)
        {
            var s = data[i];
            long total = s.InputTokens + s.OutputTokens + s.CacheReadTokens + s.CacheWriteTokens;
            double ratio   = (double)total / maxTotal;
            double barH    = Math.Max(3, ratio * barAreaH);
            double x       = i * slot + gap / 2;

            // Background bar
            var bg = new WRect
            {
                Width = barW, Height = barAreaH,
                Fill = new SCB(WColor.FromRgb(45, 47, 69)),
                RadiusX = 3, RadiusY = 3
            };
            Canvas.SetLeft(bg, x);
            Canvas.SetTop(bg, 0);
            HistoryCanvas.Children.Add(bg);

            // Fill bar
            var fill = new WRect
            {
                Width = barW, Height = barH,
                Fill = s.Date == todayKey
                    ? (System.Windows.Media.Brush)grad
                    : new SCB(WColor.FromArgb(180, 99, 102, 241)),
                RadiusX = 3, RadiusY = 3
            };
            Canvas.SetLeft(fill, x);
            Canvas.SetTop(fill, barAreaH - barH);
            HistoryCanvas.Children.Add(fill);

            // Date label (MM/dd)
            var label = new TextBlock
            {
                Text = s.Date.Length >= 10 ? s.Date[5..] : s.Date,
                FontSize = 9,
                Foreground = s.Date == todayKey
                    ? new SCB(WColor.FromRgb(167, 139, 250))
                    : new SCB(WColor.FromRgb(61, 66, 102)),
                Width = slot,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(label, i * slot);
            Canvas.SetTop(label, barAreaH + 2);
            HistoryCanvas.Children.Add(label);
        }

        HistoryCanvas.Height = canvasH;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool isEsc   = e.Key == Key.Escape;
        bool isCtrlW = e.Key == Key.W && (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool isAltF4 = e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        if (isEsc || isCtrlW || isAltF4)
        {
            Hide();
            e.Handled = true;
        }
    }

    protected override void OnSourceInitialized(EventArgs e) => base.OnSourceInitialized(e);

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await _vm.RefreshAsync();

    private void ExportCsvBtn_Click(object sender, RoutedEventArgs e) =>
        _vm.ExportCsvCommand.Execute(null);

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_vm);
            _settingsWindow.IsVisibleChanged += (_, ev) =>
            {
                if (!(bool)ev.NewValue)
                {
                    _settingsOpen = false;
                    ShowNearTray();
                }
            };
        }

        if (_settingsWindow.IsVisible)
        {
            _settingsOpen = false;
            _settingsWindow.Hide();
        }
        else
        {
            _settingsOpen = true;
            _settingsWindow.ShowNearTray();
        }
    }

    private async void UpdateBanner_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => await _vm.ApplyUpdateCommand.ExecuteAsync(null);

    private void QuitBtn_Click(object sender, RoutedEventArgs e) =>
        System.Windows.Application.Current.Shutdown();

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
