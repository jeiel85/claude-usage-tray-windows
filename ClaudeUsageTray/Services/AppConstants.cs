namespace ClaudeUsageTray.Services;

/// <summary>
/// Centralized constants for the application.
/// </summary>
internal static class AppConstants
{
    // ============== 폴링 간격 ==============
    /// <summary>API 폴링 간격 (2분)</summary>
    public const int PollingIntervalMs = 120_000;

    /// <summary>자동 업데이트 확인 간격 (24시간)</summary>
    public const int UpdateCheckIntervalMs = 86_400_000;

    // ============== HTTP 타임아웃 ==============
    /// <summary>API 요청 타임아웃 (10초)</summary>
    public const int ApiTimeoutSeconds = 10;

    /// <summary>인증 요청 타임아웃 (15초)</summary>
    public const int AuthTimeoutSeconds = 15;

    /// <summary>푸시 알림 타임아웃 (5초)</summary>
    public const int PushTimeoutSeconds = 5;

    /// <summary>날씨 API 요청 타임아웃 (10초)</summary>
    public const int WeatherTimeoutSeconds = 10;

    // ============== 알림 임계값 ==============
    /// <summary>기본 알림 임계값</summary>
    public static readonly int[] DefaultThresholds = [50, 75, 90, 100];

    // ============== 히스토리 ==============
    /// <summary>히스토리 보관 기간 (90일)</summary>
    public const int HistoryRetentionDays = 90;

    /// <summary>히스토리 차트 표시 일수</summary>
    public const int HistoryChartDays = 7;

    // ============== 파일 감시 ==============
    /// <summary>파일 쓰기 디바운스 시간 (500ms)</summary>
    public const int FileWriteDebounceMs = 500;

    // ============== UI ==============
    /// <summary>설정창 버튼 피드백 표시 시간 (2.5초)</summary>
    public const int UiFeedbackDelayMs = 2500;

    // ============== 업데이트 ==============
    /// <summary>업데이트 배치 스크립트 대기 시간 (2초)</summary>
    public const int UpdateDelaySeconds = 2;

    // ============== API 백오프 ==============
    /// <summary>429 응답에 Retry-After 헤더가 없거나 파싱 실패한 경우의 기본 backoff (5분)</summary>
    public const int DefaultRateLimitBackoffSeconds = 300;

    /// <summary>
    /// 403 permission_error 발생 시의 backoff (90분).
    /// 신규 계정 검증/조직 OAuth API 활성화 같은 일시 차단 케이스에서 활성화 시점을 너무 늦게 잡지 않도록
    /// 6시간(이전 값) 보다 짧게 잡는다. 90분이면 24시간 활성화 윈도우에서 ~6% 미만 지연 보장.
    /// </summary>
    public const int PermissionDeniedBackoffSeconds = 5_400;
}
