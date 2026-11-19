namespace ClaudeUsageTray.Services;

/// <summary>
/// 계정 별명(#307). 개인·회사처럼 Claude 계정을 여럿 쓰면 계정 이름이 모두 본인 이름이라 휴대폰 계정 카드·알림에서
/// 어느 계정인지 구분이 안 된다. 별명은 계정마다 따로 저장한다 — 키는 휴대폰 계정 카드와 같은 계정 키
/// (Claude 는 <c>orgUuid|accountUuid</c>, <c>MainViewModel.BuildClaudeMobileAccountKey</c>). 같은 사람이라도
/// 개인 조직과 팀 조직은 accountUuid 가 같고 orgUuid 가 달라서, 조직까지 넣어야 두 계정이 갈린다.
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

    /// <summary>그 계정의 별명. 계정 키를 모르거나(로그아웃) 별명이 없으면 "".</summary>
    public static string Find(IReadOnlyDictionary<string, string>? aliases, string? accountKey) =>
        aliases is not null && !string.IsNullOrEmpty(accountKey) && aliases.TryGetValue(accountKey, out var alias)
            ? Clean(alias)
            : "";

    /// <summary>
    /// Input : 지금 사전, 계정 키, 새 별명
    /// Output: 그 계정의 별명만 바꾼 새 사전(빈 별명은 항목을 지운다). 계정 키가 없으면 정리만 한 사본.
    /// 핵심 로직: 갱신·알림 경로가 백그라운드 스레드에서 사전을 읽으므로 고쳐 쓰지 않고 새 사전으로 통째 바꾼다.
    /// </summary>
    public static Dictionary<string, string> With(IReadOnlyDictionary<string, string>? aliases, string? accountKey, string? alias)
    {
        var next = Normalize(aliases);
        if (string.IsNullOrEmpty(accountKey)) return next;
        var clean = Clean(alias);
        if (clean.Length == 0) next.Remove(accountKey);
        else next[accountKey] = clean;
        return next;
    }

    /// <summary>파일에서 읽은 사전을 정리한다 — 빈 키·빈 별명은 버리고 별명은 <see cref="Clean"/>. null 이면 빈 사전.</summary>
    public static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? aliases)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (aliases is null) return result;
        foreach (var (key, value) in aliases)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            var clean = Clean(value);
            if (clean.Length > 0) result[key] = clean;
        }
        return result;
    }

    /// <summary>보여 줄 계정 이름 — 별명이 있으면 별명, 없으면 계정 이름(없으면 "").</summary>
    public static string Display(string? alias, string? accountLabel)
    {
        var clean = Clean(alias);
        return clean.Length > 0 ? clean : accountLabel?.Trim() ?? "";
    }
}
