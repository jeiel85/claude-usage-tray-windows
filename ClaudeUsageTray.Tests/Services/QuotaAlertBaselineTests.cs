using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

// Codex 푸시 알림이 "엉뚱한 때" 나가던 문제(#173)의 회귀 테스트.
// 시간은 전부 고정 기준 시각(Now)에서 상대값으로 만들어 실행 시각에 좌우되지 않게 한다.
public class QuotaAlertBaselineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ResetA = Now.AddHours(3);
    private static readonly int[] AllThresholds = [50, 75, 90, 100];

    private static QuotaAlertResult Observe(
        QuotaAlertBaseline baseline, double percent, DateTimeOffset? resetAt,
        int[]? thresholds = null, bool estimated = false, DateTimeOffset? now = null, bool notifyReset = true) =>
        baseline.Observe(percent, resetAt, estimated, now ?? Now, thresholds ?? AllThresholds, notifyReset);

    [Fact]
    public void FirstReading_OnlyEstablishesBaseline()
    {
        var baseline = new QuotaAlertBaseline();

        var result = Observe(baseline, 0.95, ResetA);

        Assert.Empty(result.CrossedThresholds);
        Assert.False(result.QuotaReset);
        Assert.Equal(0.95, baseline.Percent);
    }

    // 사용자가 설정에서 끈 임계값(50/75)은 발송되지 않아야 한다 — 예전에는 {50,75,90,100} 이 하드코딩돼 있었다.
    [Fact]
    public void Crossing_ReportsOnlyEnabledThresholds()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 0.40, ResetA);

        var result = Observe(baseline, 0.92, ResetA, thresholds: [90, 100]);

        Assert.Equal([90], result.CrossedThresholds);
    }

    [Fact]
    public void Crossing_ReportsThresholdsInAscendingOrder()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 0.10, ResetA);

        var result = Observe(baseline, 1.0, ResetA, thresholds: [100, 50, 90, 75]);

        Assert.Equal([50, 75, 90, 100], result.CrossedThresholds);
    }

    // 시작 직후 조회가 비어(API 실패 + 로그 없음) 0% 로 기준선을 잡으면, 다음 정상 조회가 전부 "새로 넘은 것" 이 된다.
    [Fact]
    public void EmptyFirstReading_DoesNotCreateBaseline_SoNextReadingIsSilent()
    {
        var baseline = new QuotaAlertBaseline();

        var empty = Observe(baseline, 0, null);
        Assert.Empty(empty.CrossedThresholds);
        Assert.Equal(-1, baseline.Percent);

        var first = Observe(baseline, 0.92, ResetA);
        Assert.Empty(first.CrossedThresholds);
        Assert.False(first.QuotaReset);
    }

    // 창이 아직 끝나지 않았는데 데이터가 사라진 것(API 실패 + 로그 없음)은 초기화가 아니다.
    [Fact]
    public void DataLossMidWindow_KeepsBaseline_AndRecoveryDoesNotResendAlerts()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 0.40, ResetA);
        Observe(baseline, 0.92, ResetA);

        var lost = Observe(baseline, 0, null, now: Now.AddMinutes(2));
        Assert.Empty(lost.CrossedThresholds);
        Assert.False(lost.QuotaReset);
        Assert.Equal(0.92, baseline.Percent);

        var recovered = Observe(baseline, 0.92, ResetA, now: Now.AddMinutes(4));
        Assert.Empty(recovered.CrossedThresholds);
        Assert.False(recovered.QuotaReset);
    }

    // 100% 에서 조회가 끊겨도 "할당량 초기화됨" 이 나가면 안 된다.
    [Fact]
    public void DataLossAtFullQuota_DoesNotFireQuotaReset()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 1.0, ResetA);

        var result = Observe(baseline, 0, null, now: Now.AddMinutes(2));

        Assert.False(result.QuotaReset);
        Assert.Equal(1.0, baseline.Percent);
    }

    // API 실패로 로그 폴백이 낮은 옛 값을 주더라도(같은 창), 기준선이 내려가 다음 API 복구 때 재발송되면 안 된다.
    [Fact]
    public void LowerFallbackValueInSameWindow_IsIgnored()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 0.40, ResetA);
        Observe(baseline, 0.92, ResetA);

        // 로그의 resets_at 은 API 와 몇 초 어긋날 수 있다.
        var fallback = Observe(baseline, 0.60, ResetA.AddSeconds(1), now: Now.AddMinutes(2));
        Assert.Empty(fallback.CrossedThresholds);
        Assert.Equal(0.92, baseline.Percent);

        var recovered = Observe(baseline, 0.92, ResetA, now: Now.AddMinutes(4));
        Assert.Empty(recovered.CrossedThresholds);
    }

    [Fact]
    public void HigherValueInSameWindow_StillAlerts()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 0.60, ResetA);

        var result = Observe(baseline, 0.78, ResetA.AddSeconds(1), now: Now.AddMinutes(2));

        Assert.Equal([75], result.CrossedThresholds);
    }

    // 창이 시간상 끝났는데 새 데이터가 없다 = 진짜 초기화. 이전 동작(만료 창을 0% 로 버림)과 같아야 한다.
    [Fact]
    public void WindowEndedByTime_FiresQuotaResetOnce()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 1.0, ResetA);

        var after = ResetA.AddMinutes(1);
        var reset = Observe(baseline, 0, null, now: after);
        Assert.True(reset.QuotaReset);
        Assert.Equal(0, baseline.Percent);
        Assert.Null(baseline.ResetAt);

        var again = Observe(baseline, 0, null, now: after.AddMinutes(2));
        Assert.False(again.QuotaReset);
        Assert.Empty(again.CrossedThresholds);
    }

    // 서버가 창 도중 사용량을 되돌리면(리셋 시각이 새 창으로 바뀜) 낮은 값도 진짜다.
    [Fact]
    public void NewWindow_WithLowerPercent_IsARealReset()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 1.0, ResetA);

        var result = Observe(baseline, 0.03, Now.AddHours(5), now: Now.AddMinutes(2));

        Assert.True(result.QuotaReset);
        Assert.Equal(0.03, baseline.Percent);
    }

    [Fact]
    public void QuotaReset_IsSuppressedWhenDisabled_ButBaselineStillMoves()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 1.0, ResetA);

        var result = Observe(baseline, 0.03, Now.AddHours(5), now: Now.AddMinutes(2), notifyReset: false);

        Assert.False(result.QuotaReset);
        Assert.Equal(0.03, baseline.Percent);
    }

    // 추정 리셋은 실제 리셋과 몇 시간씩 어긋날 수 있어, 같은 창을 다른 창으로 오인하게 만들면 안 된다.
    [Fact]
    public void EstimatedReset_DoesNotChangeWindowIdentity()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 0.92, ResetA);

        var lower = Observe(baseline, 0.60, Now.AddHours(1), estimated: true, now: Now.AddMinutes(2));
        Assert.Empty(lower.CrossedThresholds);
        Assert.False(lower.QuotaReset);
        Assert.Equal(0.92, baseline.Percent);
        Assert.Equal(ResetA, baseline.ResetAt);

        var higher = Observe(baseline, 1.0, Now.AddHours(1), estimated: true, now: Now.AddMinutes(4));
        Assert.Equal([100], higher.CrossedThresholds);
        Assert.Equal(ResetA, baseline.ResetAt);
    }

    // 알림이 꺼져 있어도 기준선은 움직인다(호출자가 발송만 건너뜀) — 켠 직후 낡은 기준선과 비교하지 않도록.
    [Fact]
    public void Baseline_TracksReadings_EvenWithNoThresholds()
    {
        var baseline = new QuotaAlertBaseline();
        Observe(baseline, 0.20, ResetA, thresholds: []);

        var result = Observe(baseline, 0.95, ResetA, thresholds: []);

        Assert.Empty(result.CrossedThresholds);
        Assert.Equal(0.95, baseline.Percent);
    }
}
