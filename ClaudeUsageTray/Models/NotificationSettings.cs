namespace ClaudeUsageTray.Models;

public class NotificationSettings
{
    public bool Enabled { get; set; } = true;

    // 5시간 윈도우 임계값 (%)
    public List<int> Thresholds { get; set; } = [50, 75, 90, 100];

    public bool NotifyOnRateLimit { get; set; } = true;

    // ntfy.sh push notification
    public string NtfyTopic { get; set; } = "";

    public bool StartWithWindows { get; set; } = false;

    // 건너뛴 업데이트 버전 (예: "1.5.0")
    public string SkippedVersion { get; set; } = "";
}
