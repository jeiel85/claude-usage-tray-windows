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

    public async Task<NotificationTestResult> ShowTestAlertAsync(string ntfyTopic)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.TestNotificationBody;
        ShowBalloon(title, body);

        if (string.IsNullOrWhiteSpace(ntfyTopic))
            return new NotificationTestResult(true, false, true, null);

        var ntfyOk = await SendNtfyAsync(ntfyTopic, title, body);
        return new NotificationTestResult(true, true, ntfyOk, ntfyOk ? null : Loc.NtfyTestSendFailed);
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
            await SendNtfyAsync(topic, title, body);
        });
    }

    private static async Task<bool> SendNtfyAsync(string topic, string title, string body)
    {
        try
        {
            // 여러 PC에서 동일 토픽 사용 시 중복 발송 방지: 최근 3분 내 동일 알림 확인
            if (await IsRecentDuplicateAsync(topic, title, body)) return true;

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
            using var resp = await Http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[NotificationService] Ntfy failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
            return false;
        }
    }

    private static async Task<bool> IsRecentDuplicateAsync(string topic, string title, string body)
    {
        try
        {
            var resp = await Http.GetStringAsync(
                $"https://ntfy.sh/{Uri.EscapeDataString(topic.Trim())}/json?poll=1&since=3m");

            // ntfy 응답은 NDJSON — 줄마다 하나의 메시지 JSON
            var titleJson = JsonSerializer.Serialize(title);   // 따옴표 포함 JSON 문자열
            var bodyJson  = JsonSerializer.Serialize(body);

            foreach (var line in resp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Contains($"\"title\":{titleJson}") &&
                    line.Contains($"\"message\":{bodyJson}"))
                    return true;
            }
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[NotificationService] Dedup check failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
        }
        return false;
    }
}

public record NotificationTestResult(
    bool WindowsToastAttempted,
    bool NtfyAttempted,
    bool NtfySucceeded,
    string? ErrorMessage);
