using System.Text.Json;
using ClaudeUsageTray.Models;
using Xunit;

namespace ClaudeUsageTray.Tests.Models;

// Gemini CLI 공급자를 없애고 Antigravity 설정으로 옮긴 변경(#183)의 회귀 테스트.
// 설정 파일은 SettingsService 와 같은 기본 옵션으로 읽고, 읽은 직후 이관을 적용한다.
public class NotificationSettingsMigrationTests
{
    private static NotificationSettings LoadLikeSettingsService(string json)
    {
        var settings = JsonSerializer.Deserialize<NotificationSettings>(json)!;
        settings.MigrateLegacyProviders();
        return settings;
    }

    [Fact]
    public void LegacyGeminiCliSettings_MoveToAntigravity()
    {
        var settings = LoadLikeSettingsService("""
            {
              "TrayDisplayMode": "gemini-cli",
              "VisibleProviders": ["claude", "codex", "gemini-cli", "opencode"],
              "FocusedProvider": "gemini-cli"
            }
            """);

        Assert.DoesNotContain(UsageProviderKind.LegacyGeminiCli, settings.VisibleProviders);
        Assert.Contains(UsageProviderKind.Antigravity, settings.VisibleProviders);
        Assert.Equal(UsageProviderKind.Antigravity, settings.TrayDisplayMode);
        Assert.Equal(UsageProviderKind.Antigravity, settings.FocusedProvider);
        Assert.Equal(NotificationSettings.CurrentProviderSettingsVersion, settings.ProviderSettingsVersion);
    }

    // 예전에는 Antigravity 표시 설정이 없어 늘 보였다. Gemini CLI 만 꺼 두었던 사용자에게서
    // Antigravity 까지 사라지면 안 된다.
    [Fact]
    public void LegacySettingsWithGeminiCliHidden_KeepAntigravityVisible()
    {
        var settings = LoadLikeSettingsService("""
            { "VisibleProviders": ["claude", "codex", "opencode"] }
            """);

        Assert.Equal(
            [UsageProviderKind.Claude, UsageProviderKind.Codex, UsageProviderKind.OpenCode, UsageProviderKind.Antigravity],
            settings.VisibleProviders);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("codex")]
    [InlineData("opencode")]
    public void LegacyTrayModeOtherThanGeminiCli_IsKept(string trayMode)
    {
        var settings = LoadLikeSettingsService($$"""{ "TrayDisplayMode": "{{trayMode}}" }""");

        Assert.Equal(trayMode, settings.TrayDisplayMode);
    }

    // 이관이 끝난 파일에서 사용자가 Antigravity 를 끈 상태는 다시 켜지면 안 된다.
    [Fact]
    public void MigratedSettingsWithAntigravityHidden_StayHidden()
    {
        var settings = LoadLikeSettingsService("""
            { "VisibleProviders": ["claude", "codex"], "ProviderSettingsVersion": 1 }
            """);

        Assert.DoesNotContain(UsageProviderKind.Antigravity, settings.VisibleProviders);
    }

    [Fact]
    public void Migration_SurvivesSaveAndReload()
    {
        var first = LoadLikeSettingsService("""
            { "VisibleProviders": ["claude", "gemini-cli"], "TrayDisplayMode": "gemini-cli" }
            """);
        first.VisibleProviders.Remove(UsageProviderKind.Antigravity);   // 이관 뒤 사용자가 Antigravity 를 끔
        first.TrayDisplayMode = UsageProviderKind.Auto;

        var reloaded = LoadLikeSettingsService(JsonSerializer.Serialize(first));

        Assert.Equal([UsageProviderKind.Claude], reloaded.VisibleProviders);
        Assert.Equal(UsageProviderKind.Auto, reloaded.TrayDisplayMode);
    }

    [Theory]
    [InlineData("""{ "VisibleProviders": null }""")]
    [InlineData("""{ "VisibleProviders": ["antigravity", "gemini-cli", "antigravity"] }""")]
    public void BrokenLegacyProviderList_EndsWithAntigravityOnce(string json)
    {
        var settings = LoadLikeSettingsService(json);

        Assert.Equal([UsageProviderKind.Antigravity], settings.VisibleProviders);
    }

    [Fact]
    public void DefaultSettings_ShowEveryProviderOnce()
    {
        var settings = new NotificationSettings();
        settings.MigrateLegacyProviders();

        Assert.Equal(
            [UsageProviderKind.Claude, UsageProviderKind.Codex, UsageProviderKind.OpenCode, UsageProviderKind.Antigravity],
            settings.VisibleProviders);
    }

    [Fact]
    public void LegacyGeminiCliKey_IsNoLongerAValidProvider()
    {
        Assert.False(UsageProviderKind.IsValid(UsageProviderKind.LegacyGeminiCli));
        Assert.True(UsageProviderKind.IsValid(UsageProviderKind.Antigravity));
    }
}
