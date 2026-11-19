using System.Collections.Concurrent;
using System.IO;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Mobile;
using ClaudeUsageTray.Services.WeatherWarnings;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 날씨 알림이 실제 경로(<see cref="WeatherAlertService.ProcessAlertsAsync"/>)에서 어떤 우선순위로 나가는지 고정한다(#299).
/// <see cref="AlertPriorityTests"/> 는 우선순위 값 → 휴대폰 중요도 매핑만 보므로, 호출부가 값을 바꾸면 거기서는 드러나지 않는다.
/// 휴대폰은 4(<see cref="PushPriority.Warning"/>) 이상만 소리·진동으로 알린다 — 기관 특보만 울리고 예보·조건 알림은 조용히.
/// </summary>
// 문구를 Loc 로 비교하므로 언어를 바꾸는 테스트와 나란히 돌지 않게 한다.
[Collection("WpfTests")]
public class WeatherAlertPriorityPathTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cut-weather-priority-" + Guid.NewGuid().ToString("N"));

    public WeatherAlertPriorityPathTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private sealed class RecordingPush : IAppPushSender
    {
        public readonly BlockingCollection<(string Category, string? Body, string Priority)> Sent = new();
        public bool HasTargets => true;

        public Task<AppPushResult> SendAsync(string category, string title, string? body, int ntfyPriority, string? eventKey,
            TimeSpan eventTolerance, string? clickUrl, PushAccount? account = null, CancellationToken ct = default)
        {
            Sent.Add((category, body, PushPriority.FromNtfy(ntfyPriority)), ct);
            return Task.FromResult(AppPushResult.Delivered);
        }
    }

    private sealed class OneWarning(WeatherAlertItem alert) : IWeatherWarningProvider
    {
        public bool Supports(WeatherLocation location) => true;

        public Task<IReadOnlyList<WeatherAlertItem>> GetActiveAlertsAsync(WeatherLocation location, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WeatherAlertItem>>([alert]);
    }

    [Fact]
    public async Task OnlyOfficialWarningsRing_ForecastAndConditionAlertsStayQuiet()
    {
        var now = DateTimeOffset.Now;
        var loc = new WeatherLocation("Testville", "KR", null, 37.5, 127.0, "Asia/Seoul");
        // 비(확률 90%, 코드 63)·폭염(40°C)·한파(-20°C)·강풍(80km/h)이 모두 걸리는 날.
        var report = new WeatherReport(
            new CurrentWeatherSnapshot(loc, now, 20, 19, 63, "rain", 5, 80),
            [new DailyWeatherForecast(DateOnly.FromDateTime(now.Date), 63, -20, 40, 90)],
            []);
        var warning = new WeatherAlertItem("test", "w-1", "Heavy rain warning", "Severe", "Stay indoors", now, now.AddHours(6));
        var settings = new NotificationSettings
        {
            WeatherEnabled = true,
            WeatherDailyForecastEnabled = true,
            WeatherDailyForecastTime = now.ToString("HH:mm"),
            WeatherConditionAlertsEnabled = true,
            WeatherOfficialAlertsEnabled = true,
        };
        var push = new RecordingPush();
        var notifier = new NotificationService(() => null) { AppPush = push };
        var service = new WeatherAlertService(new WeatherService(), notifier, () => settings, [new OneWarning(warning)],
            Path.Combine(_dir, "weather-alerts.json"));

        await service.ProcessAlertsAsync(report);

        var sent = new List<(string Category, string? Body, string Priority)>();
        while (sent.Count < 6 && push.Sent.TryTake(out var item, TimeSpan.FromSeconds(5))) sent.Add(item);
        Assert.Equal(6, sent.Count);
        Assert.All(sent, s => Assert.Equal(PushCategories.Weather, s.Category));

        string PriorityOf(string body) => Assert.Single(sent, s => s.Body == body).Priority;
        string PriorityStartingWith(string prefix) => Assert.Single(sent, s => s.Body!.StartsWith(prefix, StringComparison.Ordinal)).Priority;

        Assert.Equal(PushPriority.Info, PriorityStartingWith($"Testville: {Loc.WeatherRain}\n"));        // 매일 예보
        Assert.Equal(PushPriority.Info, PriorityOf($"Testville: {Loc.WeatherRainWarning(90)}"));
        Assert.Equal(PushPriority.Info, PriorityOf($"Testville: {Loc.WeatherHeatWarning(40)}"));
        Assert.Equal(PushPriority.Info, PriorityOf($"Testville: {Loc.WeatherColdWarning(-20)}"));
        Assert.Equal(PushPriority.Info, PriorityOf($"Testville: {Loc.WeatherWindWarning(80)}"));
        Assert.Equal(PushPriority.Warning, PriorityOf("Heavy rain warning · Severe\nStay indoors"));
    }
}
