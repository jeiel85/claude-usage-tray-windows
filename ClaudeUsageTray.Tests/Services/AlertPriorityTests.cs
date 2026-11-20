using System.Collections.Concurrent;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Mobile;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

// 휴대폰 앱은 warning·critical 알림만 소리·진동으로 울린다(#297). PC 가 알림마다 어떤 우선순위를 싣는지가
// 곧 "울리는 알림" 목록이므로, 실제 NotificationService 경로로 보내 휴대폰에 도착하는 우선순위를 고정한다.
public class AlertPriorityTests
{
    private sealed class RecordingPush : IAppPushSender
    {
        public readonly BlockingCollection<(string Category, string Priority)> Sent = new();
        public bool HasTargets => true;

        public Task<AppPushResult> SendAsync(string category, string title, string? body, int ntfyPriority, string? eventKey,
            TimeSpan eventTolerance, string? clickUrl, PushAccount? account = null, CancellationToken ct = default)
        {
            Sent.Add((category, PushPriority.FromNtfy(ntfyPriority)), ct);
            return Task.FromResult(AppPushResult.Delivered);
        }
    }

    private static (string Category, string Priority) SendOne(Action<NotificationService> send)
    {
        var push = new RecordingPush();
        var notifier = new NotificationService(() => null) { AppPush = push };
        send(notifier);
        Assert.True(push.Sent.TryTake(out var sent, TimeSpan.FromSeconds(5)), "push was not sent");
        return sent;
    }

    [Theory]
    [InlineData(50, PushPriority.Info)]
    [InlineData(75, PushPriority.Info)]
    [InlineData(90, PushPriority.Warning)]
    [InlineData(100, PushPriority.Critical)]
    public void Usage_rings_from_90_percent(int threshold, string expected)
    {
        var sent = SendOne(n => n.ShowUsageAlert(threshold, "5h", "", "", "Claude", UsageCalculator.ThresholdToPriority(threshold)));
        Assert.Equal((PushCategories.Usage, expected), sent);
    }

    [Fact]
    public void Rate_limit_rings_because_work_has_stopped() =>
        Assert.Equal((PushCategories.RateLimit, PushPriority.Warning), SendOne(n => n.ShowRateLimitAlert("")));

    [Fact]
    public void Reset_is_quiet() =>
        Assert.Equal((PushCategories.Reset, PushPriority.Info), SendOne(n => n.ShowQuotaResetAlert("")));

    [Fact]
    public void Early_exhaustion_is_quiet()
    {
        var now = DateTimeOffset.Now;
        Assert.Equal((PushCategories.Exhaustion, PushPriority.Info),
            SendOne(n => n.ShowEarlyExhaustionAlert(now.AddHours(1), now.AddHours(3), "")));
    }

    [Theory]
    [InlineData(WeatherPriority.Forecast, PushPriority.Info)]
    [InlineData(WeatherPriority.Condition, PushPriority.Info)]
    [InlineData(WeatherPriority.Official, PushPriority.Warning)]
    public void Only_official_weather_warnings_ring(int priority, string expected) =>
        Assert.Equal((PushCategories.Weather, expected), SendOne(n => n.ShowWeatherAlert("t", "b", "", priority: priority)));
}
