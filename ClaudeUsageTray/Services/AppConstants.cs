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

    /// <summary>
    /// 업데이트 모달이 스스로 설치를 시작하기까지의 기본 대기 시간 (60초).
    /// 릴리즈 노트를 읽고 "건너뛰기"를 누를 여유는 주되, 자리를 비운 사이에도 업데이트가 끝나도록 한다.
    /// 설정에서 <see cref="MinAutoUpdateCountdownSeconds"/>~<see cref="MaxAutoUpdateCountdownSeconds"/> 범위로 바꿀 수 있다.
    /// </summary>
    public const int DefaultAutoUpdateCountdownSeconds = 60;

    /// <summary>자동 설치 대기 시간 하한 (10초) — 건너뛰기를 누를 최소한의 여유.</summary>
    public const int MinAutoUpdateCountdownSeconds = 10;

    /// <summary>자동 설치 대기 시간 상한 (5분).</summary>
    public const int MaxAutoUpdateCountdownSeconds = 300;

    /// <summary>
    /// 앱 시작 후 첫 업데이트 확인까지의 대기 시간 (5초).
    /// 부팅 직후 자동 실행되는 경우 네트워크 스택이 아직 올라오지 않은 상태에서 첫 요청이 나가는 것을 피한다.
    /// </summary>
    public const int StartupUpdateCheckDelayMs = 5_000;

    /// <summary>
    /// 시작 시 업데이트 확인이 네트워크 도달 불가/타임아웃으로 실패했을 때의 재시도 간격 (15초 → 1분 → 3분).
    /// 여기서 포기하면 다음 확인 기회는 <see cref="UpdateCheckIntervalMs"/>(24시간) 뒤가 된다.
    /// </summary>
    public static readonly int[] StartupUpdateCheckRetryDelaysMs = [15_000, 60_000, 180_000];

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
