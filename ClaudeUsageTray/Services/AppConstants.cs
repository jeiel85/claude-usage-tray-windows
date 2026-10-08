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

    /// <summary>
    /// 같은 사건의 알림을 다른 PC 가 이미 보냈는지 확인하기 전의 무작위 대기 상한 (4초).
    /// ntfy.sh 는 발행한 메시지가 폴링에 보이기까지 1~1.6초쯤 걸린다(2026-09-30 4회 실측).
    /// 두 PC 가 같은 순간에 감지해도 한쪽이 상대의 발행을 볼 수 있을 만큼 서로의 확인 시점을 벌려 준다.
    /// </summary>
    public const int PushDedupeJitterMaxMs = 4_000;

    /// <summary>
    /// 중복 확인 때 토픽 캐시를 거슬러 볼 기간. ntfy 서버의 기본 캐시 보존(cache-duration)이 12시간이라
    /// 이보다 길게 물어도 더 나오지 않는다.
    /// </summary>
    public const string PushDedupeLookback = "12h";

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

    /// <summary>
    /// OAuth 토큰 갱신이 서버에서 실패했을 때의 첫 backoff (5분). 실패가 이어질 때마다 두 배로 늘린다.
    /// 백오프 없이 폴링마다 갱신을 두드리면 토큰 엔드포인트가 429 로 계속 막힌다(#177).
    /// </summary>
    public const int TokenRefreshBackoffBaseSeconds = 300;

    /// <summary>OAuth 토큰 갱신 backoff 상한 (60분)</summary>
    public const int TokenRefreshBackoffMaxSeconds = 3_600;

    /// <summary>네트워크 오류로 토큰 갱신 응답을 받지 못했을 때의 재시도 간격 (1분) — 서버에 닿지 않았으므로 짧게.</summary>
    public const int TokenRefreshNetworkRetrySeconds = 60;
}
