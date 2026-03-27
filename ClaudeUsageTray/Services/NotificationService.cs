using Microsoft.Toolkit.Uwp.Notifications;

namespace ClaudeUsageTray.Services;

public class NotificationService
{
    public void ShowUsageAlert(int thresholdPercent, string windowLabel, string resetLabel)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(Loc.NotificationTitle)
                .AddText(Loc.NotificationBody(thresholdPercent, windowLabel, resetLabel))
                .Show();
        }
        catch { }
    }

    public void ShowRateLimitAlert()
    {
        try
        {
            new ToastContentBuilder()
                .AddText(Loc.RateLimitTitle)
                .AddText(Loc.RateLimited)
                .Show();
        }
        catch { }
    }
}
