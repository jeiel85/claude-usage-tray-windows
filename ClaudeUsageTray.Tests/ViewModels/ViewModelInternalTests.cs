using ClaudeUsageTray.Models;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

public class WeatherViewModelTests
{
    [Theory]
    [InlineData("clear", "☀")]
    [InlineData("mainly_clear", "☀")]
    [InlineData("partly_cloudy", "⛅")]
    [InlineData("overcast", "☁")]
    [InlineData("fog", "☁")]
    [InlineData("drizzle", "☂")]
    [InlineData("freezing_drizzle", "☂")]
    [InlineData("rain", "☔")]
    [InlineData("freezing_rain", "☔")]
    [InlineData("rain_showers", "☔")]
    [InlineData("snow", "❄")]
    [InlineData("snow_grains", "❄")]
    [InlineData("snow_showers", "❄")]
    [InlineData("thunderstorm", "⚡")]
    [InlineData("unknown", "•")]
    [InlineData("", "•")]
    public void GetIcon_ReturnsCorrectIcon(string conditionKey, string expected)
    {
        var result = WeatherViewModel.GetIcon(conditionKey);
        Assert.Equal(expected, result);
    }
}

public class AntigravityViewModelTests
{
    [Theory]
    [InlineData("gemini-2.5-flash", "Gemini 2.5 Flash")]
    [InlineData("gemini-3.1-flash-lite", "Gemini 3.1 Flash Lite")]
    [InlineData("claude-sonnet-4.6", "Claude Sonnet 4.6")]
    [InlineData("gpt-oss-120b", "Gpt Oss 120b")]
    [InlineData("gemini-3-flash-preview", "Gemini 3 Flash Preview")]
    [InlineData("", "(unknown)")]
    public void FormatModelName_FormatsCorrectly(string modelId, string expected)
    {
        var result = AntigravityViewModel.FormatModelName(modelId);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatResetLabel_ReturnsEmpty_WhenNull()
    {
        var result = AntigravityViewModel.FormatResetLabel(null);
        Assert.Equal("", result);
    }

    [Fact]
    public void FormatResetLabel_ReturnsEmpty_WhenPast()
    {
        var result = AntigravityViewModel.FormatResetLabel(DateTimeOffset.Now.AddHours(-1));
        Assert.Equal("", result);
    }

    [Fact]
    public void FormatResetLabel_ShowsMinutes_WhenLessThanOneHour()
    {
        var result = AntigravityViewModel.FormatResetLabel(DateTimeOffset.Now.AddMinutes(30));
        Assert.Contains("m", result);
    }
}

public class ClaudeViewModelTests
{
    [Theory]
    [InlineData("429 Too Many Requests", true)]
    [InlineData("rate_limit exceeded", true)]
    [InlineData("HTTP 500: Internal Server Error", false)]
    [InlineData("Something went wrong", false)]
    public void ParseFriendlyError_DetectsRateLimit(string raw, bool isRateLimit)
    {
        // ParseFriendlyError 는 실제로 MainViewModel 에 있다. 죽은 ClaudeViewModel 중복본은 하드닝에서 제거됨.
        var result = MainViewModel.ParseFriendlyError(raw);
        if (isRateLimit)
            Assert.Equal(Loc.RateLimited, result);
        else
            Assert.DoesNotContain("429", result);
    }
}

public class OpenCodeViewModelTests
{
    [Fact]
    public void CalculateTimeProgress_UsesEachOfficialWindowLength()
    {
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.FromHours(9));
        var usage = new OpenCodeWebUsage
        {
            Rolling = new OpenCodeQuotaWindow { ResetAt = now.AddHours(2.5) },
            Weekly = new OpenCodeQuotaWindow { ResetAt = now.AddDays(3.5) },
            // 8/10 12:00 ~ 9/10 12:00 의 정확한 중간이 8/26 00:00 이다.
            Monthly = new OpenCodeQuotaWindow
            {
                ResetAt = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.FromHours(9))
            },
        };

        var timeline = OpenCodeViewModel.CalculateTimeProgress(usage, now);

        Assert.NotNull(timeline.Rolling);
        Assert.NotNull(timeline.Weekly);
        Assert.NotNull(timeline.Monthly);
        Assert.Equal(0.5, timeline.Rolling.Value, 6);
        Assert.Equal(0.5, timeline.Weekly.Value, 6);
        Assert.Equal(0.5, timeline.Monthly.Value, 6);
    }

    [Fact]
    public void CalculateTimeProgress_HidesExpiredWindows()
    {
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.FromHours(9));
        var expired = new OpenCodeQuotaWindow { ResetAt = now.AddSeconds(-1) };
        var usage = new OpenCodeWebUsage
        {
            Rolling = expired,
            Weekly = expired,
            Monthly = expired,
        };

        var timeline = OpenCodeViewModel.CalculateTimeProgress(usage, now);

        Assert.Null(timeline.Rolling);
        Assert.Null(timeline.Weekly);
        Assert.Null(timeline.Monthly);
    }

    [Fact]
    public void ApplySyncedWebUsage_DoesNotReplaceAnAlreadyAppliedQuota()
    {
        var vm = new OpenCodeViewModel(new OpenCodeUsageMonitor(), new HistoryService());
        var first = CreateUsage(0.25);
        var later = CreateUsage(0.8);

        vm.ApplySyncedWebUsage(first);
        vm.ApplySyncedWebUsage(later);

        Assert.True(vm.HasWebQuota);
        Assert.Equal(0.25, vm.RollingPercent, 6);
        Assert.Same(first, vm.LastSnapshot.OpenCodeDetails?.WebUsage);
    }

    private static OpenCodeWebUsage CreateUsage(double percent)
    {
        var now = DateTimeOffset.Now;
        return new OpenCodeWebUsage
        {
            Rolling = new OpenCodeQuotaWindow { UsagePercent = percent, ResetAt = now.AddHours(3) },
            Weekly = new OpenCodeQuotaWindow { UsagePercent = percent, ResetAt = now.AddDays(4) },
            Monthly = new OpenCodeQuotaWindow { UsagePercent = percent, ResetAt = now.AddDays(20) },
        };
    }
}

/// <summary>
/// 다중 PC 동기화에서 "무엇을 공유해도 되는가" 규칙과, 받은 값을 화면에 옮기는 경로 검증.
/// </summary>
public class UsageSyncQuotaPolicyTests
{
    [Theory]
    [InlineData(UsageProviderKind.OpenCode, 5, 40)]
    [InlineData(UsageProviderKind.OpenCode, 60, 60)]
    [InlineData(UsageProviderKind.Codex, 5, 5)]
    public void OpenCodeQuotaTtl_CoversWebRetryAndCloudDelay(
        string provider, int configuredMinutes, int expectedMinutes)
    {
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            MainViewModel.UsageSyncQuotaTtl(provider, configuredMinutes));
    }

    // 서버가 계정별로 내려주는 할당량만 공유한다. OpenCode 는 로컬 계산 percent 가 아니라
    // 공식 웹 콘솔의 계정 단위 롤링·주간·월간 값만 공유한다.
    [Theory]
    [InlineData(UsageProviderKind.Codex, true)]
    [InlineData(UsageProviderKind.Antigravity, true)]
    [InlineData(UsageProviderKind.GeminiCli, false)]
    [InlineData(UsageProviderKind.OpenCode, true)]
    public void OnlyAccountLevelQuotaIsShared(string provider, bool shared)
    {
        Assert.Equal(shared, MainViewModel.UsageSyncSharesAccountQuota(provider));
    }

    // 이 PC 에 로컬 사용량이 없고 다른 PC 에만 있는 경우(예: OpenCode 를 다른 PC 에서만 씀),
    // MergeLocalTotals 는 "사용량이 있는 기기" 만 세므로 DeviceCount 가 1 이 된다.
    // 예전처럼 DeviceCount > 1 을 요구하면 그 값이 통째로 버려져
    // HideInactiveProviders 와 맞물려 공급자 섹션이 화면에서 사라졌다.
    [Fact]
    public void RemoteOnlyTotals_AreStillDisplayed()
    {
        var remoteOnly = new UsageSyncMergedLocalTotals
        {
            DeviceCount = 1,
            InputTokens = 248_796,
            OutputTokens = 42_958,
            RequestCount = 179,
        };

        Assert.True(MainViewModel.HasMergedDeviceTotals(remoteOnly));
    }

    [Fact]
    public void MergedTotals_WithoutAnyDevice_AreNotDisplayed()
    {
        Assert.False(MainViewModel.HasMergedDeviceTotals(null));
        Assert.False(MainViewModel.HasMergedDeviceTotals(new UsageSyncMergedLocalTotals { DeviceCount = 0 }));
    }

    [Fact]
    public void DeviceDerivedPercent_IsNeverWrittenToTheSharedFolder()
    {
        var snapshot = new ProviderUsageSnapshot
        {
            ShortUsagePercent = 0.73,
            ShortResetAt = DateTimeOffset.Now.AddHours(2),
        };

        Assert.Null(MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.GeminiCli, snapshot));
        Assert.Null(MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.OpenCode, snapshot));
        Assert.NotNull(MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.Codex, snapshot));
    }

    [Fact]
    public void OpenCodeOfficialQuota_RoundTripsAllWindows()
    {
        var observedAt = DateTimeOffset.Now.AddMinutes(-10);
        var rollingReset = DateTimeOffset.Now.AddHours(3);
        var weeklyReset = DateTimeOffset.Now.AddDays(4);
        var monthlyReset = DateTimeOffset.Now.AddDays(20);
        var snapshot = new ProviderUsageSnapshot
        {
            OpenCodeDetails = new OpenCodeUsageDetails
            {
                WebUsage = new OpenCodeWebUsage
                {
                    ObservedAtUtc = observedAt,
                    Rolling = new OpenCodeQuotaWindow { UsagePercent = 0.31, ResetAt = rollingReset },
                    Weekly = new OpenCodeQuotaWindow { UsagePercent = 0.42, ResetAt = weeklyReset },
                    Monthly = new OpenCodeQuotaWindow { UsagePercent = 0.53, ResetAt = monthlyReset },
                },
            },
        };

        var quota = MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.OpenCode, snapshot);
        var restored = MainViewModel.CreateOpenCodeWebUsage(quota);

        Assert.NotNull(quota?.OpenCode);
        Assert.NotNull(restored);
        Assert.Equal(observedAt, restored!.ObservedAtUtc);
        Assert.Equal(0.31, restored.Rolling.UsagePercent, 6);
        Assert.Equal(rollingReset, restored.Rolling.ResetAt);
        Assert.Equal(0.42, restored.Weekly.UsagePercent, 6);
        Assert.Equal(weeklyReset, restored.Weekly.ResetAt);
        Assert.Equal(0.53, restored.Monthly.UsagePercent, 6);
        Assert.Equal(monthlyReset, restored.Monthly.ResetAt);
    }

    [Fact]
    public void OpenCodeSyncedQuota_IsRejectedAfterRollingReset()
    {
        var now = DateTimeOffset.Now;
        var quota = new UsageSyncQuotaSnapshot
        {
            HasData = true,
            OpenCode = new UsageSyncOpenCodeQuota
            {
                Rolling = new UsageSyncOpenCodeQuotaWindow { ResetAt = now.AddSeconds(-1) },
                Weekly = new UsageSyncOpenCodeQuotaWindow { ResetAt = now.AddDays(2) },
                Monthly = new UsageSyncOpenCodeQuotaWindow { ResetAt = now.AddDays(20) },
            },
        };

        Assert.Null(MainViewModel.CreateOpenCodeWebUsage(quota, now));
    }

    // 창 길이가 빠지면 받는 PC 가 5시간으로 가정할 수밖에 없어 주간 창에서 시간선이 어긋난다.
    [Fact]
    public void CodexQuota_CarriesWindowLengths()
    {
        var quota = MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.Codex, new ProviderUsageSnapshot
        {
            ShortUsagePercent = 0.61,
            ShortResetAt = DateTimeOffset.Now.AddHours(3),
            ShortWindowMinutes = 10080,
            LongUsagePercent = 0.2,
            LongResetAt = DateTimeOffset.Now.AddDays(3),
            LongWindowMinutes = 43200,
            PlanType = "Plus",
        });

        Assert.NotNull(quota);
        Assert.Equal(10080, quota!.ShortWindowMinutes);
        Assert.Equal(43200, quota.LongWindowMinutes);
        Assert.True(quota.HasLongWindow);
        Assert.Equal("Plus", quota.PlanType);
    }

    // 리셋 시각이 없으면 어느 창의 값인지 알 수 없다 — 사용률만 떠서는 공유해도 쓸 데가 없다.
    [Fact]
    public void QuotaWithoutAnyResetTime_IsNotShared()
    {
        Assert.Null(MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.Codex, new ProviderUsageSnapshot
        {
            ShortUsagePercent = 0.5,
            PlanType = "Plus",
        }));
    }

    // Antigravity 에 로그인하지 않은 PC 도, 받은 모델 목록으로 로컬 조회와 같은 패널을 그린다.
    [Fact]
    public void Antigravity_AppliesSyncedModelRows_LikeALocalLookup()
    {
        var vm = new AntigravityViewModel(new AntigravityUsageMonitor());
        var resetAt = DateTimeOffset.Now.AddHours(4);

        vm.ApplyQuota(
            [
                new AntigravityModelQuota { ModelId = "gemini-3-pro", RemainingFraction = 0.25, ResetTime = resetAt },
                new AntigravityModelQuota { ModelId = "claude-sonnet-4-5", RemainingFraction = 0.9, ResetTime = resetAt },
                // 내부용 모델은 로컬 경로와 똑같이 제외된다.
                new AntigravityModelQuota { ModelId = "chat_default", RemainingFraction = 0.1, ResetTime = resetAt },
                // 리셋 시각이 없는 행도 제외.
                new AntigravityModelQuota { ModelId = "tab_default", RemainingFraction = 0.1, ResetTime = null },
            ],
            "Gemini Code Assist",
            "Gemini Code Assist in Google One AI Pro");

        Assert.True(vm.HasData);
        Assert.Equal("Gemini Code Assist", vm.TierName);
        Assert.Equal(2, vm.Models.Count);
        Assert.Equal("gemini-3-pro", vm.Models[0].ModelId);   // 사용률 내림차순
        Assert.Equal(0.75, vm.Models[0].UsagePercent, 6);
        Assert.Equal(0.10, vm.Models[1].UsagePercent, 6);
        Assert.Equal((0.75 + 0.10) / 2, vm.Percent, 6);       // 평균은 남은 두 모델 기준
    }
}
