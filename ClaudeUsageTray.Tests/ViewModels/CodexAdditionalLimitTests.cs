using System;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

/// <summary>
/// Codex 추가 한도(이미지 생성 등, #182)가 화면에 올라가는 규칙 — 이름, 창, 리셋 지난 창 제거,
/// 그리고 이 PC 값과 다른 PC 값 중 무엇을 쓰는지.
///
/// 언어를 바꿔 확인하는 항목이 있어 Loc 을 만지는 다른 테스트와 같은 컬렉션에 둔다(Loc.Lang 은 프로세스 전역).
/// </summary>
[Collection("WpfTests")]
public sealed class CodexAdditionalLimitTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("ko", "이미지 생성")]
    [InlineData("en", "Image generation")]
    [InlineData("zh", "图像生成")]
    [InlineData("ja", "画像生成")]
    public void ResolveLimitName_ImageLimit_UsesAppLanguage(string lang, string expected)
    {
        var previous = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage(lang);

            Assert.Equal(expected, CodexViewModel.ResolveLimitName("codex_image_gen", "image_gen"));
            // 로그 줄은 이름이 비어 있을 수 있다 — ID 로도 알아봐야 한다.
            Assert.Equal(expected, CodexViewModel.ResolveLimitName("codex_image_gen", ""));
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    // 모르는 한도는 지어내지 않고 서버가 준 이름을 다듬어 쓴다. 이름이 없으면 ID 로.
    [Theory]
    [InlineData("codex_bengalfox", "gpt-5.3-codex-spark", "GPT-5.3-Codex-Spark")]
    [InlineData("codex_other", "", "Codex Other")]
    public void ResolveLimitName_UnknownLimit_TidiesTheServerName(string limitId, string limitName, string expected)
        => Assert.Equal(expected, CodexViewModel.ResolveLimitName(limitId, limitName));

    // 창 이름은 Antigravity 행과 같은 짧은 형식. 시간으로 나누어떨어지지 않는 창은 내려 부르지 않고 분으로.
    [Theory]
    [InlineData(300, "5시간")]
    [InlineData(10080, "주간")]
    [InlineData(20160, "2주")]
    [InlineData(2880, "2일")]
    [InlineData(90, "90분")]
    public void QuotaWindowShortName_NamesTheWindowLength(int minutes, string expected)
    {
        var previous = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage("ko");
            Assert.Equal(expected, Loc.QuotaWindowShortName(minutes));
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    [Fact]
    public void BuildAdditionalLimitRows_NamesTheWindow_AndDropsWindowsThatAlreadyReset()
    {
        var previous = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage("ko");

            var rows = CodexViewModel.BuildAdditionalLimitRows(
            [
                Window("codex_image_gen", "image_gen", 0.4, Now.AddDays(2), 10080),
                Window("codex_image_gen", "image_gen", 0.9, Now.AddMinutes(-1), 300),
                Window("codex_other", "", 0.1, Now.AddHours(1), null),
            ], Now, showAbsoluteResetTime: false);

            Assert.Equal(2, rows.Count);

            Assert.Equal("이미지 생성 · 주간", rows[0].DisplayName);
            Assert.Equal(0.4, rows[0].UsagePercent, 3);
            Assert.Equal(Loc.UsageSummary(0.4), rows[0].Summary);
            // 주간 창이 이틀 남았으면 5/7 경과 — 시간선 마커가 제자리에 선다.
            Assert.True(rows[0].HasTimeline);
            Assert.Equal(5.0 / 7.0, rows[0].TimePercent, 3);

            // 창 길이를 모르면 이름만 쓰고 마커도 그리지 않는다(위치를 지어내지 않는다).
            Assert.Equal("Codex Other", rows[1].DisplayName);
            Assert.False(rows[1].HasTimeline);
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    // Direct API 는 추가 한도를 빠짐없이 준다 — 비어 있으면 "지금 걸린 추가 한도 없음"이 정답이다.
    // 다른 PC 의 옛 값으로 채우면 이미 없어진 한도가 되살아난다.
    [Fact]
    public void SelectCodexAdditionalQuotas_DirectApiResultWins_EvenWhenEmpty()
    {
        var local = new ProviderUsageSnapshot { DataSource = CodexUsageMonitor.DirectApiSource };

        Assert.Empty(MainViewModel.SelectCodexAdditionalQuotas(
            local, [Synced("laptop", Window("codex_image_gen", "image_gen", 0.6, Now.AddDays(2), 10080, Now.AddMinutes(-1)))], Now));
    }

    // 리뷰 지적 재현: 토큰이 만료돼 로그로만 읽는 PC 도 로그에 살아 있는 주간 창이 있으면 자기 스냅샷을 방금 쓰고,
    // 그 스냅샷이 폴더에서 가장 새것이다. 그래도 다른 PC 가 본 추가 한도를 써야 한다(계정 단위 값).
    [Fact]
    public void SelectCodexAdditionalQuotas_LogFallback_UsesAnotherPcsValues_EvenWhenOwnSnapshotIsNewest()
    {
        var local = new ProviderUsageSnapshot { DataSource = "Local Log", LongResetAt = Now.AddDays(3) };
        var ownSnapshot = Synced("desktop", observedAt: Now);
        var laptopSnapshot = Synced("laptop",
            observedAt: Now.AddMinutes(-2),
            Window("codex_image_gen", "image_gen", 0.25, Now.AddHours(2), 300, Now.AddMinutes(-2)),
            Window("codex_image_gen", "image_gen", 0.6, Now.AddDays(2), 10080, Now.AddMinutes(-2)));

        var picked = MainViewModel.SelectCodexAdditionalQuotas(local, [ownSnapshot, laptopSnapshot], Now);

        Assert.Collection(picked,
            fiveHour => Assert.Equal(0.25, fiveHour.UsagePercent, 3),
            weekly => Assert.Equal(0.6, weekly.UsagePercent, 3));
    }

    // 며칠 전 로그 값이 다른 PC 가 방금 본 값을 가리면 안 된다 — 한도마다 더 최근 관측을 쓴다.
    [Fact]
    public void SelectCodexAdditionalQuotas_NewestObservationWins()
    {
        var staleLocal = new ProviderUsageSnapshot
        {
            DataSource = "Local Log",
            AdditionalQuotas = [Window("codex_image_gen", "", 0.4, Now.AddDays(4), 10080, Now.AddDays(-3))],
        };
        var fresherLaptop = Synced("laptop", observedAt: Now.AddMinutes(-1),
            Window("codex_image_gen", "image_gen", 0.9, Now.AddDays(4), 10080, Now.AddMinutes(-1)));

        Assert.Equal(0.9, Assert.Single(MainViewModel.SelectCodexAdditionalQuotas(staleLocal, [fresherLaptop], Now)).UsagePercent, 3);

        var freshLocal = new ProviderUsageSnapshot
        {
            DataSource = "Local Log",
            AdditionalQuotas = [Window("codex_image_gen", "", 0.95, Now.AddDays(4), 10080, Now)],
        };

        Assert.Equal(0.95, Assert.Single(MainViewModel.SelectCodexAdditionalQuotas(freshLocal, [fresherLaptop], Now)).UsagePercent, 3);
    }

    // 이 PC 가 방금 쓴 스냅샷에는 로컬 값이 그대로 실려 있다 — 같은 창이 두 번 그려지면 안 된다.
    // 다른 PC 값이라도 리셋이 지난 창은 지금 창을 설명하지 않으므로 뺀다.
    [Fact]
    public void SelectCodexAdditionalQuotas_DropsEchoesOfLocalValues_AndExpiredWindows()
    {
        var localWindow = Window("codex_image_gen", "", 0.3, Now.AddDays(2), 10080, Now.AddMinutes(-5));
        var local = new ProviderUsageSnapshot { DataSource = "Local Log", AdditionalQuotas = [localWindow] };
        var ownEcho = Synced("desktop", observedAt: Now, localWindow);
        var expired = Synced("laptop", observedAt: Now,
            Window("codex_bengalfox", "gpt-5.3-codex-spark", 0.5, Now.AddMinutes(-1), 300, Now));

        var window = Assert.Single(MainViewModel.SelectCodexAdditionalQuotas(local, [ownEcho, expired], Now));

        Assert.Equal("codex_image_gen", window.LimitId);
    }

    // 다른 PC 가 받아 쓰려면 이 PC 가 올리는 스냅샷에 관측 시각까지 실려 있어야 한다.
    [Fact]
    public void CreateProviderQuotaSnapshot_CarriesCodexAdditionalQuotas()
    {
        var reset = DateTimeOffset.UtcNow.AddHours(3);
        var observedAt = DateTimeOffset.UtcNow;
        var quota = MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.Codex, new ProviderUsageSnapshot
        {
            HasData = true,
            ShortUsagePercent = 0.1,
            ShortResetAt = reset,
            ShortWindowMinutes = 300,
            AdditionalQuotas = [Window("codex_image_gen", "image_gen", 0.4, reset, 10080, observedAt)],
        });

        Assert.NotNull(quota);
        var window = Assert.Single(quota!.AdditionalQuotas);
        Assert.Equal("codex_image_gen", window.LimitId);
        Assert.Equal("image_gen", window.LimitName);
        Assert.Equal(0.4, window.UsagePercent, 3);
        Assert.Equal(reset, window.ResetAt);
        Assert.Equal(10080, window.WindowMinutes);
        Assert.Equal(observedAt, window.ObservedAt);
    }

    private static AdditionalQuotaWindow Window(string limitId, string limitName, double percent,
        DateTimeOffset resetAt, int? windowMinutes, DateTimeOffset? observedAt = null) => new()
    {
        LimitId = limitId,
        LimitName = limitName,
        UsagePercent = percent,
        ResetAt = resetAt,
        WindowMinutes = windowMinutes,
        ObservedAt = observedAt,
    };

    private static UsageSyncSnapshot Synced(string deviceId, DateTimeOffset observedAt, params AdditionalQuotaWindow[] windows) => new()
    {
        DeviceId = deviceId,
        Provider = UsageProviderKind.Codex,
        ObservedAtUtc = observedAt,
        Quota = new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ObservedAtUtc = observedAt,
            LongResetAt = Now.AddDays(3),
            AdditionalQuotas = [.. Array.ConvertAll(windows, window => new UsageSyncAdditionalQuota
            {
                LimitId = window.LimitId,
                LimitName = window.LimitName,
                UsagePercent = window.UsagePercent,
                ResetAt = window.ResetAt,
                WindowMinutes = window.WindowMinutes,
                ObservedAt = window.ObservedAt,
            })],
        },
    };

    private static UsageSyncSnapshot Synced(string deviceId, AdditionalQuotaWindow window) =>
        Synced(deviceId, window.ObservedAt ?? Now, window);
}
