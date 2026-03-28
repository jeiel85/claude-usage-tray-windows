namespace ClaudeUsageTrayMobile.Models;

public class AppSettings
{
    public bool NotificationsEnabled { get; set; } = true;
    public List<int> Thresholds { get; set; } = [50, 75, 90, 100];
    public bool NotifyOnRateLimit { get; set; } = true;
    public string NtfyTopic { get; set; } = "";
}
