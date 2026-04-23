using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace ClaudeUsageTray.Services;

public class NotificationService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(AppConstants.PushTimeoutSeconds) };
    private readonly Func<NotifyIcon?> _getIcon;

    public NotificationService(Func<NotifyIcon?> getNotifyIcon)
    {
        _getIcon = getNotifyIcon;
    }

    public void ShowUsageAlert(int thresholdPercent, string windowLabel, string resetLabel, string ntfyTopic)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.NotificationBody(thresholdPercent, windowLabel, resetLabel);

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body);
    }

    public void ShowTestAlert(string ntfyTopic)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.TestNotificationBody;
        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body);
    }

    public void ShowRateLimitAlert(string ntfyTopic)
    {
        var title = Loc.RateLimitTitle;
        var body  = Loc.RateLimited;

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body);
    }

    public void ShowQuotaResetAlert(string ntfyTopic)
    {
        var title = Loc.QuotaResetTitle;
        var body  = Loc.QuotaResetBody;

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body);
    }

    private void ShowBalloon(string title, string body)
    {
        try
        {
            _getIcon()?.ShowBalloonTip(4000, title, body, ToolTipIcon.None);
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[NotificationService] Balloon failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
        }
    }

    private static void SendNtfy(string topic, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;

        // Fire-and-forget — don't block the UI
        _ = Task.Run(async () =>
        {
            try
            {
                // JSON API로 전송 — HTTP 헤더에 한국어 등 non-ASCII 문자를 넣으면
                // .NET이 FormatException을 던지므로 JSON body 방식을 사용
                var payload = JsonSerializer.Serialize(new
                {
                    topic = topic.Trim(),
                    title,
                    message = body,
                    priority = 4,
                    tags = new[] { "bell" }
                });
                var req = new HttpRequestMessage(HttpMethod.Post, "https://ntfy.sh/")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                await Http.SendAsync(req);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[NotificationService] Ntfy failed: {ex.Message}");
#endif
                GC.KeepAlive(ex);
            }
        });
    }
}
