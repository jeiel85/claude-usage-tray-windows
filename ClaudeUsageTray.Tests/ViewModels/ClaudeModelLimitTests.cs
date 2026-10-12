using System;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

/// <summary>
/// Claude 모델 한정 한도(#187)가 화면 행이 되는 규칙 — 이름, 리셋 지난 창 제거, 동기화 스냅샷에 싣기.
/// (다른 PC 스냅샷을 화면에 반영하는 경로는 MainViewModel 이 필요해 MainViewModelIntegrationTests 에 둔다 —
/// 실제 사용자 파일을 건드리지 않는 MainViewModel 생성을 한곳에서 관리하기 위함, #186.)
/// 언어를 바꿔 확인하는 항목이 있어 Loc 을 만지는 다른 테스트와 같은 컬렉션에 둔다(Loc.Lang 은 프로세스 전역).
/// </summary>
[Collection("WpfTests")]
public sealed class ClaudeModelLimitTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    // 모델 이름은 서버 표시 이름 그대로(고유명사), 창 이름만 앱 언어로.
    [Theory]
    [InlineData("ko", "Fable · 주간")]
    [InlineData("en", "Fable · Weekly")]
    [InlineData("zh", "Fable · 每周")]
    [InlineData("ja", "Fable · 週間")]
    public void BuildModelLimitRows_UsesServerModelName_AndAppLanguageWindowName(string lang, string expected)
    {
        var previous = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage(lang);

            var row = Assert.Single(ClaudeViewModel.BuildModelLimitRows(
                [Window("weekly:fable", "Fable", 0.3, Now.AddDays(2), 10080)], Now, showAbsoluteResetTime: false));

            Assert.Equal(expected, row.DisplayName);
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    [Fact]
    public void BuildModelLimitRows_DrawsTheTimeline_AndDropsWindowsThatAlreadyReset()
    {
        var rows = ClaudeViewModel.BuildModelLimitRows(
        [
            Window("weekly:fable", "Fable", 0.3, Now.AddDays(2), 10080),
            Window("seven_day_sonnet", "Sonnet", 0.9, Now.AddMinutes(-1), 10080),
            Window("weekly:opus", "Opus", 0, null, 10080),
        ], Now, showAbsoluteResetTime: false);

        Assert.Equal(["weekly:fable", "weekly:opus"], rows.Select(row => row.Key));
        Assert.Equal(Loc.UsageSummary(0.3), rows[0].Summary);
        // 주간 창이 이틀 남았으면 5/7 경과 — 시간선 마커가 제자리에 선다.
        Assert.True(rows[0].HasTimeline);
        Assert.Equal(5.0 / 7.0, rows[0].TimePercent, 3);
        // 아직 쓰지 않아 리셋 창이 없는 한도는 마커를 그리지 않는다(위치를 지어내지 않는다).
        Assert.False(rows[1].HasTimeline);
    }

    // 동기화 파일의 이름이 비어 있으면(손으로 고친 파일 등 — 읽을 때 null 은 "" 로 보정된다) " · 주간" 대신 한도 ID 로 부른다.
    [Fact]
    public void BuildModelLimitRows_EmptyName_FallsBackToTheLimitId()
    {
        var row = Assert.Single(ClaudeViewModel.BuildModelLimitRows(
            [Window("weekly:fable", "", 0.3, Now.AddDays(2), null)], Now, showAbsoluteResetTime: false));

        Assert.Equal("weekly:fable", row.DisplayName);
    }

    // 매초 갱신 중 리셋이 지나면 다음 조회를 기다리지 않고 바로 뺀다 — 남겨 두면 리셋 전 사용률이 그대로 보인다.
    [Fact]
    public void UpdateModelLimitTimeProgress_RemovesRowsWhoseWindowJustReset()
    {
        var vm = new ClaudeViewModel();
        vm.SetModelLimits(
        [
            Window("weekly:fable", "Fable", 0.3, Now.AddMinutes(1), 10080),
            Window("seven_day_sonnet", "Sonnet", 0.1, Now.AddDays(3), 10080),
        ], Now, showAbsoluteResetTime: false);
        Assert.Equal(2, vm.ModelLimits.Count);

        vm.UpdateModelLimitTimeProgress(Now.AddMinutes(2), showAbsoluteResetTime: false);

        Assert.Equal("seven_day_sonnet", Assert.Single(vm.ModelLimits).Key);
    }

    // 모델 한도도 계정 단위 값이라 다른 PC 로 건너간다 — Codex 추가 한도와 같은 필드로.
    [Fact]
    public void CreateClaudeQuotaSnapshot_CarriesModelLimits()
    {
        var usage = new UsageResponse
        {
            FiveHour = new UsageWindow { Utilization = 10, ResetsAt = Now.AddHours(2).ToString("O") },
        };

        var quota = MainViewModel.CreateClaudeQuotaSnapshot(usage,
            [Window("weekly:fable", "Fable", 0.3, Now.AddDays(2), 10080, Now.AddMinutes(-1))]);

        var synced = Assert.Single(quota!.AdditionalQuotas);
        Assert.Equal("weekly:fable", synced.LimitId);
        Assert.Equal("Fable", synced.LimitName);
        Assert.Equal(0.3, synced.UsagePercent, 6);
        Assert.Equal(Now.AddDays(2), synced.ResetAt);
        Assert.Equal(10080, synced.WindowMinutes);
        Assert.Equal(Now.AddMinutes(-1), synced.ObservedAt);
    }

    private static AdditionalQuotaWindow Window(
        string limitId, string limitName, double percent, DateTimeOffset? resetAt, int? windowMinutes, DateTimeOffset? observedAt = null) => new()
    {
        LimitId = limitId,
        LimitName = limitName,
        UsagePercent = percent,
        ResetAt = resetAt,
        WindowMinutes = windowMinutes,
        ObservedAt = observedAt,
    };
}
