namespace ClaudeUsageTray.Models;

public static class UsageProviderKind
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string GeminiCli = "gemini-cli";

    public static bool IsValid(string? value) =>
        value is Claude or Codex or GeminiCli;
}
