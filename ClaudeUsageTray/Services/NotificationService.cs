using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using ClaudeUsageTray.Services.Mobile;

namespace ClaudeUsageTray.Services;

public class NotificationService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(AppConstants.PushTimeoutSeconds) };
    private static readonly string MachineName = Environment.MachineName;
    private readonly Func<NotifyIcon?> _getIcon;

    public NotificationService(Func<NotifyIcon?> getNotifyIcon)
    {
        _getIcon = getNotifyIcon;
    }

    /// <summary>
    /// 휴대폰 앱 푸시 경로(#222). 앱이 QR 로 연결되면 구독을 자동 등록하므로 토픽을 입력할 필요가 없다.
    /// 앱 시작 때 휴대폰 연동 서비스가 들어온다(그 전에는 null — 기존 토픽만 쓴다).
    /// </summary>
    public IAppPushSender? AppPush { get; set; }

    /// <summary>"이 PC 에서 휴대폰 알림 보내기" — 끄면 앱 푸시도 기존 토픽도 보내지 않는다(여러 PC 중 하나만 보내게 할 때).</summary>
    public Func<bool> SendsPhoneAlerts { get; set; } = () => true;

    // windowId/windowResetAt/accountId: 여러 PC 가 같은 토픽을 쓸 때 "같은 사건" 을 가르는 값이다(#175).
    // windowLabel 은 표시 언어에 따라 PC 마다 달라지므로 키에 쓰지 않고, 창의 리셋 시각은 서버 값이라 PC 간에 같다.
    // accountId 는 모니터링 중인 계정의 식별자 원문(해시는 키 쪽에서 한다) — 계정이 다른 PC 의 알림이 서로를 삼키지 않게 한다.
    // resetLabel 은 리셋 시각을 모를 때(Codex 추정 리셋)만 본문에 쓴다. pushAccount 는 휴대폰 앱이 계정으로 알림을 구분하게 한다(#262).
    public void ShowUsageAlert(int thresholdPercent, string windowLabel, string resetLabel, string ntfyTopic,
        string agent = "Claude", int priority = 3, string windowId = "short", DateTimeOffset? windowResetAt = null,
        string? accountId = null, PushAccount? pushAccount = null)
    {
        var now = DateTimeOffset.Now;
        var title = AlertText.UsageTitle(agent, windowLabel, thresholdPercent);
        var body  = AlertText.UsageBody(thresholdPercent, windowResetAt, resetLabel, now);

        ShowBalloon(title, body);
        SendNtfy(PushCategories.Usage, ntfyTopic, title, body, priority,
            key: NtfyEventKey.ForUsage(agent, windowId, thresholdPercent, windowResetAt, now, accountId), account: pushAccount);
    }

    // 테스트 알림은 키를 붙이지 않는다 — 눌러 볼 때마다 실제로 도착해야 "테스트" 가 된다.
    public void ShowTestAlert(string ntfyTopic, string agent = "Claude", int priority = 3)
    {
        var title = Loc.TestNotification;
        var body  = Loc.TestNotificationBody;
        ShowBalloon(title, body);
        SendNtfy(PushCategories.Test, ntfyTopic, title, body, priority);
    }

    public async Task<NotificationTestResult> ShowTestAlertAsync(string ntfyTopic, string agent = "Claude")
    {
        var title = Loc.TestNotification;
        var body  = Loc.TestNotificationBody;
        ShowBalloon(title, body);

        // 테스트는 두 경로를 모두 확인한다 — 앱 푸시가 됐다고 토픽 경로가 되는지는 알 수 없으므로.
        AppPushResult? push = null;
        if (AppPush is { HasTargets: true } sender && SendsPhoneAlerts())
            push = await sender.SendAsync(PushCategories.Test, title, body, 3, null, TimeSpan.Zero, null);

        if (string.IsNullOrWhiteSpace(ntfyTopic))
            return new NotificationTestResult(true, false, true, null, push);

        var ntfyOk = await SendNtfyAsync(ntfyTopic, title, body);
        return new NotificationTestResult(true, true, ntfyOk, ntfyOk ? null : Loc.NtfyTestSendFailed, push);
    }

    // eventAt: 로그에 찍힌 레이트 리밋 기록의 시각. 재시작 뒤 같은 기록을 다시 읽어도 같은 사건으로 알아보게 하는 기준이다.
    public void ShowRateLimitAlert(string ntfyTopic, int priority = 2, string? accountId = null, DateTimeOffset? eventAt = null,
        PushAccount? pushAccount = null)
    {
        var title = Loc.RateLimitTitle;
        var body  = Loc.RateLimitAlertBody;

        ShowBalloon(title, body);
        SendNtfy(PushCategories.RateLimit, ntfyTopic, title, body, priority,
            key: NtfyEventKey.ForRateLimit(eventAt, DateTimeOffset.Now, accountId), account: pushAccount);
    }

    // endedWindowResetAt: 초기화로 끝난 창의 서버 리셋 시각. 초기화를 자고 넘겨 늦게 깬 PC 도 다른 PC 가 보낸 같은 초기화를 알아보게 한다.
    public void ShowQuotaResetAlert(string ntfyTopic, string agent = "Claude", int priority = 2, string? accountId = null,
        DateTimeOffset? endedWindowResetAt = null, PushAccount? pushAccount = null)
    {
        var title = Loc.QuotaResetTitle(agent);
        var body  = Loc.QuotaResetBody(agent);

        ShowBalloon(title, body);
        SendNtfy(PushCategories.Reset, ntfyTopic, title, body, priority,
            key: NtfyEventKey.ForReset(agent, endedWindowResetAt, DateTimeOffset.Now, accountId), account: pushAccount);
    }

    // depletionAt: 예상 소진 시각. PC 마다 몇 분씩 어긋나므로 키에서는 허용 오차로 흡수한다. resetAt: 지금 5시간 창의 서버 리셋 시각.
    public void ShowEarlyExhaustionAlert(DateTimeOffset depletionAt, DateTimeOffset resetAt, string ntfyTopic, int priority = 2,
        string? accountId = null, PushAccount? pushAccount = null)
    {
        var now = DateTimeOffset.Now;
        var title = Loc.EarlyExhaustionAlertTitle;
        var body  = AlertText.EarlyExhaustionBody(depletionAt, resetAt, now);

        ShowBalloon(title, body);
        SendNtfy(PushCategories.Exhaustion, ntfyTopic, title, body, priority, [],
            NtfyEventKey.ForEarlyExhaustion("Claude", depletionAt, now, accountId), account: pushAccount);
    }

    // dedupeKey: 날씨 알림이 PC 안에서 이미 쓰는 중복 방지 키(위치·날짜 포함). 없으면(테스트 알림) 중복 확인을 건너뛴다.
    public void ShowWeatherAlert(string title, string body, string ntfyTopic,
        string? ntfyMessage = null, int priority = 4, string[]? tags = null,
        string? clickUrl = null, string? dedupeKey = null)
    {
        ShowBalloon(title, body);
        SendNtfy(PushCategories.Weather, ntfyTopic, title, ntfyMessage ?? body, priority, tags ?? ["sunny"], clickUrl: clickUrl,
            key: dedupeKey is null ? null : NtfyEventKey.ForExact("weather", dedupeKey));
    }

    private void ShowBalloon(string title, string body)
    {
        try
        {
            _getIcon()?.ShowBalloonTip(4000, title, body, ToolTipIcon.None);
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[NotificationService] Balloon failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
        }
    }

    /// <summary>
    /// 휴대폰 알림 1건을 보낸다(#222). 기존 체계(평문 ntfy 토픽)를 앱 푸시로 옮긴 핵심 경로다.
    ///
    /// Input : 범주·토픽(이 PC 에서 보내지 않거나 토픽이 없으면 빈 문자열)·제목·본문·우선순위·사건 키
    /// Output: 없음(백그라운드 전송)
    /// 핵심 로직: 앱 푸시를 등록한 휴대폰이 있으면 그쪽(종단 암호화)으로만 보낸다 — 같은 알림이 앱과 ntfy 앱에 두 번 뜨지 않게.
    ///           앱 푸시가 모두 실패했거나 등록된 휴대폰이 없을 때만 기존 토픽으로 보낸다(iOS·앱 미설치 사용자 호환).
    ///           모든 휴대폰이 그 범주를 껐으면(AllMuted) 사용자의 선택이므로 토픽으로도 보내지 않는다.
    /// </summary>
    private void SendNtfy(string category, string topic, string title, string body, int priority = 3, string[]? tags = null,
        NtfyEventKey? key = null, string? clickUrl = null, PushAccount? account = null)
    {
        var push = SendsPhoneAlerts() ? AppPush : null;
        if (string.IsNullOrWhiteSpace(topic) && push is not { HasTargets: true }) return;

        // Fire-and-forget — don't block the UI
        _ = Task.Run(async () =>
        {
            if (push is { HasTargets: true })
            {
                AppPushResult result;
                try
                {
                    // 계정은 종단 암호화되는 앱 푸시에만 싣는다 — 아래 평문 토픽에는 계정 이름을 보내지 않는다.
                    result = await push.SendAsync(category, title, body, priority, key?.SequenceId,
                        key?.Tolerance ?? TimeSpan.Zero, clickUrl, account);
                }
                catch (Exception ex)
                {
                    GC.KeepAlive(ex);
                    result = AppPushResult.Failed;
                }
                if (result is AppPushResult.Delivered or AppPushResult.AllMuted) return;
            }
            if (!string.IsNullOrWhiteSpace(topic))
                await SendNtfyAsync(topic, title, body, priority, tags, key, clickUrl);
        });
    }

    private static Task<bool> SendNtfyAsync(string topic, string title, string body, int priority = 3,
        string[]? tags = null, NtfyEventKey? key = null, string? clickUrl = null) =>
        PublishAsync(topic, title, body, priority, tags ?? ["bell"], clickUrl, key);

    /// <summary>
    /// ntfy 로 알림 1건을 발행한다.
    ///
    /// Input : 토픽·제목·본문·우선순위·태그·클릭 URL, 그리고 사건 키(없으면 중복 확인 없이 항상 발송)
    /// Output: 발송했거나 다른 PC 가 이미 보내서 건너뛰었으면 true, 실패면 false
    /// 핵심 로직(왜 이렇게 했는가):
    ///  - 중복 판정은 본문이 아니라 사건 키로 한다. 본문에는 PC 이름·상대 시간이 들어 있어 다른 PC 와 절대 일치하지 않는다(#175).
    ///  - 확인 전에 무작위로 기다린다. 발행한 메시지가 폴링에 보이기까지 1~1.6초가 걸려서, 같은 순간 확인하면 서로가 안 보인다.
    ///  - 그래도 동시에 보내는 경우를 위해 같은 사건은 같은 sequence_id 로 발행한다. Android·웹 클라이언트는 이를 한 알림으로 합친다
    ///    (ntfy 문서: "Supported on: Android, Web" — iOS 는 해당 없음).
    ///  - 확인이 실패하면(네트워크 등) 중복 걱정보다 알림을 잃는 쪽이 나쁘므로 그냥 보낸다.
    ///  - 서버가 sequence_id 를 거부(400)하면 키 없이 한 번 더 보낸다. 중복 방지는 부가 기능이고 전달이 본기능이다.
    /// </summary>
    private static async Task<bool> PublishAsync(string topic, string title, string message,
        int priority, string[] tags, string? clickUrl, NtfyEventKey? key)
    {
        try
        {
            message = message + "\n" + "— " + MachineName;

            if (key is not null)
            {
                await Task.Delay(Random.Shared.Next(AppConstants.PushDedupeJitterMaxMs));
                if (await IsAlreadySentAsync(topic, key)) return true;
            }

            // JSON API로 전송 — HTTP 헤더에 한국어 등 non-ASCII 문자를 넣으면
            // .NET이 FormatException을 던지므로 JSON body 방식을 사용
            var payload = new Dictionary<string, object?>
            {
                ["topic"] = topic.Trim(),
                ["title"] = title,
                ["message"] = message,
                ["priority"] = priority,
                ["tags"] = tags
            };
            if (!string.IsNullOrWhiteSpace(clickUrl))
                payload["click"] = clickUrl;
            if (key is not null)
                payload["sequence_id"] = key.SequenceId;

            var status = await PostAsync(payload);
            if (status == HttpStatusCode.BadRequest && payload.Remove("sequence_id"))
                status = await PostAsync(payload);

            return (int)status is >= 200 and < 300;
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[NotificationService] Ntfy failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
            return false;
        }
    }

    private static async Task<HttpStatusCode> PostAsync(Dictionary<string, object?> payload)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://ntfy.sh/")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        using var resp = await Http.SendAsync(req);
        return resp.StatusCode;
    }

    private static async Task<bool> IsAlreadySentAsync(string topic, NtfyEventKey key)
    {
        try
        {
            var resp = await Http.GetStringAsync(
                $"https://ntfy.sh/{Uri.EscapeDataString(topic.Trim())}/json?poll=1&since={AppConstants.PushDedupeLookback}");
            return key.IsAlreadySent(resp);
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[NotificationService] Dedup check failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
            return false;
        }
    }
}

public record NotificationTestResult(
    bool WindowsToastAttempted,
    bool NtfyAttempted,
    bool NtfySucceeded,
    string? ErrorMessage,
    AppPushResult? AppPush = null);

/// <summary>
/// 사용량 알림 문구(#262). Windows 풍선·ntfy 토픽·휴대폰 앱 푸시가 같은 문구를 쓴다.
/// 제목만 봐도 무엇이 몇 % 인지 알 수 있게 서비스·창·퍼센트를 제목에 두고, 본문에는 초기화 시각을 적는다.
/// 알림은 나중에 열어 보는 일이 많아 "3h 12m 후" 같은 상대 시간만으로는 언제인지 알 수 없으므로 절대 시각을 함께 적는다.
/// </summary>
internal static class AlertText
{
    public static string UsageTitle(string agent, string window, int percent) => Loc.UsageAlertTitle(agent, window, percent);

    /// <summary>
    /// Input : 임계값(%), 창의 서버 리셋 시각(모르면 null), 리셋 시각을 모를 때 쓸 팝업 라벨(" · 약 3h 후 초기화(예상)" 꼴), 지금
    /// Output: 본문
    /// 핵심 로직: 리셋 시각이 앞으로 있으면 "15:30 초기화 · 3h 12m 남음". 없으면 팝업 라벨에서 앞의 구분점을 떼어 쓰고,
    ///           그것도 없으면(추가 사용량처럼 창이 없는 한도) 어떤 기준을 넘었는지만 적는다.
    /// </summary>
    public static string UsageBody(int percent, DateTimeOffset? resetAt, string? fallbackResetLabel, DateTimeOffset now)
    {
        if (ResetText(resetAt, now) is { } reset) return reset;
        var label = fallbackResetLabel?.Trim().TrimStart('·').Trim();
        return string.IsNullOrEmpty(label) ? Loc.AlertThresholdPassed(percent) : label;
    }

    public static string EarlyExhaustionBody(DateTimeOffset depletionAt, DateTimeOffset resetAt, DateTimeOffset now) =>
        Loc.EarlyExhaustionAlertBody(UsageCalculator.FormatClockStamp(depletionAt, now), UsageCalculator.FormatClockStamp(resetAt, now));

    /// <summary>"15:30 초기화 · 3h 12m 남음". 리셋 시각을 모르거나 이미 지났으면 null.</summary>
    public static string? ResetText(DateTimeOffset? resetAt, DateTimeOffset now)
    {
        if (resetAt is not { } at || at <= now) return null;
        return Loc.AlertResetsAt(UsageCalculator.FormatClockStamp(at, now), UsageCalculator.FormatRemaining(at - now));
    }
}
