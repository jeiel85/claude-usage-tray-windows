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
            // (Claude·GPT 칸까지 보이도록 설정을 켠 경우)
            vm.ShowThirdPartyQuota = true;
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
            vm.ShowThirdPartyQuota = true;
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
                vm.Models.Select(m => m.Key));
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
                vm.Models.Select(m => m.Key));
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
            vm.ApplyQuota([Bucket("gemini-weekly", 0.5, "Gemini Models · Weekly Limit")], null, null);

            Assert.Equal("Gemini Models · Weekly Limit", vm.Models[0].DisplayName);
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

            Assert.Equal(["gemini-weekly"], vm.Models.Select(m => m.Key));
            // 걸러진 행은 트레이 게이지 값에도 반영되지 않는다.
            Assert.Equal(0.5, vm.Percent, 3);
        }
    }

    // 다중 PC 동기화 최후 폴백은 최대 24시간 전 관측치까지 받을 수 있다. 그 사이 이 창만
    // 리셋됐다면 remainingFraction 은 리셋 전 값이라 실제보다 적게 남은 것처럼 보인다 —
    // 다음 실제 조회 전까지는 아예 숨기는 편이 낫다. 리셋 전인 다른 행은 그대로 남는다.
    [Fact]
    public void ApplyQuota_SkipsRows_WhoseResetTimeAlreadyPassed()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ApplyQuota(
            [
                Bucket("gemini-weekly", 0.3, reset: DateTimeOffset.Now.AddHours(2)),
                Bucket("gemini-5h", 0.03, reset: DateTimeOffset.Now.AddMinutes(-1)),
            ], null, null);

            Assert.Equal(["gemini-weekly"], vm.Models.Select(m => m.Key));
            // 걸러진 행(97% 사용)이 대표값에 반영되면 안 된다.
            Assert.Equal(0.7, vm.Percent, 3);
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
            vm.ShowThirdPartyQuota = true;
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
            var weekly = vm.Models.Single(m => m.Key == "gemini-weekly");
            var fiveHour = vm.Models.Single(m => m.Key == "3p-5h");
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
                    ModelId = "gemini-5h", TokenType = "5h",
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
    public void ApplyQuota_AddsAbsoluteResetTime_ToTheLabel_WhenSettingIsOn()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ShowAbsoluteResetTime = true;
            vm.ApplyQuota([Bucket("gemini-5h", 0.5, window: "5h")], null, null);

            // Claude·Codex·OpenCode 와 같은 방식 — 설정이 켜지면 라벨에 괄호로 절대 시각이 붙는다.
            // 이름이 길어 잘리는 경우는 이름 TextBlock 의 CharacterEllipsis + 툴팁이 감당한다(#139).
            Assert.Contains("(", vm.Models[0].ResetAtLabel);
            Assert.DoesNotContain("(", vm.Models[0].PaceTip);
        }
    }

    [Fact]
    public void ApplyQuota_OmitsAbsoluteResetTime_FromTheLabel_WhenSettingIsOff()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 기본값(false) — Claude·Codex 와 마찬가지로 설정이 꺼져 있으면 어디에도 절대 시각을 붙이지 않는다.
            vm.ApplyQuota([Bucket("gemini-5h", 0.5, window: "5h")], null, null);

            Assert.DoesNotContain("(", vm.Models[0].ResetAtLabel);
            Assert.DoesNotContain("(", vm.Models[0].PaceTip);
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
                    ModelId = "gemini-5h", TokenType = "5h",
                    RemainingFraction = 0.4, ResetTime = now.AddHours(2.5),
                },
            ], null, null);

            vm.UpdateTimeProgress(now);

            // Claude·Codex 게이지 툴팁과 같은 페이스 문구 (시간 50% 경과 · 사용 60%).
            Assert.Equal(Loc.PaceTip(0.5, 0.6, settled: true), vm.Models[0].PaceTip);
        }
    }

    /// <summary>Gemini 4 Argon 주간 한도(#204) — 공유 풀과 별개로 자기 그룹으로 온다.</summary>
    private static AntigravityModelQuota Argon(double remaining) =>
        new()
        {
            ModelId = "gemini-argon-wtu-weekly",
            TokenType = "weekly",
            GroupName = "Gemini 4 Argon",
            DisplayName = "Gemini 4 Argon Weekly Limit",
            RemainingFraction = remaining,
            ResetTime = Reset,
        };

    [Theory]
    [InlineData("ko", "Gemini 4 Argon · 주간")]
    [InlineData("en", "Gemini 4 Argon · Weekly")]
    [InlineData("ja", "Gemini 4 Argon · 週間")]
    [InlineData("zh", "Gemini 4 Argon · 每周")]
    public void ResolveDisplayName_NamesAModelOnlyGroup_ByItsServerName_AndTheAppLanguageWindow(string lang, string expected)
    {
        var previous = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage(lang);

            // 모델 이름은 고유 명사라 그대로, 창 이름만 다른 행과 같은 말로 부른다.
            Assert.Equal(expected, AntigravityViewModel.ResolveDisplayName(Argon(0.71)));
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    [Fact]
    public void ApplyQuota_PutsArgonBelowTheSharedPools_AndKeepsItOutOfTheTrayValue()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ShowThirdPartyQuota = true;
            // 서버가 Argon 그룹을 맨 앞에 보내도(그룹 순서는 보장되지 않는다) 공유 풀 아래로 간다.
            vm.ApplyQuota(
            [
                Argon(0.0),                                          // Argon 은 다 썼다
                Bucket("gemini-weekly", 0.80, window: "weekly"),
                Bucket("gemini-5h",     0.90, window: "5h"),
                Bucket("3p-weekly",     0.70, window: "weekly"),     // 공유 풀 중 가장 많이 씀(30%)
                Bucket("3p-5h",         1.00, window: "5h"),
            ], null, null);

            Assert.Equal(
                ["gemini-5h", "gemini-weekly", "3p-5h", "3p-weekly", "gemini-argon-wtu-weekly"],
                vm.Models.Select(m => m.Key));
            Assert.Equal(1.0, vm.Models[^1].UsagePercent, 3);
            // Argon 을 다 써도 다른 모델은 계속 쓸 수 있다 — 트레이는 공유 풀 기준(Claude 모델 한정 한도 #187 과 같은 규칙).
            Assert.Equal(0.30, vm.Percent, 3);
        }
    }

    [Fact]
    public void ApplyQuota_ShowsArgon_EvenWhenClaudeGptQuotaIsHidden()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 기본 설정(Claude·GPT 숨김) — Argon 은 Gemini 모델이라 그 설정과 상관없이 보인다.
            vm.ApplyQuota(
            [
                Bucket("gemini-weekly", 0.80, window: "weekly"),
                Bucket("3p-weekly",     0.10, window: "weekly"),
                Argon(0.71),
            ], null, null);

            Assert.Equal(["gemini-weekly", "gemini-argon-wtu-weekly"], vm.Models.Select(m => m.Key));
            Assert.Equal(0.20, vm.Percent, 3);
        }
    }

    [Fact]
    public void ApplyQuota_UsesEveryRowForTheTrayValue_WhenNoSharedPoolIsPresent()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 공유 풀 행이 하나도 없으면(구버전 동기화 값, 버킷 이름 규칙 변경) 트레이가 0% 로 거짓말하지 않는다.
            vm.ApplyQuota([Argon(0.40)], null, null);

            Assert.Equal(0.60, vm.Percent, 3);
        }
    }

    private static IReadOnlyList<AntigravityModelQuota> FourWindows() =>
    [
        Bucket("gemini-5h",     0.90, window: "5h"),       // 10% 사용
        Bucket("gemini-weekly", 0.80, window: "weekly"),   // 20% 사용
        Bucket("3p-5h",         0.10, window: "5h"),       // 90% 사용
        Bucket("3p-weekly",     0.50, window: "weekly"),   // 50% 사용
    ];

    [Fact]
    public void ApplyQuota_ShowsOnlyGeminiWindows_ByDefault()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ApplyQuota(FourWindows(), null, null);

            Assert.Equal(["gemini-5h", "gemini-weekly"], vm.Models.Select(m => m.Key));
            // 트레이 대표값도 보이는 창 기준이다 — Claude·GPT 5시간 창의 90% 가 끼면 팝업과 숫자가 어긋난다.
            Assert.Equal(0.20, vm.Percent, 3);
            // 자동 트레이 모드의 "지금 쓰는 중" 판정도 같은 행을 본다.
            Assert.True(MainViewModel.HasRecentAntigravityUsage(vm.Models));
        }
    }

    [Fact]
    public void ApplyQuota_DoesNotTreatClaudeUsageAsRecentAntigravityUse_WhenHidden()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // Claude·GPT 5시간 창만 썼다 — 숨긴 창으로 트레이를 Antigravity 로 넘기면 안 된다.
            vm.ApplyQuota(
            [
                Bucket("gemini-5h",     1.0, window: "5h"),
                Bucket("gemini-weekly", 1.0, window: "weekly"),
                Bucket("3p-5h",         0.4, window: "5h"),
            ], null, null);

            Assert.False(MainViewModel.HasRecentAntigravityUsage(vm.Models));
            Assert.Equal(0, vm.Percent);
        }
    }

    [Fact]
    public void ShowThirdPartyQuota_RebuildsRowsAndPercent_WithoutANewLookup()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ApplyQuota(FourWindows(), null, null);

            vm.ShowThirdPartyQuota = true;
            Assert.Equal(["gemini-5h", "gemini-weekly", "3p-5h", "3p-weekly"], vm.Models.Select(m => m.Key));
            Assert.Equal(0.90, vm.Percent, 3);

            vm.ShowThirdPartyQuota = false;
            Assert.Equal(["gemini-5h", "gemini-weekly"], vm.Models.Select(m => m.Key));
            Assert.Equal(0.20, vm.Percent, 3);
        }
    }

    [Fact]
    public void RebuildingRows_KeepsTheLocalErrorShownOverSyncedValues()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 다른 PC 값 위에 이 PC 의 진짜 오류를 살려 둔 상태(MainViewModel 의 최후 폴백 경로).
            vm.ApplyQuota(FourWindows(), null, null, 300);
            vm.HasError = true;
            vm.ErrorMessage = "login required";

            // 표시 설정·언어만 바뀌었을 뿐이므로 오류와 크레딧은 그대로 남아야 한다.
            vm.ShowThirdPartyQuota = true;
            vm.RefreshLocalizedLabels();

            Assert.True(vm.HasError);
            Assert.Equal("login required", vm.ErrorMessage);
            Assert.Equal(300L.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), vm.AiCreditsText);
        }
    }

    [Theory]
    [InlineData("3p-5h", true)]
    [InlineData("3p-weekly", true)]
    [InlineData("gemini-5h", false)]
    [InlineData("gemini-image-5h", false)]
    [InlineData("3p-image-5h", false)]     // 그룹 키가 정확히 3p 인 것만 Claude·GPT 창이다
    public void IsThirdPartyBucket_MatchesTheClaudeAndGptGroupOnly(string bucketId, bool expected)
    {
        Assert.Equal(expected, AntigravityViewModel.IsThirdPartyBucket(bucketId));
    }

    [Theory]
    [InlineData("ko")]
    [InlineData("en")]
    public void ResolveDisplayName_CallsAnImageGroupImageGeneration_NotGeminiModels(string lang)
    {
        var previous = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage(lang);

            // 요약 응답에서 아직 본 적은 없지만, gemini 로 시작하는 새 그룹이 오면 Gemini 모델 칸과 같은 이름이 되면 안 된다.
            var label = AntigravityViewModel.ResolveDisplayName(new AntigravityModelQuota
            {
                ModelId = "gemini-image-5h",
                TokenType = "5h",
                DisplayName = "Gemini Models · Images",
            });

            Assert.Equal($"{Loc.CodexImageGenerationLimit} · {Loc.QuotaWindowShortName(300)}", label);
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    [Fact]
    public void ResolveDisplayName_KeepsServerText_ForUnknownGroupStartingWithGemini()
    {
        var label = AntigravityViewModel.ResolveDisplayName(new AntigravityModelQuota
        {
            ModelId = "gemini-agent-5h",
            TokenType = "5h",
            DisplayName = "Gemini Agent Models · Five Hour Limit",
        });

        Assert.Equal("Gemini Agent Models · Five Hour Limit", label);
    }

    [Theory]
    [InlineData(12345L)]
    [InlineData(0L)]      // 다 쓴 경우도 보여준다 — 한도 소진 뒤 이어 쓸 수 없다는 뜻이다.
    public void ApplyQuota_FormatsAiCredits_WhenAccountIsEligible(long credits)
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ApplyQuota([Bucket("gemini-weekly", 0.5)], null, "Google AI Pro", credits);

            Assert.Equal(credits.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), vm.AiCreditsText);
        }
    }

    [Fact]
    public void ApplyQuota_LeavesAiCreditsEmpty_WhenAccountIsNotEligible()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            // 먼저 크레딧이 있던 값을 올렸다가, 크레딧 대상이 아닌 값(예: 다른 계정의 동기화 값)으로 바뀌면 줄이 사라져야 한다.
            vm.ApplyQuota([Bucket("gemini-weekly", 0.5)], null, null, 500);
            vm.ApplyQuota([Bucket("gemini-weekly", 0.5)], null, null);

            Assert.Equal("", vm.AiCreditsText);
        }
    }

    [Fact]
    public void RefreshLocalizedLabels_KeepsAiCredits()
    {
        var vm = CreateVm(out var monitor);
        using (monitor)
        {
            vm.ApplyQuota([Bucket("gemini-weekly", 0.5)], null, null, 750);

            vm.RefreshLocalizedLabels();

            Assert.Equal(750L.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), vm.AiCreditsText);
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
