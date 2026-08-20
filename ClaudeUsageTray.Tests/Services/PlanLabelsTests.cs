using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public sealed class PlanLabelsTests
{
    [Theory]
    // Max 는 5x/20x 로 한도가 4배 차이 나므로 배수까지 붙어야 등급 표시가 실제 한도와 맞는다.
    [InlineData("max", "default_claude_max_5x", "Claude Max 5x")]
    [InlineData("max", "default_claude_max_20x", "Claude Max 20x")]
    [InlineData("pro", "default_pro", "Claude Pro")]
    [InlineData("free", null, "Claude Free")]
    // 모르는 등급이라도 원문을 버리지 않는다 — 새 요금제가 생겨도 배지가 사라지지 않도록.
    [InlineData("team_seat", null, "Claude Team Seat")]
    public void Claude_FormatsSubscriptionWithRateLimitMultiplier(string? subscriptionType, string? tier, string expected)
        => Assert.Equal(expected, PlanLabels.Claude(subscriptionType, tier));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Claude_ReturnsEmptyWhenSubscriptionUnknown(string? subscriptionType)
        => Assert.Equal("", PlanLabels.Claude(subscriptionType, "default_claude_max_5x"));

    [Theory]
    [InlineData("plus", "ChatGPT Plus")]
    [InlineData("PRO", "ChatGPT Pro")]
    [InlineData(null, "")]
    public void Codex_FormatsPlanType(string? planType, string expected)
        => Assert.Equal(expected, PlanLabels.Codex(planType));

    [Theory]
    [InlineData("opencode-go", "OpenCode Go")]
    [InlineData("opencode", "OpenCode")]
    // 로그인 파일에는 다른 공급자 키도 함께 있으므로 OpenCode 자체 항목이 아니면 배지를 만들지 않는다.
    [InlineData("anthropic", "")]
    [InlineData(null, "")]
    public void OpenCode_FormatsOwnAuthEntryOnly(string? providerId, string expected)
        => Assert.Equal(expected, PlanLabels.OpenCode(providerId));
}
