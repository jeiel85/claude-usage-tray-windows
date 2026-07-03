using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class LocalizationServiceTests
{
    [Theory]
    [InlineData("ko", "새 버전 v9.9.9 업데이트", "클릭")]
    [InlineData("zh", "新版本 v9.9.9 可用", "点击")]
    [InlineData("ja", "新バージョン v9.9.9 が利用可能", "クリック")]
    [InlineData("en", "Update v9.9.9 available", "click")]
    public void UpdateAvailable_DoesNotIncludeInstallPrompt(string lang, string expected, string forbidden)
    {
        var originalLang = Loc.CurrentLang;

        try
        {
            Loc.SetLanguage(lang);

            var result = Loc.UpdateAvailable("v9.9.9");

            Assert.Equal(expected, result);
            Assert.False(result.Contains(forbidden, StringComparison.OrdinalIgnoreCase), result);
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }

    // "No access token found" 는 UI에 그대로 노출되면 안 되고(내부 sentinel),
    // 대신 언어별로 구체적 로그인 조치를 안내해야 한다.
    [Theory]
    [InlineData("ko")]
    [InlineData("zh")]
    [InlineData("ja")]
    [InlineData("en")]
    public void NoToken_IsActionable_AndNeverTheRawSentinel(string lang)
    {
        var originalLang = Loc.CurrentLang;

        try
        {
            Loc.SetLanguage(lang);

            var msg = Loc.NoToken;

            Assert.False(string.IsNullOrWhiteSpace(msg));
            // 내부용 원문 sentinel 이 사용자에게 그대로 보이면 안 된다.
            Assert.NotEqual(UsageApiService.NoTokenError, msg);
            // 무엇을 해야 하는지(claude 로그인)를 담고 있어야 한다.
            Assert.Contains("claude", msg, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }
}
