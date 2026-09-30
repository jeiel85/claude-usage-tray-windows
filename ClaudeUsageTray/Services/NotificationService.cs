using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

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

    // windowId/windowResetAt/accountId: 여러 PC 가 같은 토픽을 쓸 때 "같은 사건" 을 가르는 값이다(#175).
    // windowLabel 은 표시 언어에 따라 PC 마다 달라지므로 키에 쓰지 않고, 창의 리셋 시각은 서버 값이라 PC 간에 같다.
    // accountId 는 모니터링 중인 계정의 식별자 원문(해시는 키 쪽에서 한다) — 계정이 다른 PC 의 알림이 서로를 삼키지 않게 한다.
    public void ShowUsageAlert(int thresholdPercent, string windowLabel, string resetLabel, string ntfyTopic,
        string agent = "Claude", int priority = 3, string windowId = "short", DateTimeOffset? windowResetAt = null,
        string? accountId = null)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.NotificationBody(thresholdPercent, windowLabel, resetLabel, agent);

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body, priority,
            key: NtfyEventKey.ForUsage(agent, windowId, thresholdPercent, windowResetAt, DateTimeOffset.Now, accountId));
    }

    // 테스트 알림은 키를 붙이지 않는다 — 눌러 볼 때마다 실제로 도착해야 "테스트" 가 된다.
    public void ShowTestAlert(string ntfyTopic, string agent = "Claude", int priority = 3)
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.TestNotificationBody;
        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body, priority);
    }

    public async Task<NotificationTestResult> ShowTestAlertAsync(string ntfyTopic, string agent = "Claude")
    {
        var title = Loc.NotificationTitle;
        var body  = Loc.TestNotificationBody;
        ShowBalloon(title, body);

        if (string.IsNullOrWhiteSpace(ntfyTopic))
            return new NotificationTestResult(true, false, true, null);

        var ntfyOk = await SendNtfyAsync(ntfyTopic, title, body);
        return new NotificationTestResult(true, true, ntfyOk, ntfyOk ? null : Loc.NtfyTestSendFailed);
    }

    public void ShowRateLimitAlert(string ntfyTopic, int priority = 2, string? accountId = null)
    {
        var title = Loc.RateLimitTitle;
        var body  = Loc.RateLimited;

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body, priority,
            key: NtfyEventKey.ForInstant("ratelimit", "Claude", DateTimeOffset.Now, accountId));
    }

    public void ShowQuotaResetAlert(string ntfyTopic, string agent = "Claude", int priority = 2, string? accountId = null)
    {
        var title = Loc.QuotaResetTitle(agent);
        var body  = Loc.QuotaResetBody(agent);

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body, priority,
            key: NtfyEventKey.ForInstant("reset", agent, DateTimeOffset.Now, accountId));
    }

    // depletionAt: 예상 소진 시각(문자열 depletionTime 은 표시용이라 키에 쓸 수 없다). PC 마다 몇 분씩 어긋나므로 허용 오차로 흡수한다.
    public void ShowEarlyExhaustionAlert(string depletionTime, string resetTime, string ntfyTopic, int priority = 2,
        DateTimeOffset? depletionAt = null, string? accountId = null)
    {
        var title = Loc.EarlyExhaustionTitle;
        var body  = Loc.EarlyExhaustionBody(depletionTime, resetTime);

        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, body, priority, [],
            NtfyEventKey.ForEarlyExhaustion("Claude", depletionAt, DateTimeOffset.Now, accountId));
    }

    // dedupeKey: 날씨 알림이 PC 안에서 이미 쓰는 중복 방지 키(위치·날짜 포함). 없으면(테스트 알림) 중복 확인을 건너뛴다.
    public void ShowWeatherAlert(string title, string body, string ntfyTopic,
        string? ntfyMessage = null, int priority = 4, string[]? tags = null,
        string? clickUrl = null, string? dedupeKey = null)
    {
        ShowBalloon(title, body);
        SendNtfy(ntfyTopic, title, ntfyMessage ?? body, priority, tags ?? ["sunny"], clickUrl: clickUrl,
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

    private static void SendNtfy(string topic, string title, string body, int priority = 3, string[]? tags = null,
        NtfyEventKey? key = null, string? clickUrl = null)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;

        // Fire-and-forget — don't block the UI
        _ = Task.Run(async () =>
        {
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
    string? ErrorMessage);
