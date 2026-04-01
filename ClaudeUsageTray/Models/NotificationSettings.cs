namespace ClaudeUsageTray.Models;

public class AccountProfile
{
    public string Name { get; set; } = "Default";

    /// <summary>
    /// Path to the Claude base directory (e.g. C:\Users\you\.claude).
    /// Empty string means the default ~/.claude directory.
    /// </summary>
    public string ClaudeBaseDir { get; set; } = "";
}

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

    // 다중 계정 지원
    public List<AccountProfile> Accounts { get; set; } = [];
    public int ActiveAccountIndex { get; set; } = 0;
}
