using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ClaudeUsageTray.Services;

public class NotificationService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public void ShowUsageAlert(int thresholdPercent, string windowLabel, string resetLabel, string ntfyTopic)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.NotificationBody(thresholdPercent, windowLabel, resetLabel);

        ShowToast(title, body);
        SendNtfy(ntfyTopic, title, body);
    }

    public void ShowTestAlert(string ntfyTopic)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.TestNotificationBody;
        ShowToast(title, body);
        SendNtfy(ntfyTopic, title, body);
    }

    public void ShowRateLimitAlert(string ntfyTopic)
    {
        var title = Loc.RateLimitTitle;
        var body  = Loc.RateLimited;

        ShowToast(title, body);
        SendNtfy(ntfyTopic, title, body);
    }

    private static void ShowToast(string title, string body)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch { }
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
            catch { }
        });
    }
}
