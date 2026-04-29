using System.Text.Json.Serialization;

namespace ClaudeUsageTray.Models;

public class NotificationSettings
{
    public string SelectedProvider { get; set; } = UsageProviderKind.Claude;

    public bool Enabled { get; set; } = true;

    // 5시간 윈도우 임계값 (%)
    public bool Threshold50 { get; set; } = true;
    public bool Threshold75 { get; set; } = true;
    public bool Threshold90 { get; set; } = true;
    public bool Threshold100 { get; set; } = true;

    // 레이트 리밋 알림
    public bool NotifyRateLimit { get; set; } = true;

    // 할당량 리셋 알림 (100% 소진 후 리셋 시)
    public bool NotifyOnQuotaReset { get; set; } = true;

    // ntfy.sh 토픽 (비어있으면 비활성)
    public string NtfyTopic { get; set; } = "";

    // 윈도우 시작 시 자동 실행
    public bool StartWithWindows { get; set; } = false;

    // 폴링 간격 (분)
    public int PollingIntervalMinutes { get; set; } = 2;

    // 이 PC에서 ntfy 발송 여부 (여러 PC 중복 방지용)
    public bool NtfySendFromThisPc { get; set; } = true;

    // 표시 언어 ("system"=OS 따라가기, "ko", "en", "zh", "ja")
    public string Language { get; set; } = "system";

    // 트레이 아이콘 표시 기준 (auto, claude, codex, gemini-cli, opencode)
    public string TrayDisplayMode { get; set; } = "auto";

    // Gemini 일일 출력 토큰 목표 (사용량 % 계산용)
    public long GeminiDailyTokenGoal { get; set; } = 50000;

    // OpenCode 일일 출력 토큰 목표 (사용량 % 계산용)
    public long OpenCodeDailyTokenGoal { get; set; } = 100000;

    // 데이터가 없는 공급자 자동 숨김
    public bool HideInactiveProviders { get; set; } = true;

    // Backward compatibility with old schema used by ViewModel.
    [JsonIgnore]
    public bool NotifyOnRateLimit
    {
        get => NotifyRateLimit;
        set => NotifyRateLimit = value;
    }

    [JsonIgnore]
    public List<int> Thresholds
    {
        get
        {
            var list = new List<int>();
            if (Threshold50) list.Add(50);
            if (Threshold75) list.Add(75);
            if (Threshold90) list.Add(90);
            if (Threshold100) list.Add(100);
            return list;
        }
        set
        {
            var set = value ?? [];
            Threshold50 = set.Contains(50);
            Threshold75 = set.Contains(75);
            Threshold90 = set.Contains(90);
            Threshold100 = set.Contains(100);
        }
    }

    public string SkippedVersion { get; set; } = "";
}
