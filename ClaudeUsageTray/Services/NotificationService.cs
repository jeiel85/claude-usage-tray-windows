using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace ClaudeUsageTray.Services;

public class NotificationService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(AppConstants.PushTimeoutSeconds) };
    private static readonly string MachineName = Environment.MachineName;
    private readonly Func<NotifyIcon?> _getIcon;

    public NotificationService(Func<NotifyIcon?> getNotifyIcon)
    {
        _getIcon = getNotifyIcon;
    }

    public void ShowUsageAlert(int thresholdPercent, string windowLabel, string resetLabel, string ntfyTopic, string agent = "Claude", int priority = 3)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.NotificationBody(thresholdPercent, windowLabel, resetLabel, agent);

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body, priority);
    }

    public void ShowTestAlert(string ntfyTopic, string agent = "Claude", int priority = 3)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.TestNotificationBody;
        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body, priority);
    }

    public async Task<NotificationTestResult> ShowTestAlertAsync(string ntfyTopic, string agent = "Claude")
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.TestNotificationBody;
        ShowBalloon(title, body);

        if (string.IsNullOrWhiteSpace(ntfyTopic))
            return new NotificationTestResult(true, false, true, null);

        var ntfyOk = await SendNtfyAsync(ntfyTopic, title, body);
        return new NotificationTestResult(true, true, ntfyOk, ntfyOk ? null : Loc.NtfyTestSendFailed);
    }

    public void ShowRateLimitAlert(string ntfyTopic, int priority = 2)
    {
        var title = Loc.RateLimitTitle;
        var body  = Loc.RateLimited;

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body, priority);
    }

    public void ShowQuotaResetAlert(string ntfyTopic, int priority = 2)
    {
        var title = Loc.QuotaResetTitle;
        var body  = Loc.QuotaResetBody;

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body, priority);
    }

    public void ShowEarlyExhaustionAlert(string depletionTime, string resetTime, string ntfyTopic, int priority = 2)
    {
        var title = Loc.EarlyExhaustionTitle;
        var body  = Loc.EarlyExhaustionBody(depletionTime, resetTime);

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body, priority, []);
    }

    public void ShowWeatherAlert(string title, string body, string ntfyTopic,
        string? ntfyMessage = null, int priority = 4, string[]? tags = null,
        string? clickUrl = null)
    {
        ShowBalloon(title, body);
        SendNtfyWeather(ntfyTopic, title, ntfyMessage ?? body, priority, tags ?? ["sunny"],
            clickUrl);
    }

    private static void SendNtfyWeather(string topic, string title, string message,
        int priority, string[] tags, string? clickUrl)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;

        _ = Task.Run(async () =>
        {
            await SendNtfyWeatherAsync(topic, title, message, priority, tags, clickUrl);
        });
    }

    private static async Task<bool> SendNtfyWeatherAsync(string topic, string title,
        string message, int priority, string[] tags, string? clickUrl)
    {
        try
        {
            message = message + "\n" + "— " + MachineName;
            if (await IsRecentDuplicateAsync(topic, title, message)) return true;

            var payloadObj = new Dictionary<string, object?>
            {
                ["topic"] = topic.Trim(),
                ["title"] = title,
                ["message"] = message,
                ["priority"] = priority,
                ["tags"] = tags
            };
            if (!string.IsNullOrWhiteSpace(clickUrl))
                payloadObj["click"] = clickUrl;

            var payload = JsonSerializer.Serialize(payloadObj);
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
            System.Diagnostics.Debug.WriteLine($"[NotificationService] Ntfy weather failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
            return false;
        }
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

    private static void SendNtfy(string topic, string title, string body, int priority = 3, string[]? tags = null)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;

        // Fire-and-forget — don't block the UI
        _ = Task.Run(async () =>
        {
            await SendNtfyAsync(topic, title, body, priority, tags);
        });
    }

    private static async Task<bool> SendNtfyAsync(string topic, string title, string body, int priority = 3, string[]? tags = null)
    {
        try
        {
            body = body + "\n" + "— " + MachineName;
            // 여러 PC에서 동일 토픽 사용 시 중복 발송 방지: 최근 3분 내 동일 알림 확인
            if (await IsRecentDuplicateAsync(topic, title, body)) return true;

            // JSON API로 전송 — HTTP 헤더에 한국어 등 non-ASCII 문자를 넣으면
            // .NET이 FormatException을 던지므로 JSON body 방식을 사용
            var payload = JsonSerializer.Serialize(new
            {
                topic = topic.Trim(),
                title,
                message = body,
                priority,
                tags = tags ?? new[] { "bell" }
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
