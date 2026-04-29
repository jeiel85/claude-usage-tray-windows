namespace ClaudeUsageTray.Models;

public static class UsageProviderKind
{
    public const string Auto = "auto";
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string GeminiCli = "gemini-cli";
    public const string OpenCode = "opencode";

    public static bool IsValid(string? value) =>
        value is Auto or Claude or Codex or GeminiCli or OpenCode;
}
