namespace ClaudeUsageTray.Models;

public static class UsageProviderKind
{
    public const string Auto = "auto";
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string OpenCode = "opencode";
    public const string Antigravity = "antigravity";

    /// <summary>
    /// v1.42.x 까지 있던 Gemini CLI 공급자 키. Gemini CLI 가 Antigravity CLI 로 전환되면서(#183) 공급자는 없앴고,
    /// 예전 설정 파일에 남은 값을 Antigravity 로 옮길 때만 쓴다(<see cref="NotificationSettings.MigrateLegacyProviders"/>).
    /// </summary>
    public const string LegacyGeminiCli = "gemini-cli";

    public static bool IsValid(string? value) =>
        value is Auto or Claude or Codex or OpenCode or Antigravity;

    public static string DisplayName(string? kind) => kind switch
    {
        Claude       => "Claude",
        Codex        => "Codex",
        OpenCode     => "OpenCode",
        Antigravity  => "Antigravity",
        _            => kind ?? ""
    };
}
