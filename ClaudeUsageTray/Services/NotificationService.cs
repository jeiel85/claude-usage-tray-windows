using System.Net.Http;
using System.Text;
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
                var req = new HttpRequestMessage(HttpMethod.Post, $"https://ntfy.sh/{topic.Trim()}")
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/plain")
                };
                req.Headers.Add("Title", title);
                req.Headers.Add("Priority", "high");
                req.Headers.Add("Tags", "bell");

                await Http.SendAsync(req);
            }
            catch { }
        });
    }
}
