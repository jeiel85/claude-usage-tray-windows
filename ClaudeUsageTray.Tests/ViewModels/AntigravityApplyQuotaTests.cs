using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

/// <summary>
/// 화면에 올라가는 규칙(무엇을 보여주고, 트레이 게이지에 어떤 값을 쓰는지)만 검증한다.
/// ApplyQuota 는 로컬 조회와 다른 PC 동기화가 함께 지나가는 지점이라 표시가 어긋나면 양쪽이 같이 틀어진다.
///
/// 언어를 바꿔 확인하는 항목이 있어 Loc 을 만지는 다른 테스트와 같은 컬렉션에 둔다.
/// Loc.Lang 은 프로세스 전역이라 병렬로 돌면 서로의 단정을 깨뜨린다.
/// </summary>
[Collection("WpfTests")]
public sealed class AntigravityApplyQuotaTests
{
    private static readonly DateTimeOffset Reset = DateTimeOffset.UtcNow.AddHours(3);

    private static AntigravityModelQuota Bucket(string id, double remaining, string displayName = "",
                                                DateTimeOffset? reset = null, string window = "") =>
        new()
        {
            ModelId = id,
            RemainingFraction = remaining,
            DisplayName = displayName,
            ResetTime = reset ?? Reset,
            TokenType = window,
        };

    private static AntigravityViewModel CreateVm(out AntigravityUsageMonitor monitor)
    {
        monitor = new AntigravityUsageMonitor();
        return new AntigravityViewModel(monitor);
    }

    [Fact]
    public void ApplyQuota_KeepsUntouchedWindows_SoTheGaugeMatchesTheAntigravityScreen()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 아직 아무것도 쓰지 않은 주 — Antigravity 화면은 이때도 100% 게이지 네 칸을 보여준다.
            vm.ApplyQuota(
            [
                Bucket("gemini-weekly", 1.0, "Gemini Models · Weekly Limit"),
                Bucket("gemini-5h",     1.0, "Gemini Models · Five Hour Limit"),
                Bucket("3p-weekly",     1.0, "Claude and GPT models · Weekly Limit"),
                Bucket("3p-5h",         1.0, "Claude and GPT models · Five Hour Limit"),
            ], "Antigravity", "Google AI Pro");

            Assert.True(vm.HasData);
            Assert.Equal(4, vm.Models.Count);
            Assert.All(vm.Models, row => Assert.Equal(0, row.UsagePercent));
            Assert.Equal(0, vm.Percent);
            Assert.Equal("Google AI Pro", vm.PaidTierName);
        }
    }

    [Fact]
    public void ApplyQuota_ReportsWorstWindow_NotTheAverage()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 주간은 90% 썼고 5시간 창은 아직 안 썼다. 평균(45%)은 실제 제약을 가린다.
            vm.ApplyQuota(
            [
                Bucket("gemini-weekly", 0.10),
                Bucket("gemini-5h",     1.00),
            ], null, null);

            Assert.Equal(0.90, vm.Percent, 3);
        }
    }

    [Fact]
    public void ApplyQuota_OrdersShortWindowFirst_LikeClaudeAndCodex()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 사용량 순으로 세우면 새로고침마다 자리가 바뀐다. 다른 provider 처럼 창 길이 순으로 고정한다.
            vm.ApplyQuota(
            [
                Bucket("gemini-weekly", 0.80, window: "weekly"),   // 20% 사용
                Bucket("3p-5h",         0.10, window: "5h"),       // 90% 사용
                Bucket("gemini-5h",     0.50, window: "5h"),       // 50% 사용
                Bucket("3p-weekly",     0.30, window: "weekly"),   // 70% 사용
            ], null, null);

            // 그룹은 서버가 준 순서(gemini → 3p)를 지키고, 그 안에서 5시간 → 주간.
            Assert.Equal(
                ["gemini-5h", "gemini-weekly", "3p-5h", "3p-weekly"],
                vm.Models.Select(m => m.ModelId));
        }
    }

    [Fact]
    public void ApplyQuota_PutsUnknownWindowLast_WithoutDroppingIt()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 새 창 종류는 길이를 몰라 시간선을 못 그린다 — 자리를 지어내지 않고 그룹 맨 뒤로 보낸다.
            vm.ApplyQuota(
            [
                Bucket("gemini-monthly", 0.50, window: "monthly"),
                Bucket("gemini-weekly",  0.50, window: "weekly"),
                Bucket("gemini-5h",      0.50, window: "5h"),
            ], null, null);

            Assert.Equal(
                ["gemini-5h", "gemini-weekly", "gemini-monthly"],
                vm.Models.Select(m => m.ModelId));
        }
    }

    [Fact]
    public void ApplyQuota_WritesSummaryLine_LikeClaudeAndCodexGauges()
    {
        var previous = Loc.CurrentLang;
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            try
            {
                Loc.SetLanguage("ko");
                vm.ApplyQuota([Bucket("gemini-5h", 0.25, window: "5h")], null, null);

                // 게이지 아래 한 줄은 다른 provider 와 같은 문구 형식이어야 한다.
                Assert.Equal(Loc.UsageSummary(0.75), vm.Models[0].Summary);
            }
            finally
            {
                Loc.SetLanguage(previous);
            }
        }
    }

    [Theory]
    [InlineData("ko", "gemini-weekly", "weekly", "Gemini 모델 · 주간")]
    [InlineData("ko", "3p-5h", "5h", "Claude · GPT 모델 · 5시간")]
    [InlineData("ja", "gemini-5h", "5h", "Gemini モデル · 5時間")]
    [InlineData("zh", "3p-weekly", "weekly", "Claude 和 GPT 模型 · 每周")]
    [InlineData("en", "gemini-weekly", "weekly", "Gemini Models · Weekly")]
    public void ResolveDisplayName_UsesAppLanguage_ForKnownBuckets(
        string lang, string bucketId, string window, string expected)
    {
        var previous = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage(lang);

            var label = AntigravityViewModel.ResolveDisplayName(new AntigravityModelQuota
            {
                ModelId = bucketId,
                TokenType = window,
                DisplayName = "Gemini Models · Weekly Limit Remaining",   // 서버가 준 영어 문구
            });

            Assert.Equal(expected, label);
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    [Fact]
    public void ResolveDisplayName_KeepsServerText_ForUnknownBucket()
    {
        var previous = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage("ko");

            // 새 그룹이나 새 창 종류가 생기면 번역이 없으므로 서버 문구를 그대로 보여준다.
            var label = AntigravityViewModel.ResolveDisplayName(new AntigravityModelQuota
            {
                ModelId = "future-group-monthly",
                TokenType = "monthly",
                DisplayName = "Future Group · Monthly Limit",
            });

            Assert.Equal("Future Group · Monthly Limit", label);
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    [Fact]
    public void RefreshLocalizedLabels_RebuildsRows_WhenLanguageChangesAfterLoad()
    {
        var previous = Loc.CurrentLang;
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            try
            {
                Loc.SetLanguage("en");
                vm.ApplyQuota(
                [
                    new AntigravityModelQuota
                    {
                        ModelId = "gemini-weekly", TokenType = "weekly",
                        RemainingFraction = 0.4, ResetTime = Reset,
                    },
                ], null, null);
                Assert.Equal("Gemini Models · Weekly", vm.Models[0].DisplayName);

                // 행 문구는 만들 때 정해지므로, 언어만 바꾸면 다음 조회까지 옛 언어로 남는다.
                Loc.SetLanguage("ko");
                vm.RefreshLocalizedLabels();

                Assert.Equal("Gemini 모델 · 주간", vm.Models[0].DisplayName);
                Assert.Contains("초기화", vm.Models[0].ResetAtLabel);
            }
            finally
            {
                Loc.SetLanguage(previous);
            }
        }
    }

    [Fact]
    public void RefreshLocalizedLabels_DoesNothing_WhenNoQuotaLoaded()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.RefreshLocalizedLabels();

            Assert.False(vm.HasData);
            Assert.Empty(vm.Models);
        }
    }

    [Fact]
    public void ApplyQuota_FallsBackToFormattedId_WhenDisplayNameMissing()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 표시 이름을 함께 올리지 않던 버전이 동기화한 값.
            vm.ApplyQuota([Bucket("gemini-2.5-pro", 0.5)], null, null);

            Assert.Equal(AntigravityViewModel.FormatModelName("gemini-2.5-pro"), vm.Models[0].DisplayName);
        }
    }

    [Fact]
    public void ApplyQuota_PrefersServerDisplayName_WhenPresent()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ApplyQuota([Bucket("3p-weekly", 0.5, "Claude and GPT models · Weekly Limit")], null, null);

            Assert.Equal("Claude and GPT models · Weekly Limit", vm.Models[0].DisplayName);
        }
    }

    [Fact]
    public void ApplyQuota_SkipsRows_WithoutResetTimeOrInternalPrefix()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ApplyQuota(
            [
                Bucket("gemini-weekly", 0.5),
                new AntigravityModelQuota { ModelId = "no-reset", RemainingFraction = 0.5, ResetTime = null },
                Bucket("chat_internal", 0.1),
                Bucket("tab_internal",  0.1),
            ], null, null);

            Assert.Equal(["gemini-weekly"], vm.Models.Select(m => m.ModelId));
            // 걸러진 행은 트레이 게이지 값에도 반영되지 않는다.
            Assert.Equal(0.5, vm.Percent, 3);
        }
    }

    [Fact]
    public void ApplyQuota_ClearsPreviousError()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.HasError = true;
            vm.ErrorMessage = "stale failure";

            vm.ApplyQuota([Bucket("gemini-weekly", 1.0)], null, null);

            Assert.False(vm.HasError);
            Assert.Equal("", vm.ErrorMessage);
        }
    }

    [Fact]
    public void ApplyQuota_PlacesTimelineMarker_ByWindowLength()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            var now = DateTimeOffset.Now;
            vm.ApplyQuota(
            [
                new AntigravityModelQuota
                {
                    ModelId = "gemini-weekly", TokenType = "weekly",
                    RemainingFraction = 0.5, ResetTime = now.AddDays(3.5),
                },
                new AntigravityModelQuota
                {
                    ModelId = "3p-5h", TokenType = "5h",
                    RemainingFraction = 0.5, ResetTime = now.AddHours(2.5),
                },
            ], null, null);

            vm.UpdateTimeProgress(now);

            // 창 길이가 다르므로 같은 "절반 남음"이라도 마커는 각자의 창 기준으로 찍혀야 한다.
            var weekly = vm.Models.Single(m => m.ModelId == "gemini-weekly");
            var fiveHour = vm.Models.Single(m => m.ModelId == "3p-5h");
            Assert.True(weekly.HasTimeline);
            Assert.Equal(0.5, weekly.TimePercent, 6);
            Assert.True(fiveHour.HasTimeline);
            Assert.Equal(0.5, fiveHour.TimePercent, 6);
        }
    }

    [Fact]
    public void ApplyQuota_HidesTimeline_WhenWindowLengthIsUnknown()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 새 창 종류가 생기면 길이를 모른다 — 행과 리셋 안내는 남기고 마커만 그리지 않는다.
            vm.ApplyQuota(
            [
                new AntigravityModelQuota
                {
                    ModelId = "future-group-monthly", TokenType = "monthly",
                    RemainingFraction = 0.5, ResetTime = DateTimeOffset.Now.AddDays(10),
                },
            ], null, null);

            var row = Assert.Single(vm.Models);
            Assert.False(row.HasTimeline);
            Assert.Equal(0, row.TimePercent);
            Assert.NotEqual("", row.ResetAtLabel);
        }
    }

    [Fact]
    public void UpdateTimeProgress_AdvancesMarkerOnTheSameRows()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            var now = DateTimeOffset.Now;
            vm.ApplyQuota(
            [
                new AntigravityModelQuota
                {
                    ModelId = "3p-5h", TokenType = "5h",
                    RemainingFraction = 0.5, ResetTime = now.AddHours(5),
                },
            ], null, null);
            var row = vm.Models[0];

            vm.UpdateTimeProgress(now.AddHours(2.5));

            // 매초 갱신이므로 행을 새로 만들면 안 된다 (스크롤·바인딩이 튄다).
            Assert.Same(row, vm.Models[0]);
            Assert.Equal(0.5, row.TimePercent, 6);
        }
    }

    [Fact]
    public void ApplyQuota_KeepsAbsoluteResetTime_InTheTooltipOnly()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ApplyQuota([Bucket("3p-5h", 0.5, window: "5h")], null, null);

            // 행 이름이 "그룹 · 창"이라 한 줄 라벨에 절대 시각까지 넣으면 이름이 잘린다 — 툴팁으로 뺀다.
            Assert.DoesNotContain("(", vm.Models[0].ResetAtLabel);
            Assert.Contains("(", vm.Models[0].PaceTip);
        }
    }

    [Fact]
    public void ApplyQuota_WritesPaceTip_ForTheGaugeTooltip()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            var now = DateTimeOffset.Now;
            vm.ApplyQuota(
            [
                new AntigravityModelQuota
                {
                    ModelId = "3p-5h", TokenType = "5h",
                    RemainingFraction = 0.4, ResetTime = now.AddHours(2.5),
                },
            ], null, null);

            vm.UpdateTimeProgress(now);

            // 툴팁 첫 줄은 Claude·Codex 와 같은 페이스 문구다 (시간 50% 경과 · 사용 60%).
            var lines = vm.Models[0].PaceTip.Split('\n');
            Assert.Equal(Loc.PaceTip(0.5, 0.6, settled: true), lines[0]);
            // 둘째 줄에 절대 시각이 남는다.
            Assert.Contains("(", lines[1]);
        }
    }

    [Fact]
    public void ApplyQuota_LeavesListEmpty_WhenNothingUsable()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ApplyQuota(new List<AntigravityModelQuota>(), null, null);

            Assert.Empty(vm.Models);
            Assert.Equal(0, vm.Percent);
        }
    }
}
