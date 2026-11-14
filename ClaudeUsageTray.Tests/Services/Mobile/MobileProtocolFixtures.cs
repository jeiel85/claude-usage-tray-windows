using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services.Mobile;

namespace ClaudeUsageTray.Tests.Services.Mobile;

/// <summary>
/// <c>docs/mobile-protocol/*</c> 픽스처의 원본 객체. .NET 테스트는 이 객체를 직렬화한 결과가 픽스처와 같은지 보고,
/// Kotlin 테스트(<c>mobile/app/src/test/.../ProtocolTest.kt</c>)는 같은 파일을 읽는다 — 두 쪽이 말없이 어긋나지 않게.
/// 의도적으로 형식을 바꿀 때는 <c>CUT_UPDATE_GOLDEN=1</c> 로 테스트를 돌려 픽스처를 다시 쓴다.
/// </summary>
public static class MobileProtocolFixtures
{
    public const string PcId = "0123456789abcdef0123456789abcdef";
    public const string Pin = "4f9a8a7d1c2b3e4f5a6b7c8d9e0f11223344556677889900aabbccddeeff0011";

    public static readonly DateTimeOffset Now = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<MobileEndpoint> Endpoints() =>
    [
        new(MobileTransport.Lan, "192.168.0.10", MobileProtocol.DefaultPort, true, Pin),
        new(MobileTransport.Tailscale, "home-pc.tail1234.ts.net", MobileProtocol.DefaultPort, false),
        new(MobileTransport.Tailscale, "100.101.102.103", MobileProtocol.DefaultPort, false),
    ];

    public static PairingOffer Offer() =>
        new(MobileProtocol.OfferMarker, PcId, "HOME-PC", "q1w2e3r4t5y6u7i8o9p0aA", Now.AddMinutes(5), Endpoints());

    /// <summary>
    /// 같은 Claude 계정(해시 고정), 계정을 아는 Codex, 계정을 모르는 Antigravity(기기 범위)를 담은 스냅샷.
    /// 계정 해시는 픽스처 안정성을 위해 <see cref="MobileAccountHash.For"/> 로 계산한 값을 그대로 쓴다.
    /// </summary>
    public static UsageSnapshotMessage Snapshot()
    {
        var (claudeHash, claudeScope) = MobileAccountHash.For("claude", "org-uuid-1|account-uuid-1", PcId);
        var (codexHash, codexScope) = MobileAccountHash.For("codex", "acct-1|user-1", PcId);
        var (agHash, agScope) = MobileAccountHash.For("antigravity", null, PcId);
        return new UsageSnapshotMessage(
            MobileProtocol.Schema,
            Now,
            PcId,
            "HOME-PC",
            "1.53.0",
            [
                new MobileProviderUsage("claude", claudeHash, claudeScope, "김민준 · Acme Corp", Now.AddSeconds(-30), "",
                    new UsageSyncQuotaSnapshot
                    {
                        HasData = true,
                        ObservedAtUtc = Now.AddSeconds(-30),
                        ShortUsagePercent = 0.42,
                        ShortResetAt = Now.AddHours(2),
                        LongUsagePercent = 0.18,
                        LongResetAt = Now.AddDays(3),
                        ExtraUsageEnabled = true,
                        ExtraHasLimit = true,
                        ExtraUsagePercent = 0.05,
                        ExtraCreditsLabel = "$2.50 / $50.00",
                        PlanType = "max",
                        AdditionalQuotas =
                        [
                            new UsageSyncAdditionalQuota
                            {
                                LimitId = "opus", LimitName = "Opus", UsagePercent = 0.7,
                                ResetAt = Now.AddDays(3), WindowMinutes = 10080, ObservedAt = Now.AddSeconds(-30),
                            },
                        ],
                    },
                    new MobileTodayTotals(120_000, 34_000, 2_500_000, 80_000, 3, 41)),
                new MobileProviderUsage("codex", codexHash, codexScope, "", Now.AddMinutes(-2), "",
                    new UsageSyncQuotaSnapshot
                    {
                        HasData = true,
                        ObservedAtUtc = Now.AddMinutes(-2),
                        ShortUsagePercent = 0.1,
                        ShortResetAt = Now.AddHours(4),
                        ShortWindowMinutes = 300,
                        LongUsagePercent = 0.6,
                        LongResetAt = Now.AddDays(5),
                        LongWindowMinutes = 10080,
                        HasLongWindow = true,
                        PlanType = "plus",
                    },
                    null),
                new MobileProviderUsage("antigravity", agHash, agScope, "", Now.AddMinutes(-1), "api_failed", null, null),
            ]);
    }
}
