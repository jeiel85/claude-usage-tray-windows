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
        var settings = LoadFromDisk();
        // 예전 파일은 다음 저장 때 새 형식으로 기록된다. 그 전까지는 읽을 때마다 같은 결과로 옮긴다.
        settings.MigrateLegacyProviders();
        return settings;
    }

    private static NotificationSettings LoadFromDisk()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<NotificationSettings>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Load failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
        }
        return new NotificationSettings();
    }

    public void Save(NotificationSettings settings)
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Save failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
        }
    }
}
