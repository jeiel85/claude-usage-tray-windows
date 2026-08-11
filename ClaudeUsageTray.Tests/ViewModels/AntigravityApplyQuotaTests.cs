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
                                                DateTimeOffset? reset = null) =>
        new()
        {
            ModelId = id,
            RemainingFraction = remaining,
            DisplayName = displayName,
            ResetTime = reset ?? Reset,
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
    public void ApplyQuota_SortsMostConsumedFirst()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ApplyQuota(
            [
                Bucket("gemini-weekly", 0.80),   // 20% 사용
                Bucket("3p-5h",         0.10),   // 90% 사용
                Bucket("gemini-5h",     0.50),   // 50% 사용
            ], null, null);

            Assert.Equal(["3p-5h", "gemini-5h", "gemini-weekly"], vm.Models.Select(m => m.ModelId));
        }
    }

    [Theory]
    [InlineData("ko", "gemini-weekly", "weekly", "Gemini 모델 · 주간 잔여량")]
    [InlineData("ko", "3p-5h", "5h", "Claude · GPT 모델 · 5시간 잔여량")]
    [InlineData("ja", "gemini-5h", "5h", "Gemini モデル · 5時間残量")]
    [InlineData("zh", "3p-weekly", "weekly", "Claude 和 GPT 模型 · 每周剩余")]
    [InlineData("en", "gemini-weekly", "weekly", "Gemini Models · Weekly Limit Remaining")]
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
                Assert.Equal("Gemini Models · Weekly Limit Remaining", vm.Models[0].DisplayName);

                // 행 문구는 만들 때 정해지므로, 언어만 바꾸면 다음 조회까지 옛 언어로 남는다.
                Loc.SetLanguage("ko");
                vm.RefreshLocalizedLabels();

                Assert.Equal("Gemini 모델 · 주간 잔여량", vm.Models[0].DisplayName);
                Assert.Contains("사용", vm.Models[0].UsageLabel);
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
