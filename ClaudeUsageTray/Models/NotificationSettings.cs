namespace ClaudeUsageTray.Models;

public class NotificationSettings
{
    public bool Enabled { get; set; } = true;

    // 5시간 윈도우 임계값 (%)
    public List<int> Thresholds { get; set; } = [50, 75, 90, 100];

    public bool NotifyOnRateLimit { get; set; } = true;

    // ntfy.sh push notification
    public string NtfyTopic { get; set; } = "";

    // Discord/Slack webhook URL
    public string WebhookUrl { get; set; } = "";

    // Webhook 타입: "discord" 또는 "slack"
    public string WebhookType { get; set; } = "";

    public bool StartWithWindows { get; set; } = false;

    // 건너뛴 업데이트 버전 (예: "1.5.0")
    public string SkippedVersion { get; set; } = "";

    // 갱신 주기 (분 단위), 0이면 기본값 사용
    public int PollingIntervalMinutes { get; set; } = 0;
}
