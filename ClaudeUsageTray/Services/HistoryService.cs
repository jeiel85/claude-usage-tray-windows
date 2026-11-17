using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeUsageTray.Services;

public record DailyStats(
    string Date,           // yyyy-MM-dd
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    int  SessionCount);

/// <summary>
/// Provider/account 별로 일별 사용량 history를 관리합니다.
/// 활성 scope(active scope)는 차트 바인딩이 보는 곳이고, 모든 scope는 별도 파일로 영속화됩니다.
/// 4개 provider가 병렬 refresh 시에도 각자 자기 scope에 안전하게 기록할 수 있도록 multi-scope 구조.
/// </summary>
public class HistoryService
{
    private static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    private readonly string _directory;

    // scopeKey ("provider|accountKey") -> (dateKey -> stats)
    private readonly Dictionary<string, Dictionary<string, DailyStats>> _scopes = new();
    private readonly object _scopesLock = new();

    private string _activeScopeKey;

    /// <param name="directory">history 파일 폴더. null 이면 ~/.claude (테스트용 주입점).</param>
    public HistoryService(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory;
        _activeScopeKey = MakeKey(Models.UsageProviderKind.Claude, null);
        EnsureLoaded(_activeScopeKey);
    }

    // =====================================================================
    // 활성 scope (차트 바인딩용 — 기존 API 그대로)
    // =====================================================================

    /// <summary>
    /// 차트가 어떤 provider/account의 history를 표시할지 결정. provider별 history 기록(RecordToday)에는 영향 없음.
    /// </summary>
    public void SetScope(string providerKey, string? accountKey)
    {
        var key = MakeKey(providerKey, accountKey);
        lock (_scopesLock)
        {
            _activeScopeKey = key;
            EnsureLoadedNoLock(key);
        }
    }

    /// <summary>활성 scope에 오늘 데이터 기록.</summary>
    public void RecordToday(long input, long output, long cacheRead, long cacheWrite, int sessions)
    {
        RecordTodayCore(_activeScopeKey, input, output, cacheRead, cacheWrite, sessions);
    }

    /// <summary>활성 scope의 최근 N일 데이터 (차트 바인딩이 사용).</summary>
    public IReadOnlyList<DailyStats> GetLast(int days) => GetLastCore(_activeScopeKey, days);

    /// <summary>활성 scope의 최근 N일 입력+출력 토큰 합계의 일별 최댓값.</summary>
    public long GetRecentMaxTotalTokens(int days) => GetRecentMaxTotalTokensCore(_activeScopeKey, days);

    // =====================================================================
    // Scope-explicit 오버로드 (provider별 기록/조회용)
    // =====================================================================

    /// <summary>지정 provider/account scope에 오늘 데이터 기록 (활성 scope 변경 안 함).</summary>
    public void RecordToday(string providerKey, string? accountKey,
                            long input, long output, long cacheRead, long cacheWrite, int sessions)
    {
        var key = MakeKey(providerKey, accountKey);
        RecordTodayCore(key, input, output, cacheRead, cacheWrite, sessions);
    }

    /// <summary>지정 scope의 최근 N일 데이터.</summary>
    public IReadOnlyList<DailyStats> GetLast(string providerKey, string? accountKey, int days)
    {
        var key = MakeKey(providerKey, accountKey);
        return GetLastCore(key, days);
    }

    /// <summary>지정 scope의 최근 N일 입력+출력 토큰 합계의 일별 최댓값.</summary>
    public long GetRecentMaxTotalTokens(string providerKey, string? accountKey, int days)
    {
        var key = MakeKey(providerKey, accountKey);
        return GetRecentMaxTotalTokensCore(key, days);
    }

    // =====================================================================
    // 핵심 동작 (private)
    // =====================================================================

    private void RecordTodayCore(string scopeKey,
                                 long input, long output, long cacheRead, long cacheWrite, int sessions)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        lock (_scopesLock)
        {
            EnsureLoadedNoLock(scopeKey);
            _scopes[scopeKey][today] = new DailyStats(today, input, output, cacheRead, cacheWrite, sessions);
            TrimOldEntries(_scopes[scopeKey]);
            Save(scopeKey);
        }
    }

    private IReadOnlyList<DailyStats> GetLastCore(string scopeKey, int days)
    {
        var cutoff = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd");
        lock (_scopesLock)
        {
            EnsureLoadedNoLock(scopeKey);
            return _scopes[scopeKey].Values
                .Where(s => string.Compare(s.Date, cutoff, StringComparison.Ordinal) >= 0)
                .OrderByDescending(s => s.Date)
                .Take(days)
                .OrderBy(s => s.Date)
                .ToList();
        }
    }

    private long GetRecentMaxTotalTokensCore(string scopeKey, int days)
    {
        var cutoff = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd");
        lock (_scopesLock)
        {
            EnsureLoadedNoLock(scopeKey);
            var recentEntries = _scopes[scopeKey].Values
                .Where(s => string.Compare(s.Date, cutoff, StringComparison.Ordinal) >= 0)
                .OrderByDescending(s => s.Date)
                .Take(days)
                .ToList();
            if (recentEntries.Count == 0) return 0;
            return recentEntries.Max(s => s.InputTokens + s.OutputTokens);
        }
    }

    /// <summary>활성 scope의 데이터를 CSV로 export (호환성 유지).</summary>
    public void ExportCsv(string filePath)
    {
        lock (_scopesLock)
        {
            EnsureLoadedNoLock(_activeScopeKey);
            var sb = new StringBuilder();
            sb.AppendLine("날짜,입력 토큰,출력 토큰,캐시 읽기,캐시 쓰기,세션 수");
            foreach (var s in _scopes[_activeScopeKey].Values.OrderBy(s => s.Date))
                sb.AppendLine($"{s.Date},{s.InputTokens},{s.OutputTokens},{s.CacheReadTokens},{s.CacheWriteTokens},{s.SessionCount}");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
    }

    // =====================================================================
    // I/O helpers
    // =====================================================================

    private static string MakeKey(string providerKey, string? accountKey)
    {
        var p = string.IsNullOrWhiteSpace(providerKey)
            ? Models.UsageProviderKind.Claude
            : providerKey.Trim().ToLowerInvariant();
        return $"{p}|{accountKey ?? ""}";
    }

    private static (string provider, string? account) ParseKey(string scopeKey)
    {
        var parts = scopeKey.Split('|', 2);
        var provider = parts[0];
        var account = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        return (provider, account);
    }

    private string ScopeKeyToPath(string scopeKey)
    {
        var (provider, accountKey) = ParseKey(scopeKey);
        var suffix = string.IsNullOrEmpty(accountKey)
            ? ""
            : $"-{accountKey![..Math.Min(8, accountKey.Length)]}";
        return Path.Combine(_directory, $"claude-usage-tray-history-{provider}{suffix}.json");
    }

    private void EnsureLoaded(string scopeKey)
    {
        lock (_scopesLock) EnsureLoadedNoLock(scopeKey);
    }

    private void EnsureLoadedNoLock(string scopeKey)
    {
        if (_scopes.ContainsKey(scopeKey)) return;
        var path = ScopeKeyToPath(scopeKey);
        var data = new Dictionary<string, DailyStats>();
        var adopted = false;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                data = JsonSerializer.Deserialize<Dictionary<string, DailyStats>>(json)
                       ?? new Dictionary<string, DailyStats>();
            }
            else if (TryAdoptUnscopedNoLock(scopeKey) is { } inherited)
            {
                data = inherited;
                adopted = true;
            }
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[HistoryService] Load failed for {scopeKey}: {ex.Message}");
#endif
            GC.KeepAlive(ex);
        }
        _scopes[scopeKey] = data;
        if (adopted) Save(scopeKey);
    }

    /// <summary>
    /// Input : 아직 파일이 없는 계정 scope
    /// Output: 이어 받을 기록(계정 없는 scope 의 복사본). 이어 받지 않으면 null
    /// 핵심 로직: 계정을 알아내지 못하던 동안(#275 — 최근 Claude Code 가 자격 파일에 orgUuid 를 쓰지 않음)의 기록은 계정 없는
    ///           scope 에 쌓였다. 계정을 알게 되는 순간 빈 scope 로 바뀌면 차트와 자동 목표(최근 최대치)가 초기화된 것처럼 보이므로,
    ///           이 PC 에 계정 scope 파일이 하나도 없을 때만 — 즉 이 PC 가 처음 알게 된 계정일 때만 — 그 기록을 복사해 이어 간다.
    ///           이미 계정 scope 가 있는 PC 에서 다른 계정으로 바꾼 경우에는 남의 기록이 될 수 있어 이어 받지 않는다.
    ///           원본은 지우지 않는다(로그아웃하면 다시 그 scope 를 본다).
    /// </summary>
    private Dictionary<string, DailyStats>? TryAdoptUnscopedNoLock(string scopeKey)
    {
        var (provider, accountKey) = ParseKey(scopeKey);
        if (accountKey is null) return null;
        if (Directory.Exists(_directory)
            && Directory.EnumerateFiles(_directory, $"claude-usage-tray-history-{provider}-*.json").Any())
            return null;

        var unscopedKey = MakeKey(provider, null);
        EnsureLoadedNoLock(unscopedKey);
        var source = _scopes[unscopedKey];
        return source.Count == 0 ? null : new Dictionary<string, DailyStats>(source);
    }

    private void Save(string scopeKey)
    {
        try
        {
            var path = ScopeKeyToPath(scopeKey);
            File.WriteAllText(path,
                JsonSerializer.Serialize(_scopes[scopeKey],
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[HistoryService] Save failed for {scopeKey}: {ex.Message}");
#endif
            GC.KeepAlive(ex);
        }
    }

    private static void TrimOldEntries(Dictionary<string, DailyStats> data)
    {
        var cutoff = DateTime.Now.AddDays(-AppConstants.HistoryRetentionDays).ToString("yyyy-MM-dd");
        foreach (var key in data.Keys.Where(k => string.Compare(k, cutoff, StringComparison.Ordinal) < 0).ToList())
            data.Remove(key);
    }
}
