using System.Text.Json.Serialization;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Forecasts;

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

    // 데이터가 없는 공급자 자동 숨김
    public bool HideInactiveProviders { get; set; } = true;

    // 명시적으로 표시할 공급자 목록 (기본값: 전체)
    public List<string> VisibleProviders { get; set; } = [
        UsageProviderKind.Claude,
        UsageProviderKind.Codex,
        UsageProviderKind.GeminiCli,
        UsageProviderKind.OpenCode
    ];

    // 현재 popup에서 펼쳐 보여주는(=focused) 공급자.
    // 빈 문자열이면 MainViewModel이 자동 결정 (Claude 우선, 비활성 시 활성된 첫 공급자).
    // 사용자가 popup의 다른 공급자 행을 클릭하면 해당 키로 갱신되어 영속화된다.
    public string FocusedProvider { get; set; } = "";

    // v1.27.0 표시 옵션 토글
    // 구독 등급 배지 ("Claude Max 5x", "ChatGPT Plus" 등) 표시 여부 — v1.41.0 부터 전 공급자 공통
    public bool ShowPlanBadge { get; set; } = true;

    /// <summary>
    /// v1.40.x 까지 쓰던 Codex 전용 키. 예전 설정 파일을 그대로 읽고(끄기 상태 보존),
    /// 새로 저장할 때도 같은 값을 함께 써 이전 버전으로 되돌아가도 토글이 유지되도록 한다.
    /// </summary>
    [JsonPropertyName("ShowCodexPlanBadge")]
    public bool ShowCodexPlanBadgeCompat
    {
        get => ShowPlanBadge;
        set => ShowPlanBadge = value;
    }

    // 리셋 라벨에 절대 시각을 병기할지 ("1h 23m 후 리셋 (18:30)")
    public bool ShowAbsoluteResetTime { get; set; } = false;

    public bool KeepPopupAboveTaskbar { get; set; } = false;
    public double UsagePanelOpacity { get; set; } = 0.94;

    public bool UsageSyncEnabled { get; set; } = false;
    public string UsageSyncFolderPath { get; set; } = "";
    public int UsageSyncApiSnapshotTtlMinutes { get; set; } = 5;
    public int UsageSyncLocalSnapshotTtlHours { get; set; } = 24;

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

    // 새 버전을 카운트다운 후 자동으로 설치할지. 끄면 업데이트 창은 그대로 뜨되 직접 실행해야 한다.
    public bool AutoUpdateEnabled { get; set; } = true;

    // 자동 설치까지의 대기 시간(초). 0 이하이면 기본값을 쓰고, 허용 범위를 벗어나면 잘라낸다.
    public int AutoUpdateCountdownSeconds { get; set; } = 60;

    // 카운트다운 만료로 "자동" 적용을 시도한 버전. 재시작 후에도 이 버전이 여전히 최신으로 남아 있으면
    // 적용이 실패한 것이므로 자동 재시도를 멈추고 수동 실행을 요구한다(다운로드-재시작 루프 방지).
    // 적용이 반영되면(현재 버전 >= 기록 버전) 시작 시 자동으로 비워진다.
    public string AutoUpdateAttemptedVersion { get; set; } = "";

    // 403 + "currently not allowed" (조직 OAuth API 미활성) 첫 감지 시각 (UTC).
    // null = 현재 미감지 / 정상. 값이 있고 24h 이상 경과하면 안내문이 에스컬레이션 톤으로 전환된다.
    public DateTime? OAuthNotAllowedFirstSeenUtc { get; set; }

    // ===== 날씨 설정 (v1.29.0) =====
    public bool WeatherEnabled { get; set; } = false;
    public bool WeatherShowInTrayTooltip { get; set; } = true;
    public string WeatherLocationMode { get; set; } = "manual";
    public string WeatherLocationName { get; set; } = "";
    public string WeatherCountryCode { get; set; } = "";
    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }
    public string WeatherTimezone { get; set; } = "auto";
    public int WeatherRefreshIntervalMinutes { get; set; } = 30;
    public bool WeatherDailyForecastEnabled { get; set; } = true;
    public string WeatherDailyForecastTime { get; set; } = "07:30";
    public bool WeatherConditionAlertsEnabled { get; set; } = true;
    public int WeatherRainProbabilityThreshold { get; set; } = 70;
    public double WeatherHighTemperatureThresholdC { get; set; } = 33;
    public double WeatherLowTemperatureThresholdC { get; set; } = -10;
    public double WeatherWindSpeedThresholdKmh { get; set; } = 50;
    public bool WeatherOfficialAlertsEnabled { get; set; } = true;

    // ===== 날씨 데이터 소스 (v1.37.0) =====

    /// <summary>
    /// 예보 provider 선택. "auto" 면 순서대로 시도하고, provider Id 면 그 소스를 우선한다.
    /// 어느 값이든 응답이 없으면 다른 provider 로 폴백한다.
    /// </summary>
    public string WeatherForecastSource { get; set; } = WeatherService.AutoSource;

    /// <summary>
    /// Open-Meteo 예보 모델. "best_match" 면 좌표별 최적 모델을 API 가 고른다.
    /// 모델 선택을 지원하지 않는 provider 에서는 무시된다.
    /// </summary>
    public string WeatherForecastModel { get; set; } = OpenMeteoForecastProvider.AutoModel;
}
