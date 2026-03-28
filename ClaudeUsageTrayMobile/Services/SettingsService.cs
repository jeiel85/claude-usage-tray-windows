using System.Text.Json;
using ClaudeUsageTrayMobile.Models;

namespace ClaudeUsageTrayMobile.Services;

/// <summary>
/// Persists app settings using MAUI Preferences (backed by SharedPreferences on Android,
/// NSUserDefaults on iOS).
/// </summary>
public class SettingsService
{
    private const string Key = "app_settings_json";
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public AppSettings Load()
    {
        try
        {
            var json = Preferences.Get(Key, null);
            if (!string.IsNullOrEmpty(json))
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Preferences.Set(Key, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch { }
    }
}
