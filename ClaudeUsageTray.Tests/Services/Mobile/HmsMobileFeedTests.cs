using System.Text.Json;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Mobile;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.Services.Mobile;

/// <summary>
/// DeepSeek(100milionseek) 일일 한도를 휴대폰 피드에 싣는 규칙(#318). 하루 창은 기존 short 창 필드로 보내
/// 업데이트하지 않은 휴대폰 앱도 그대로 그린다 — 휴대폰 쪽 파싱은 mobile 의 <c>ProtocolTest</c> 가 같은 JSON 으로 본다.
/// </summary>
public class HmsMobileFeedTests
{
    private static readonly DateTimeOffset Now = MobileProtocolFixtures.Now;
    private static readonly DateTimeOffset NextUtcMidnight = new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

    private static HmsReadResult Ok(long used, string keyId = "key-a") =>
        new(HmsReadState.Ok, new HmsDailyUsage(used, 400_000_000, 400_000_000 - used, 10, NextUtcMidnight, null), keyId);

    [Fact]
    public void Quota_IsOneDailyShortWindow()
    {
        var quota = HmsViewModel.CreateMobileQuota(Ok(100_000_000), Now)!;

        Assert.True(quota.HasData);
        Assert.Equal(0.25, quota.ShortUsagePercent);
        Assert.Equal(NextUtcMidnight, quota.ShortResetAt);
        Assert.Equal(24 * 60, quota.ShortWindowMinutes);
        Assert.False(quota.HasLongWindow);
        Assert.Null(quota.LongResetAt);
        Assert.Equal(Now, quota.ObservedAtUtc);
        Assert.Empty(quota.Models);
        Assert.Null(quota.OpenCode);

        // 휴대폰이 읽는 이름 그대로 나간다(camelCase 는 피드 직렬화가 정한다).
        var json = JsonSerializer.Serialize(quota, MobileProtocol.Json);
        Assert.Contains("\"shortWindowMinutes\":1440", json);
        Assert.Contains("\"shortUsagePercent\":0.25", json);
    }

    [Theory]
    [InlineData(HmsReadState.Unavailable)]
    [InlineData(HmsReadState.InvalidKey)]
    [InlineData(HmsReadState.NoKey)]
    public void Quota_IsSentOnlyForAFreshSuccess(HmsReadState state)
    {
        Assert.Null(HmsViewModel.CreateMobileQuota(new HmsReadResult(state, KeyId: "key-a"), Now));
        Assert.Null(HmsViewModel.CreateMobileQuota(null, Now));
    }

    [Fact]
    public void Quota_IsNotSentAfterTheDayReset()
    {
        Assert.Null(HmsViewModel.CreateMobileQuota(Ok(1), NextUtcMidnight.AddSeconds(1)));
    }

    [Fact]
    public void FeedCarriesTheLastValueOnlyForTheSameKey()
    {
        var feed = new MobileUsageFeed(new FixedTime(Now));
        feed.Publish(new MobileFeedEntry(HmsUsageService.MobileProvider, "key-a", "", Now, "",
            HmsViewModel.CreateMobileQuota(Ok(100_000_000), Now), null));

        // 같은 키로 실패하면 직전 값을 원래 관측 시각 그대로 잇는다.
        feed.Publish(new MobileFeedEntry(HmsUsageService.MobileProvider, "key-a", "", Now, "source_error", null, null));
        var kept = feed.Current("pc-a").Single();
        Assert.Equal(HmsUsageService.MobileProvider, kept.Provider);
        Assert.Equal("account", kept.AccountScope);
        Assert.Equal(0.25, kept.Quota!.ShortUsagePercent);

        // 키를 바꾼 뒤 실패면 옛 키의 값을 새 키의 것처럼 보이지 않는다.
        feed.Publish(new MobileFeedEntry(HmsUsageService.MobileProvider, "key-b", "", Now, "source_error", null, null));
        Assert.Null(feed.Current("pc-a").Single().Quota);
    }

    [Fact]
    public void FeedOrdersDeepSeekAfterOpenCode()
    {
        var feed = new MobileUsageFeed(new FixedTime(Now));
        foreach (var provider in new[] { "antigravity", HmsUsageService.MobileProvider, "opencode", "claude" })
            feed.Publish(new MobileFeedEntry(provider, null, "", Now, "", null, null));

        Assert.Equal(["claude", "opencode", HmsUsageService.MobileProvider, "antigravity"],
            feed.Current("pc-a").Select(p => p.Provider));
    }

    [Fact]
    public void FeedKeepsDeepSeekOnlyWhileAKeyIsSaved()
    {
        var withKey = new NotificationSettings { VisibleProviders = ["claude"], HmsApiKeyProtected = "protected" };
        var withoutKey = new NotificationSettings { VisibleProviders = ["claude"], HmsApiKeyProtected = "" };

        Assert.Contains(HmsUsageService.MobileProvider, MainViewModel.MobileFeedProviders(withKey));
        Assert.DoesNotContain(HmsUsageService.MobileProvider, MainViewModel.MobileFeedProviders(withoutKey));

        var feed = new MobileUsageFeed(new FixedTime(Now));
        feed.Publish(new MobileFeedEntry(HmsUsageService.MobileProvider, "key-a", "", Now, "", null, null));
        feed.Retain(MainViewModel.MobileFeedProviders(withoutKey));
        Assert.Empty(feed.Current("pc-a"));
    }

    // #319 리뷰: 조회 중에 키를 지우거나 바꾸면 늦게 끝난 옛 키의 결과가 휴대폰 카드를 되살렸다.
    [Fact]
    public void LateResultForADeletedOrReplacedKeyIsNotPublished()
    {
        Assert.Equal(MainViewModel.HmsMobileAction.Publish, MainViewModel.DecideHmsMobile(Ok(1, "key-a"), "key-a"));
        Assert.Equal(MainViewModel.HmsMobileAction.Publish,
            MainViewModel.DecideHmsMobile(new HmsReadResult(HmsReadState.Unavailable, KeyId: "key-a"), "key-a"));

        // 키를 지웠다: 늦게 온 성공 결과도 싣지 않고 카드를 뺀다.
        Assert.Equal(MainViewModel.HmsMobileAction.Remove, MainViewModel.DecideHmsMobile(Ok(1, "key-a"), null));
        // 키를 바꿨다: 옛 키의 결과는 건너뛴다(다음 조회가 새 키로 싣는다).
        Assert.Equal(MainViewModel.HmsMobileAction.Skip, MainViewModel.DecideHmsMobile(Ok(1, "key-a"), "key-b"));
        Assert.Equal(MainViewModel.HmsMobileAction.Remove,
            MainViewModel.DecideHmsMobile(new HmsReadResult(HmsReadState.NoKey), "key-a"));
        Assert.Equal(MainViewModel.HmsMobileAction.Skip, MainViewModel.DecideHmsMobile(null, "key-a"));

        var feed = new MobileUsageFeed(new FixedTime(Now));
        feed.Publish(new MobileFeedEntry(HmsUsageService.MobileProvider, "key-a", "", Now, "", null, null));
        feed.Remove(HmsUsageService.MobileProvider);
        Assert.Empty(feed.Current("pc-a"));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
