using System;
using System.Collections.Generic;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 알림 중복 방지 캐시. v1.36.x 까지는 발송 시각을 남기지 않아 정리가 불가능했고,
/// 공식 특보 키는 매 폴링마다 통째로 지워져 같은 특보가 반복 발송됐다.
/// </summary>
public class WeatherAlertCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void PruneCache_RemovesEntriesOlderThanRetentionWindow()
    {
        var cache = new WeatherAlertService.AlertCache();
        cache.Mark("stale", Now.AddDays(-8));
        cache.Mark("fresh", Now.AddDays(-1));

        WeatherAlertService.PruneCache(cache, Now);

        Assert.False(cache.Contains("stale"));
        Assert.True(cache.Contains("fresh"));
    }

    /// <summary>
    /// 이전 구현은 official/nws 키를 정리 때마다 전부 지워 중복 방지가 무력화됐다.
    /// 보관 기간 안의 키는 종류와 무관하게 남아야 한다.
    /// </summary>
    [Fact]
    public void PruneCache_KeepsRecentOfficialAlertKeys()
    {
        var cache = new WeatherAlertService.AlertCache();
        cache.Mark("official:NWS:urn:oid:2.49.0.1.840.0.abc", Now.AddHours(-2));
        cache.Mark("official:JMA:130000:03:1754000000", Now.AddHours(-2));

        WeatherAlertService.PruneCache(cache, Now);

        Assert.True(cache.Contains("official:NWS:urn:oid:2.49.0.1.840.0.abc"));
        Assert.True(cache.Contains("official:JMA:130000:03:1754000000"));
    }

    [Fact]
    public void PruneCache_RecordsLastPrunedAt()
    {
        var cache = new WeatherAlertService.AlertCache();

        WeatherAlertService.PruneCache(cache, Now);

        Assert.Equal(Now, cache.LastPrunedAt);
    }

    [Fact]
    public void MigrateLegacyKeys_MovesOldKeysIntoTimestampedEntries()
    {
        var cache = new WeatherAlertService.AlertCache
        {
            SentKeys = ["daily:20260801:37.48:126.89", "condition:heat:2026080114:33:37.48:126.89"]
        };

        cache.MigrateLegacyKeys(Now);

        Assert.True(cache.Contains("daily:20260801:37.48:126.89"));
        Assert.True(cache.Contains("condition:heat:2026080114:33:37.48:126.89"));
        // 마이그레이션이 끝난 뒤에는 파일에 구형식을 다시 쓰지 않는다.
        Assert.Null(cache.SentKeys);
    }

    /// <summary>
    /// 구형식 키는 발송 시각을 알 수 없다. 당장은 재발송을 막되 다음 정리에서 빠지도록
    /// 보관 기간 경계 직전 시각을 부여한다.
    /// </summary>
    [Fact]
    public void MigrateLegacyKeys_AssignsNearExpiryTimestamp()
    {
        var cache = new WeatherAlertService.AlertCache { SentKeys = ["legacy"] };

        cache.MigrateLegacyKeys(Now);
        Assert.True(cache.Contains("legacy"));

        WeatherAlertService.PruneCache(cache, Now);
        Assert.True(cache.Contains("legacy"));

        WeatherAlertService.PruneCache(cache, Now.AddHours(2));
        Assert.False(cache.Contains("legacy"));
    }

    [Fact]
    public void MigrateLegacyKeys_DoesNotOverwriteExistingEntries()
    {
        var cache = new WeatherAlertService.AlertCache { SentKeys = ["shared"] };
        cache.Mark("shared", Now);

        cache.MigrateLegacyKeys(Now);

        Assert.Equal(Now, cache.Entries["shared"]);
    }

    [Fact]
    public void MigrateLegacyKeys_IsNoOp_WhenNoLegacyKeys()
    {
        var cache = new WeatherAlertService.AlertCache();
        cache.Mark("kept", Now);

        cache.MigrateLegacyKeys(Now);

        Assert.True(cache.Contains("kept"));
        Assert.Null(cache.SentKeys);
    }

    [Fact]
    public void Mark_OverwritesTimestampForSameKey()
    {
        var cache = new WeatherAlertService.AlertCache();
        cache.Mark("k", Now.AddDays(-3));
        cache.Mark("k", Now);

        Assert.Equal(Now, cache.Entries["k"]);
    }
}
