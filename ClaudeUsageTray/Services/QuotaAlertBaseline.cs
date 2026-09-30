namespace ClaudeUsageTray.Services;

/// <summary>
/// 한 번의 관측에서 발송해야 할 알림. 발송 여부(알림 설정)는 호출자가 결정한다.
/// <paramref name="EndedWindowResetAt"/> 은 초기화로 끝난 <b>창</b>의 서버 리셋 시각(추정치면 null)이다 —
/// 같은 계정의 여러 PC 는 모두 이 창을 지켜봤으므로, 초기화 알림을 언제 감지했든 같은 사건으로 알아볼 수 있는 값이다.
/// </summary>
internal readonly record struct QuotaAlertResult(
    IReadOnlyList<int> CrossedThresholds, bool QuotaReset, DateTimeOffset? EndedWindowResetAt = null)
{
    public static QuotaAlertResult None { get; } = new(Array.Empty<int>(), false);
}

/// <summary>
/// 임계값·초기화 알림이 "이전 값"으로 삼는 기준선을 관리한다.
///
/// 왜 따로 두는가: 예전에는 조회 결과가 무엇이든 곧바로 "이전 값"을 덮어썼다. 그러면 API 실패로
/// 로그 폴백(옛 값 또는 0%)이 들어온 순간 기준선이 무너지고, 다음 정상 조회에서 이미 보낸 임계값이
/// 다시 발송되거나(50/75/90 한꺼번에) 가짜 "초기화" 알림이 나갔다. 기준선은 "창을 설명하는 실제
/// 관측"으로만 움직여야 한다.
/// </summary>
internal sealed class QuotaAlertBaseline
{
    // 같은 창인지 가르는 허용 오차. API(reset_at)와 세션 로그(resets_at)는 같은 서버 값이라 사실상 일치하고,
    // 서로 다른 창은 최소 수 시간(5시간 창) 떨어져 있어 5분이면 둘을 안전하게 구분한다.
    private static readonly TimeSpan SameWindowTolerance = TimeSpan.FromMinutes(5);

    /// <summary>마지막으로 신뢰한 사용률(0~1). 아직 관측이 없으면 -1.</summary>
    public double Percent { get; private set; } = -1;

    /// <summary>마지막으로 신뢰한 창의 리셋 시각. 창이 끝나 0% 로 확정된 뒤에는 null.</summary>
    public DateTimeOffset? ResetAt { get; private set; }

    /// <summary>
    /// <see cref="ResetAt"/> 이 서버 값이 아니라 추정치인지. 추정 시각의 경과는 창이 끝났다는 증거가 못 되므로
    /// (실제 리셋과 몇 시간씩 어긋날 수 있다) "데이터 없음 → 진짜 초기화" 판정에서 제외하는 데 쓴다.
    /// </summary>
    public bool ResetIsEstimated { get; private set; }

    /// <summary>
    /// 관측 1건을 반영하고, 그 결과로 발송할 알림을 돌려준다.
    ///
    /// Input : percent/resetAt/isResetEstimated = 스냅샷의 단기 창 값, now = 판정 기준 시각,
    ///         thresholds = 사용자가 켠 임계값(%), notifyOnQuotaReset = 초기화 알림 사용 여부
    /// Output: 이번 관측으로 새로 넘은 임계값(오름차순)과 초기화 여부. 기준선이 움직이지 않으면 None.
    ///
    /// 핵심 로직(왜 이렇게 판정하는가):
    ///  - resetAt 이 없다 = 지금 창을 설명하는 데이터가 없다(조회 실패·로그 없음·창 만료). 이때의 0% 는
    ///    "실제로 0%" 가 아니라 "모름" 이므로 기준선에 쓰지 않는다. 단, 기준선의 창이 시간상 이미 끝났다면
    ///    그것은 진짜 초기화이므로 0% 로 확정한다.
    ///  - 같은 창(리셋 시각이 허용 오차 이내)인데 값이 내려갔다 = 사용률은 창 안에서 줄지 않으므로
    ///    지연·폴백 데이터다. 무시해야 API 복구 때 같은 임계값이 다시 발송되지 않는다.
    ///  - 창이 바뀌었다면(리셋 시각이 다르거나 이전 창이 끝남) 낮은 값도 진짜다 → 초기화로 본다.
    ///  - 추정 리셋(isResetEstimated)은 창의 정체성을 바꾸지 못한다. 추정치는 실제 리셋과 몇 시간씩
    ///    어긋날 수 있어, 그대로 쓰면 같은 창이 다른 창으로 오인된다.
    ///  - 같은 이유로 추정 리셋이 지났다는 사실만으로는 초기화를 확정하지 않는다. 데이터가 없을 때
    ///    "창이 끝났다" 고 볼 수 있는 근거는 서버가 준 리셋 시각의 경과뿐이다(추정 여부는 기준선이 기억한다).
    /// </summary>
    public QuotaAlertResult Observe(
        double percent,
        DateTimeOffset? resetAt,
        bool isResetEstimated,
        DateTimeOffset now,
        IReadOnlyCollection<int> thresholds,
        bool notifyOnQuotaReset)
    {
        var previousWindowEnded = ResetAt is { } previousReset && previousReset <= now;
        var resetEstimated = isResetEstimated;

        if (resetAt is null)
        {
            // 첫 관측이 비었으면 기준선을 만들지 않는다 — 0% 로 시작하면 다음 정상 조회가 전부 "새로 넘은 것" 이 된다.
            // 기준선의 리셋이 추정치면 그 경과는 증거가 아니다 → 기준선 유지(진짜 관측이 오면 그때 갈아탄다).
            if (Percent < 0 || !previousWindowEnded || ResetIsEstimated)
                return QuotaAlertResult.None;

            percent = 0;
        }
        else if (Percent >= 0 && !previousWindowEnded && ResetAt is { } known &&
                 (isResetEstimated || (resetAt.Value - known).Duration() <= SameWindowTolerance))
        {
            if (percent < Percent)
                return QuotaAlertResult.None;

            // 추정 리셋으로 기존 리셋 시각을 덮어쓰지 않는다. 기존 값의 추정 여부도 함께 물려받아야
            // 서버 값이 추정으로, 추정 값이 서버 값으로 둔갑하지 않는다.
            if (isResetEstimated)
            {
                resetAt = known;
                resetEstimated = ResetIsEstimated;
            }
        }

        var result = QuotaAlertResult.None;
        if (Percent >= 0)
        {
            var crossed = new List<int>();
            foreach (var threshold in thresholds.OrderBy(x => x))
            {
                var fraction = threshold / 100.0;
                if (Percent < fraction && percent >= fraction)
                    crossed.Add(threshold);
            }

            var quotaReset = notifyOnQuotaReset && Percent >= 1.0 && percent < 1.0;

            // 초기화 알림의 사건 식별자는 "감지한 시각" 이 아니라 "끝난 창" 이어야 한다. 초기화를 자고 넘겨 한참 뒤에 깬 PC 는
            // 다른 PC 가 이미 보낸 같은 초기화를 감지 시각으로는 알아볼 수 없다(허용 오차 30분 밖). 끝난 창의 서버 리셋 시각은
            // 두 PC 가 똑같이 알고 있다. 추정 리셋은 PC 마다 달라 식별자로 쓰지 않는다.
            result = new QuotaAlertResult(crossed, quotaReset, quotaReset && !ResetIsEstimated ? ResetAt : null);
        }

        Percent = percent;
        ResetAt = resetAt;
        ResetIsEstimated = resetAt is not null && resetEstimated;
        return result;
    }
}
