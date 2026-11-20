using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 별명 하나와 바꾼 시각(UTC, #313). <c>Alias</c> 가 "" 이면 지웠다는 기록이다 — 지운 것도 다른 PC 에 전해야
/// 그 PC 가 옛 별명을 계속 쓰지 않는다. v1.70.0 에서 넣은 별명은 시각이 없어 <see cref="DateTimeOffset.MinValue"/> 로 읽힌다.
/// </summary>
public sealed record AccountAliasEntry(string Alias, DateTimeOffset UpdatedAtUtc);

/// <summary>
/// 계정 별명(#307). 개인·회사처럼 Claude 계정을 여럿 쓰면 계정 이름이 모두 본인 이름이라 휴대폰 계정 카드·알림에서
/// 어느 계정인지 구분이 안 된다. 별명은 계정마다 따로 저장한다 — 키는 휴대폰 계정 카드와 같은 계정 키
/// (Claude 는 <c>orgUuid|accountUuid</c>, <c>MainViewModel.BuildClaudeMobileAccountKey</c>). 같은 사람이라도
/// 개인 조직과 팀 조직은 accountUuid 가 같고 orgUuid 가 달라서, 조직까지 넣어야 두 계정이 갈린다.
/// PC 간 동기화(#313)로 같은 계정을 보는 PC 끼리 더 최근에 바꾼 별명을 따른다(<see cref="Wins"/>).
/// </summary>
public static class AccountAlias
{
    /// <summary>
    /// 별명 최대 길이. 휴대폰 알림 제목 맨 앞 "[별명]" 에 들어가므로 PC 이름(32자)보다 짧게 둔다 —
    /// 길면 접힌 알림에서 뒤의 서비스·퍼센트가 밀려 잘린다.
    /// </summary>
    public const int MaxLength = 20;

    /// <summary>한 줄·최대 길이로 정리한다(PC 이름과 같은 규칙: 제어 문자·연속 공백 정리, 서로게이트 쌍 보존).</summary>
    public static string Clean(string? alias) => PcName.Clean(alias, MaxLength);

    /// <summary>그 계정의 별명. 계정 키를 모르거나(로그아웃) 별명이 없거나 지웠으면 "".</summary>
    public static string Find(IReadOnlyDictionary<string, AccountAliasEntry>? entries, string? accountKey) =>
        Entry(entries, accountKey)?.Alias ?? "";

    /// <summary>그 계정의 기록(지운 기록 포함). 없으면 null.</summary>
    public static AccountAliasEntry? Entry(IReadOnlyDictionary<string, AccountAliasEntry>? entries, string? accountKey) =>
        entries is not null && !string.IsNullOrEmpty(accountKey) && entries.TryGetValue(accountKey, out var entry) ? entry : null;

    /// <summary>
    /// Input : 지금 기록, 계정 키, 이 PC 에서 새로 넣은 별명, 지금 시각
    /// Output: 그 계정의 기록만 바꾼 새 사전. 빈 별명은 "지웠다" 기록으로 남는다(없던 별명을 지우는 것은 남길 것이 없다).
    ///         계정 키가 없으면 그대로인 사본.
    /// 핵심 로직: 갱신·알림·동기화 경로가 백그라운드 스레드에서 사전을 읽으므로 고쳐 쓰지 않고 새 사전으로 통째 바꾼다.
    /// </summary>
    public static Dictionary<string, AccountAliasEntry> With(
        IReadOnlyDictionary<string, AccountAliasEntry>? entries, string? accountKey, string? alias, DateTimeOffset now)
    {
        var next = Copy(entries);
        if (string.IsNullOrEmpty(accountKey)) return next;
        var clean = Clean(alias);
        if (clean.Length == 0 && !next.ContainsKey(accountKey)) return next;
        next[accountKey] = new AccountAliasEntry(clean, now.ToUniversalTime());
        return next;
    }

    /// <summary>다른 PC 에서 받은 기록으로 그 계정만 바꾼 새 사전(<see cref="Wins"/> 를 통과한 기록만 넘긴다).</summary>
    public static Dictionary<string, AccountAliasEntry> Adopt(
        IReadOnlyDictionary<string, AccountAliasEntry>? entries, string accountKey, AccountAliasEntry incoming)
    {
        var next = Copy(entries);
        next[accountKey] = incoming with { Alias = Clean(incoming.Alias) };
        return next;
    }

    /// <summary>
    /// 다른 PC 의 기록(<paramref name="incoming"/>)이 이 PC 의 기록(<paramref name="local"/>)을 이기는가(#313).
    /// 더 최근에 바꾼 쪽이 이긴다. 같은 시각(예: 둘 다 시각이 없던 v1.70.0 별명)이면 문자열 순서로 정한다 —
    /// 어느 PC 에서 판단해도 같은 쪽이 이겨야 모든 PC 가 한 값으로 모인다. 이 PC 에 기록이 없으면 별명이 있을 때만 따른다.
    /// </summary>
    public static bool Wins(AccountAliasEntry incoming, AccountAliasEntry? local)
    {
        if (local is null) return Clean(incoming.Alias).Length > 0;
        if (incoming.UpdatedAtUtc != local.UpdatedAtUtc) return incoming.UpdatedAtUtc > local.UpdatedAtUtc;
        return string.CompareOrdinal(Clean(incoming.Alias), local.Alias) > 0;
    }

    /// <summary>
    /// 동기화 스냅샷 중 이 계정(별명 키 해시)에 대해 다른 PC 들이 보낸 기록 가운데 가장 이기는 것. 없으면 null.
    /// 스냅샷의 계정 해시는 조직 단위라 팀 조직에서는 다른 사람의 스냅샷도 같이 오므로 별명 키 해시로 다시 거른다.
    /// </summary>
    public static AccountAliasEntry? PickSynced(IEnumerable<UsageSyncSnapshot> snapshots, string keyHash, string ownDeviceId)
    {
        AccountAliasEntry? best = null;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.AccountAlias is not { } synced
                || !string.Equals(synced.KeyHash, keyHash, StringComparison.OrdinalIgnoreCase)
                || string.Equals(snapshot.DeviceId, ownDeviceId, StringComparison.OrdinalIgnoreCase))
                continue;
            var entry = new AccountAliasEntry(Clean(synced.Alias), synced.UpdatedAtUtc.ToUniversalTime());
            if (best is null || Wins(entry, best)) best = entry;
        }
        return best;
    }

    /// <summary>
    /// 설정 파일의 두 사전(별명, 바꾼 시각)을 기록으로 읽는다. 시각이 없는 별명(v1.70.0)은 가장 옛 시각,
    /// 별명 없이 시각만 있는 키는 "지웠다" 기록. 빈 키는 버린다.
    /// </summary>
    public static Dictionary<string, AccountAliasEntry> FromSettings(
        IReadOnlyDictionary<string, string>? aliases, IReadOnlyDictionary<string, DateTimeOffset>? updatedAt)
    {
        var result = new Dictionary<string, AccountAliasEntry>(StringComparer.Ordinal);
        foreach (var (key, value) in aliases ?? new Dictionary<string, string>())
        {
            var clean = Clean(value);
            if (string.IsNullOrWhiteSpace(key) || clean.Length == 0) continue;
            var time = updatedAt is not null && updatedAt.TryGetValue(key, out var t) ? t.ToUniversalTime() : DateTimeOffset.MinValue;
            result[key] = new AccountAliasEntry(clean, time);
        }
        foreach (var (key, time) in updatedAt ?? new Dictionary<string, DateTimeOffset>())
        {
            if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key))
                result[key] = new AccountAliasEntry("", time.ToUniversalTime());
        }
        return result;
    }

    /// <summary>
    /// 설정 파일에 쓸 두 사전. 별명 사전은 v1.70.0 과 같은 모양(별명이 있는 계정만)이라 되돌려 설치해도 별명이 남고,
    /// 시각 사전에는 시각을 아는 기록(지운 기록 포함)만 쓴다.
    /// </summary>
    public static (Dictionary<string, string> Aliases, Dictionary<string, DateTimeOffset> UpdatedAt) ToSettings(
        IReadOnlyDictionary<string, AccountAliasEntry>? entries)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var updatedAt = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var (key, entry) in entries ?? new Dictionary<string, AccountAliasEntry>())
        {
            if (entry.Alias.Length > 0) aliases[key] = entry.Alias;
            if (entry.UpdatedAtUtc > DateTimeOffset.MinValue) updatedAt[key] = entry.UpdatedAtUtc;
        }
        return (aliases, updatedAt);
    }

    /// <summary>보여 줄 계정 이름 — 별명이 있으면 별명, 없으면 계정 이름(없으면 "").</summary>
    public static string Display(string? alias, string? accountLabel)
    {
        var clean = Clean(alias);
        return clean.Length > 0 ? clean : accountLabel?.Trim() ?? "";
    }

    private static Dictionary<string, AccountAliasEntry> Copy(IReadOnlyDictionary<string, AccountAliasEntry>? entries) =>
        entries is null
            ? new Dictionary<string, AccountAliasEntry>(StringComparer.Ordinal)
            : new Dictionary<string, AccountAliasEntry>(entries, StringComparer.Ordinal);
}
