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
/// </summary>
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
