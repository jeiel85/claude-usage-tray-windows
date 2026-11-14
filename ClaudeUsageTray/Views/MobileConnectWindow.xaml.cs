using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Mobile;
using QRCoder;

namespace ClaudeUsageTray.Views;

/// <summary>
/// "휴대폰 연결" 창(Homebase <c>PhoneConnectPanel</c> 대응, #220): 일회용 페어링 QR, 지금 열린 경로, 방화벽 규칙,
/// 연결된 휴대폰 목록과 해제를 한 화면에 둔다.
/// </summary>
public partial class MobileConnectWindow : Window
{
    private const string ReleasesUrl = "https://github.com/jeiel85/claude-usage-tray-windows/releases/latest";

    private readonly MobileLinkService _mobile;
    private readonly DispatcherTimer _tick;
    private PairingOffer? _offer;
    private string _offerPathsKey = "";
    private bool _firewallBusy;
    private bool? _firewallInstalled;
    private string? _firewallMessage;

    public MobileConnectWindow(MobileLinkService mobile)
    {
        InitializeComponent();
        _mobile = mobile;

        TitleText.Text = Loc.MobileConnectTitle;
        HintText.Text = Loc.MobileScanHint;
        NewQrButton.Content = Loc.MobileNewQr;
        CopyCodeButton.Content = Loc.PeerSyncCopyCode;
        PathsHeader.Text = Loc.MobilePathsHeader;
        FirewallButton.Content = Loc.MobileFirewallAdd;
        DevicesHeader.Text = Loc.MobileDevicesHeader;
        NoDevicesText.Text = Loc.MobileNoDevices;
        GetAppButton.Content = Loc.MobileGetApp;
        CloseButton.Content = Loc.MobileClose;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => RefreshView();
        _mobile.Changed += OnMobileChanged;

        Loaded += (_, _) =>
        {
            RefreshView();
            RefreshDevices();
            _ = CheckFirewallAsync();
            _tick.Start();
        };
        Closed += (_, _) =>
        {
            _tick.Stop();
            _mobile.Changed -= OnMobileChanged;
            // 화면에서 사라진 QR 이 5분 동안 유효할 이유가 없다.
            _mobile.CancelPairing();
        };
    }

    /// <summary>서버 상태·기기 목록 변경은 스레드풀에서 올 수 있어 UI 스레드로 넘긴다.</summary>
    private void OnMobileChanged() =>
        Dispatcher.BeginInvoke(() =>
        {
            RefreshView();
            RefreshDevices();
        });

    /// <summary>
    /// Input : 없음(서버 상태를 읽는다)
    /// Output: QR·경로·상태 문구 갱신
    /// 핵심 로직: 경로 목록이 바뀌면(Wi-Fi 전환·Tailscale 로그인·주소 변경) QR 을 새로 만든다 — 옛 경로만 담긴 QR 로
    ///           페어링하면 휴대폰이 닿지 않는 주소를 들고 간다. 만료된 QR 은 자동으로 바꾸지 않는다(사용자가 "새 QR" 을
    ///           눌러야 한다) — 창을 띄워 둔 채 자리를 비운 동안 코드가 계속 새로 발급되지 않게.
    /// </summary>
    private void RefreshView()
    {
        var server = _mobile.Server;
        var status = server?.Status();
        var endpoints = status?.Endpoints ?? [];
        var pathsKey = string.Join("|", endpoints.Select(e => $"{e.Transport}:{e.Host}:{e.Port}"));

        if (endpoints.Count > 0 && (_offer is null || pathsKey != _offerPathsKey))
            IssueOffer(pathsKey);
        else if (endpoints.Count == 0 && _offer is not null)
            ClearOffer();

        PathsList.ItemsSource = endpoints.Select(DescribeEndpoint).ToList();

        string? problem = null;
        if (status is null || !status.Running) problem = Loc.MobileStatusOff;
        else if (!string.IsNullOrEmpty(status.LastError)) problem = Loc.MobileServerError(status.LastError);
        else if (!status.Listening) problem = Loc.MobileStarting;
        // 켜 둔 Cloudflare 터널이 아직 열리는 중이면 "경로 없음" 대신 기다리라고 알린다(몇 초 걸린다).
        else if (endpoints.Count == 0 && _mobile.Tunnel.Status() is { State: CloudflareStatus.Starting }) problem = Loc.CloudflareStarting;
        else if (endpoints.Count == 0) problem = Loc.MobileNoPath;
        StatusText.Text = problem ?? "";
        StatusText.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;

        var watching = Loc.MobileWatching(status?.Watchers ?? 0);
        WatchingText.Text = watching;
        WatchingText.Visibility = string.IsNullOrEmpty(watching) ? Visibility.Collapsed : Visibility.Visible;

        UpdateCountdown();
        UpdateFirewallText();
    }

    private void IssueOffer(string pathsKey)
    {
        _offer = _mobile.Server?.CreateOffer();
        _offerPathsKey = _offer is null ? "" : pathsKey;
        QrImage.Source = _offer is null ? null : RenderQr(PairingLink.Build(_offer));
        QrImage.Opacity = 1;
    }

    private void ClearOffer()
    {
        _offer = null;
        _offerPathsKey = "";
        QrImage.Source = null;
        _mobile.CancelPairing();
    }

    private void UpdateCountdown()
    {
        if (_offer is null)
        {
            CountdownText.Text = "";
            return;
        }
        var left = (int)Math.Ceiling((_offer.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        if (left <= 0)
        {
            CountdownText.Text = Loc.MobileQrExpired;
            QrImage.Opacity = 0.2;
        }
        else
        {
            CountdownText.Text = Loc.MobileQrExpiresIn(left);
        }
    }

    private static string DescribeEndpoint(MobileEndpoint e) => e.Transport switch
    {
        MobileTransport.Lan => $"LAN · {e.Host}:{e.Port} · TLS",
        MobileTransport.Tailscale => $"Tailscale · {e.Host}:{e.Port}",
        MobileTransport.Cloudflare => e.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase)
            ? $"Cloudflare · {e.Host} · {Loc.CloudflareQuickSuffix}"
            : $"Cloudflare · {e.Host}",
        _ => $"{e.Transport} · {e.Host}:{e.Port}",
    };

    /// <summary>
    /// QR 은 화면 표시용이라 오류 정정 수준을 L 로 둔다(Homebase M-20) — 링크(수백 자)가 들어가도 모듈 수가 작아
    /// 휴대폰 카메라가 빨리 읽는다. 화면 QR 은 인쇄물처럼 훼손될 일이 없다.
    /// </summary>
    private static BitmapImage RenderQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
        var png = new PngByteQRCode(data).GetGraphic(6);
        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void RefreshDevices()
    {
        var devices = _mobile.Devices?.List() ?? [];
        var now = DateTimeOffset.Now;
        DevicesList.ItemsSource = devices.Select(d => new DeviceRow(
            d.DeviceId,
            string.IsNullOrEmpty(d.Model) || d.Model == d.Name ? d.Name : $"{d.Name} · {d.Model}",
            (d.Kind == MobileDeviceKind.Pc ? Loc.MobileDevicesPcSuffix + " · " : "")
                + (d.LastSeen is { } seen ? Loc.MobileDeviceSeen(UsageCalculator.FormatObservedAt(seen, now)) : Loc.MobileDeviceNeverSeen),
            Loc.MobileRevoke)).ToList();
        NoDevicesText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>netsh 를 띄우는 확인이라(수백 ms) UI 스레드에서 돌리지 않는다.</summary>
    private async Task CheckFirewallAsync()
    {
        var port = _mobile.Server?.Options.Port ?? MobileProtocol.DefaultPort;
        var exe = Environment.ProcessPath;
        _firewallInstalled = await Task.Run(() => MobileFirewall.AreRulesInstalled(port, exe));
        UpdateFirewallText();
    }

    private void UpdateFirewallText()
    {
        if (_firewallInstalled is null)
        {
            FirewallText.Text = "";
            FirewallButton.Visibility = Visibility.Collapsed;
            return;
        }
        FirewallText.Text = _firewallMessage ?? (_firewallInstalled == true ? Loc.MobileFirewallOk : Loc.MobileFirewallMissing);
        FirewallButton.Visibility = _firewallInstalled == true ? Visibility.Collapsed : Visibility.Visible;
        FirewallButton.IsEnabled = !_firewallBusy;
    }

    private async void FirewallButton_Click(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (_firewallBusy || string.IsNullOrEmpty(exe)) return;
        _firewallBusy = true;
        _firewallMessage = null;
        UpdateFirewallText();
        try
        {
            var port = _mobile.Server?.Options.Port ?? MobileProtocol.DefaultPort;
            var result = await MobileFirewall.InstallElevatedAsync(port, exe);
            _firewallMessage = result switch
            {
                MobileFirewall.InstallResult.Cancelled => Loc.MobileFirewallCancelled,
                MobileFirewall.InstallResult.Failed => Loc.MobileFirewallFailed,
                _ => null,
            };
            await CheckFirewallAsync();
        }
        finally
        {
            _firewallBusy = false;
            UpdateFirewallText();
        }
    }

    /// <summary>지금 보이는 QR 의 내용(일회용 링크)을 다른 PC 에 붙여 넣을 동기화 코드로 복사한다.</summary>
    private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_offer is null || _offer.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _offer = null;
            RefreshView();
        }
        if (_offer is null)
        {
            ShowCopyResult(Loc.PeerSyncCodeUnavailable);
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(PairingLink.Build(_offer));
            ShowCopyResult(Loc.PeerSyncCopied);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            ShowCopyResult(Loc.PeerSyncCopyFailed);
        }
    }

    private void ShowCopyResult(string text)
    {
        CopyCodeResult.Text = text;
        CopyCodeResult.Visibility = Visibility.Visible;
    }

    private void NewQrButton_Click(object sender, RoutedEventArgs e)
    {
        _offer = null;
        RefreshView();
    }

    private void RevokeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string deviceId }) return;
        var device = _mobile.Devices?.List().FirstOrDefault(d => d.DeviceId == deviceId);
        if (device is null) return;
        var answer = DarkMessageBox.Show(Loc.MobileConnectTitle, Loc.MobileRevokeConfirm(device.Name), Loc.MobileRevoke, Loc.MobileClose);
        if (answer != DarkMessageBoxResult.Confirm) return;
        _mobile.Devices?.Revoke(deviceId);
        RefreshDevices();
    }

    private void GetAppButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            GC.KeepAlive(ex);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record DeviceRow(string DeviceId, string Title, string Detail, string RevokeLabel);
}
