using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 설정 파일 읽기·쓰기(#331). 쓰기는 한 잠금 아래에서 임시 파일에 쓴 뒤 바꿔 끼워, 쓰는 도중 앱이 죽어도 원래 파일이
/// 반쯤 쓰인 채로 남지 않는다. 바꿔 끼울 때 직전 파일을 <c>.bak</c> 으로 남긴다. 읽기가 실패하면(깨진 파일) 그 파일을
/// <c>.corrupt-…</c> 로 옆에 옮겨 두고 <c>.bak</c> 에서 읽는다 — 예전에는 기본값을 돌려줘 다음 저장이 모든 설정을 덮어썼다.
/// 일부 값만 바꾸는 곳은 <see cref="Update"/> 로 읽기·바꾸기·쓰기를 한 잠금 안에서 한다(다른 스레드의 저장과 겹쳐
/// 한쪽 변경이 사라지지 않게).
/// </summary>
public class SettingsService
{
    private static readonly string DefaultSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "claude-usage-tray.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>프로세스 안의 모든 인스턴스가 같은 파일을 쓰므로 잠금도 하나다.</summary>
    private static readonly object FileLock = new();

    private readonly string _settingsPath;

    /// <param name="settingsPath">설정 파일 경로. null 이면 ~/.claude/claude-usage-tray.json (테스트용 주입점).</param>
    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? DefaultSettingsPath;
    }

    internal string BackupPath => _settingsPath + ".bak";
    private string TempPath => _settingsPath + ".tmp";

    public NotificationSettings Load()
    {
        lock (FileLock) return LoadLocked();
    }

    public void Save(NotificationSettings settings)
    {
        lock (FileLock) SaveLocked(settings);
    }

    /// <summary>
    /// Input : 지금 디스크 값을 받아 저장할 값을 돌려주는 함수(바꿀 것이 없으면 null)
    /// Output: 저장했으면 그 값, 아니면 null
    /// 핵심 로직: 읽기부터 쓰기까지 한 잠금 안에서 한다 — 그 사이에 다른 스레드가 저장한 값을 덮어쓰지 않는다.
    /// </summary>
    public NotificationSettings? Update(Func<NotificationSettings, NotificationSettings?> change)
    {
        lock (FileLock)
        {
            var next = change(LoadLocked());
            if (next is not null) SaveLocked(next);
            return next;
        }
    }

    private NotificationSettings LoadLocked()
    {
        var settings = LoadFromDisk();
        // 예전 파일은 다음 저장 때 새 형식으로 기록된다. 그 전까지는 읽을 때마다 같은 결과로 옮긴다.
        settings.MigrateLegacyProviders();
        return settings;
    }

    private NotificationSettings LoadFromDisk()
    {
        if (!File.Exists(_settingsPath))
            return TryRead(BackupPath) ?? new NotificationSettings(); // 바꿔 끼우는 순간에 죽었으면 .bak 만 남는다.

        if (TryRead(_settingsPath) is { } settings) return settings;

        // 깨진 파일: 그대로 두면 다음 저장이 덮어써 되살릴 길이 없어진다. 옆으로 옮겨 두고 직전 정상본에서 읽는다.
        PreserveCorruptFile();
        return TryRead(BackupPath) ?? new NotificationSettings();
    }

    /// <summary>읽어서 해석한다. 없거나 비었거나 JSON 이 아니면 null.</summary>
    private static NotificationSettings? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<NotificationSettings>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log($"read {Path.GetFileName(path)} failed: {ex.Message}");
            return null;
        }
    }

    private void PreserveCorruptFile()
    {
        try
        {
            File.Move(_settingsPath, $"{_settingsPath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"keeping corrupt settings failed: {ex.Message}");
        }
    }

    private void SaveLocked(NotificationSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            using (var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_settingsPath))
                File.Replace(TempPath, _settingsPath, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(TempPath, _settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException or JsonException)
        {
            // 저장 실패는 앱을 멈추지 않는다 — 디스크 가득·권한, 그리고 JSON 으로 쓸 수 없는 값(NaN·∞ 는 ArgumentException,
            // #334 리뷰). 원래 파일은 그대로 남는다: 직렬화는 임시 파일을 쓰기 전에, 쓰기 실패는 바꿔 끼우기 전에 일어난다.
            Log($"save failed: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[SettingsService] {message}");
#else
        GC.KeepAlive(message);
#endif
    }
}
