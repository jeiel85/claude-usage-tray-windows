using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "claude-usage-tray.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public NotificationSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<NotificationSettings>(json) ?? new();
            }
        }
        catch { }
        return new NotificationSettings();
    }

    public void Save(NotificationSettings settings)
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch { }
    }
}
