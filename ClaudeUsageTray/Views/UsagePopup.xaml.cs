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

public partial class UsagePopup : Window, IDisposable
{
    private readonly MainViewModel _vm;
    private SettingsWindow? _settingsWindow;
    private bool _showHourly = false;
    private bool _settingsOpen = false;
    private bool _disposed = false;

    public UsagePopup(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        Deactivated += (_, _) => { if (!_settingsOpen) Hide(); };
        MouseLeftButtonDown += (_, e) => { if (!e.Handled) DragMove(); };
        PreviewKeyDown += OnPreviewKeyDown;

        // v1.26.0: 컨텐츠 크기 변경(focus 토글) 시 우하단 앵커 자동 유지 — 작업표시줄 침범 방지
        SizeChanged += OnSizeChangedKeepAnchor;

        vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += (_, _) =>
        {
            ApplyMaxHeight();
            RefreshChart();
        };
        UpdateToggleStyle();
    }

    /// <summary>
    /// popup 의 컨텐츠 사이즈가 변경되면(예: 사용자가 컴팩트 행 클릭 → 다른 공급자 detail 펼침)
    /// 우하단 모서리 앵커를 다시 적용해 작업표시줄 위로 끌어올린다.
    /// </summary>
    private void OnSizeChangedKeepAnchor(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || !IsVisible) return;
        AnchorToTrayCorner();
    }

    /// <summary>
    /// popup 우하단(작업표시줄 위) 8px 마진 위치로 정렬. 화면 work area 밖으로 밀려나지 않도록 클램프.
    /// </summary>
    private void AnchorToTrayCorner()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 8;
        Top  = Math.Max(workArea.Top, workArea.Bottom - ActualHeight - 8);
    }

    /// <summary>
    /// popup 최대 높이를 현재 작업영역 높이로 클램프 — 극단적으로 작은 모니터/공급자 4개 활성 케이스 안전망.
    /// </summary>
    private void ApplyMaxHeight()
    {
        MaxHeight = SystemParameters.WorkArea.Height - 16;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.HistoryData) or nameof(MainViewModel.HourlyTokens))
            Dispatcher.Invoke(RefreshChart);
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
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _settingsWindow?.Dispose();
        }

        _disposed = true;
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
        Toggle7DayText.Text        = Loc.SevenDayToggle;
        ToggleHourlyBtn.Background = _showHourly ? activeBg : inactiveBg;
        ToggleHourlyText.Foreground = _showHourly ? activeFg : inactiveFg;
        ToggleHourlyText.Text      = Loc.TodayToggle;

        ChartTitleLabel.Text = _showHourly
            ? Services.Loc.HourlyChartTitle
            : Services.Loc.HistoryTitle;
    }

    private void RefreshChart()
    {
        if (_showHourly) DrawHourlyChart();
        else DrawHistoryChart();
    }

    private void HistoryCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 캔버스 폭이 잡히는 시점(또는 변할 때) 정확한 크기로 다시 그림 — Loaded/PropertyChanged 단계에서
        // ActualWidth 가 0 이라 폴백 폭으로 그린 자식이 카드 밖으로 튀어나오는 문제 방지
        if (e.WidthChanged) RefreshChart();
    }

    private void DrawHourlyChart()
    {
        HistoryCanvas.Children.Clear();
        var data = _vm.HourlyTokens;
        if (data == null) return;

        const double canvasH  = 60;
        const double barAreaH = 46;
        double canvasW = HistoryCanvas.ActualWidth;
        // 레이아웃 전에 그리면 폴백 폭이 카드 밖으로 삐져나옴 — SizeChanged 가 올 때 다시 그림
        if (canvasW < 10) return;

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
        double canvasW         = HistoryCanvas.ActualWidth;
        // 레이아웃 전에 그리면 폴백 폭이 카드 밖으로 삐져나옴 — SizeChanged 가 올 때 다시 그림
        if (canvasW < 10) return;

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

        var todayKey = DateTime.Now.ToString("yyyy-MM-dd");

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

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 또는 시스템 닫기 → 실제 종료 대신 숨김 처리
        e.Cancel = true;
        Hide();
    }

    protected override void OnSourceInitialized(EventArgs e) => base.OnSourceInitialized(e);

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn) btn.IsEnabled = false;
        await _vm.RefreshAsync();
        if (sender is System.Windows.Controls.Button b) b.IsEnabled = true;
    }

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

    private void QuitBtn_Click(object sender, RoutedEventArgs e) =>
        System.Windows.Application.Current.Shutdown();

    // ===== Agent focus 전환 핸들러 (v1.25.0 신규) =====
    // 클릭 시 해당 공급자만 상세 펼치고 나머지는 컴팩트 행으로 접힘. 영속화 자동.
    private void ClaudeFocus_Click(object sender, RoutedEventArgs e)   => SetFocusedProvider(Models.UsageProviderKind.Claude);
    private void CodexFocus_Click(object sender, RoutedEventArgs e)    => SetFocusedProvider(Models.UsageProviderKind.Codex);
    private void GeminiFocus_Click(object sender, RoutedEventArgs e)   => SetFocusedProvider(Models.UsageProviderKind.GeminiCli);
    private void OpenCodeFocus_Click(object sender, RoutedEventArgs e) => SetFocusedProvider(Models.UsageProviderKind.OpenCode);
    private void AntigravityFocus_Click(object sender, RoutedEventArgs e) => SetFocusedProvider(Models.UsageProviderKind.Antigravity);

    private void SetFocusedProvider(string provider)
    {
        // 같은 공급자 클릭 → 토글 off (모두 접기)
        if (_vm.FocusedProvider == provider)
        {
            _vm.FocusedProvider = "";
            _vm.SaveSettingsCommand.Execute(null);
            return;
        }
        _vm.FocusedProvider = provider;
        _vm.SaveSettingsCommand.Execute(null);
    }

    public void ShowNearTray()
    {
        ApplyMaxHeight();

        // 작은 화면(work area 높이 ≤ 800px)에서는 Claude 상세를 펼치면 팝업이 찌그러지므로
        // 모든 섹션을 접은 상태로 연다. 사용자가 원하는 공급자를 직접 클릭하여 펼칠 수 있다.
        if (SystemParameters.WorkArea.Height <= 800)
        {
            _vm.FocusedProvider = "";
        }

        AnchorToTrayCorner();
        Show();
        Activate();
        // 첫 레이아웃 패스 후 ActualHeight 가 안정될 때 한 번 더 정렬 (showing 시점엔 부정확할 수 있음)
        Dispatcher.InvokeAsync(AnchorToTrayCorner,
            System.Windows.Threading.DispatcherPriority.Render);
    }
}
