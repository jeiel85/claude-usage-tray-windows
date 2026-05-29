using ClaudeUsageTray.ViewModels;
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
        var result = ClaudeViewModel.ParseFriendlyError(raw);
        if (isRateLimit)
            Assert.Contains("제한", result);
        else
            Assert.DoesNotContain("429", result);
    }
}
