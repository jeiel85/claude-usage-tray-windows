namespace ClaudeUsageTray.Models;

public class NotificationSettings
{
    public string SelectedProvider { get; set; } = UsageProviderKind.Claude;

    public bool Enabled { get; set; } = true;

    // 5시간 윈도우 임계값 (%)
    public List<int> Thresholds { get; set; } = [50, 75, 90, 100];

    public bool NotifyOnRateLimit { get; set; } = true;

    public bool NotifyOnQuotaReset { get; set; } = true;

    // ntfy.sh push notification
    public string NtfyTopic { get; set; } = "";

    public bool StartWithWindows { get; set; } = false;

    // 건너뛴 업데이트 버전 (예: "1.5.0")
    public string SkippedVersion { get; set; } = "";

    // 갱신 주기 (분 단위), 0이면 기본값 사용
    public int PollingIntervalMinutes { get; set; } = 0;

    // ntfy 토픽 경고 표시 (중복 PC 경고)
    public bool NtfyTopicWarningShown { get; set; } = false;

    // 이 PC에서 ntfy 발송 여부 (여러 PC 중복 방지용)
    public bool NtfySendFromThisPc { get; set; } = true;

    // 표시 언어 ("system"=OS 따라가기, "ko", "en", "zh", "ja")
    public string Language { get; set; } = "system";
}
