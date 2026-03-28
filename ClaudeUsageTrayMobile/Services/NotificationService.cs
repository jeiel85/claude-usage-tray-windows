using System.Net.Http;
using System.Text;
using Plugin.LocalNotification;

namespace ClaudeUsageTrayMobile.Services;

/// <summary>
/// Sends local notifications via Plugin.LocalNotification and optional ntfy.sh push.
/// </summary>
public class NotificationService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private int _notificationId = 100;

    public async Task ShowUsageAlertAsync(int thresholdPercent, string windowLabel, string resetLabel, string ntfyTopic)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.NotificationBody(thresholdPercent, windowLabel, resetLabel);
        await ShowLocalNotificationAsync(title, body);
        _ = SendNtfyAsync(ntfyTopic, title, body);
    }

    public async Task ShowTestAlertAsync(string ntfyTopic)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.TestNotificationBody;
        await ShowLocalNotificationAsync(title, body);
        _ = SendNtfyAsync(ntfyTopic, title, body);
    }

    public async Task ShowRateLimitAlertAsync(string ntfyTopic)
    {
        var title = Loc.RateLimitTitle;
        var body  = Loc.RateLimited;
        await ShowLocalNotificationAsync(title, body);
        _ = SendNtfyAsync(ntfyTopic, title, body);
    }

    private async Task ShowLocalNotificationAsync(string title, string body)
    {
        try
        {
            var notification = new NotificationRequest
            {
                NotificationId = _notificationId++,
                Title = title,
                Description = body,
                Android = new AndroidOptions { ChannelId = "claude_usage", Priority = AndroidPriority.High },
                iOS = new iOSOptions { }
            };
            await LocalNotificationCenter.Current.Show(notification);
        }
        catch { }
    }

    private static async Task SendNtfyAsync(string topic, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;
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
    }
}
