using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class CredentialService
{
    private static readonly string CredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    public ClaudeCredentials? Load()
    {
        if (!File.Exists(CredentialsPath)) return null;

        try
        {
            var json = File.ReadAllText(CredentialsPath);
            return JsonSerializer.Deserialize<ClaudeCredentials>(json);
        }
        catch
        {
            return null;
        }
    }

    public string? GetAccessToken()
    {
        return Load()?.ClaudeAiOauth?.AccessToken;
    }

    public bool HasCredentials() => File.Exists(CredentialsPath);
}
