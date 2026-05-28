namespace ClaudeUsageTray.Models;

public static class UsageProviderKind
{
    public const string Auto = "auto";
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string GeminiCli = "gemini-cli";
    public const string OpenCode = "opencode";
    public const string Antigravity = "antigravity";

    public static bool IsValid(string? value) =>
        value is Auto or Claude or Codex or GeminiCli or OpenCode or Antigravity;

    public static string DisplayName(string? kind) => kind switch
    {
        Claude       => "Claude",
        Codex        => "Codex",
        GeminiCli    => "Gemini CLI",
        OpenCode     => "OpenCode",
        Antigravity  => "Antigravity",
        _            => kind ?? ""
    };
}
