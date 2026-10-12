using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class SettingsService
{
    private static readonly string DefaultSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "claude-usage-tray.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _settingsPath;

    /// <param name="settingsPath">설정 파일 경로. null 이면 ~/.claude/claude-usage-tray.json (테스트용 주입점).</param>
    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? DefaultSettingsPath;
    }

    public NotificationSettings Load()
    {
        var settings = LoadFromDisk();
        // 예전 파일은 다음 저장 때 새 형식으로 기록된다. 그 전까지는 읽을 때마다 같은 결과로 옮긴다.
        settings.MigrateLegacyProviders();
        return settings;
    }

    private NotificationSettings LoadFromDisk()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
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
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOpts));
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
