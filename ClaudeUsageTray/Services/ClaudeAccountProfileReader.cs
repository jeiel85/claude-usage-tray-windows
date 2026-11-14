using System.IO;
using System.Text.Json;

namespace ClaudeUsageTray.Services;

/// <summary>
/// Claude 클라이언트 왼쪽 아래에 보이는 계정 이름·조직("김민준 · Acme Corp")을 읽는다(#218).
/// Claude Code 가 ~/.claude.json 의 <c>oauthAccount</c> 에 받아 둔 프로필이라 토큰도 네트워크 호출도 쓰지 않는다.
/// 공개 문서가 없는 내부 형식이므로 무엇이든 예상과 다르면 빈 문구로 끝낸다 — 그러면 등급 배지만 남는다.
/// </summary>
public sealed class ClaudeAccountProfileReader
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private const string Separator = " · ";

    private readonly string _path;
    private readonly object _gate = new();
    private DateTime _cachedWriteTimeUtc;
    private long _cachedLength = -1;
    private string _cachedLabel = "";
    private string? _cachedAccountUuid;

    /// <param name="path">프로필 파일 경로. null 이면 ~/.claude.json (테스트용 주입점).</param>
    public ClaudeAccountProfileReader(string? path = null) => _path = path ?? DefaultPath;

    /// <summary>
    /// 표시 문구("이름 · 조직"). 파일이 없거나 프로필이 없으면 "".
    /// 파일 수정 시각·크기가 그대로면 다시 파싱하지 않는다 — Claude Code 가 수시로 다시 쓰는 수십 KB 파일이다.
    /// 파일은 있는데 읽지 못했으면(쓰는 도중 등) 직전 문구를 유지하고 다음 호출에서 다시 읽는다.
    /// </summary>
    public string GetLabel()
    {
        lock (_gate)
        {
            RefreshLocked();
            return _cachedLabel;
        }
    }

    /// <summary>
    /// 이 PC 에 로그인한 Claude 계정의 accountUuid(휴대폰에서 같은 계정을 알아보는 데만 쓴다 — 해시로만 나간다, #220).
    /// 읽지 못하면 null. 파일 캐시는 <see cref="GetLabel"/> 과 공유한다.
    /// </summary>
    public string? GetAccountUuid()
    {
        lock (_gate)
        {
            RefreshLocked();
            return _cachedAccountUuid;
        }
    }

    private void RefreshLocked()
    {
        var file = new FileInfo(_path);
        if (!file.Exists)
        {
            _cachedLength = -1;
            _cachedLabel = "";
            _cachedAccountUuid = null;
            return;
        }

        if (file.LastWriteTimeUtc == _cachedWriteTimeUtc && file.Length == _cachedLength)
            return;

        if (!TryRead(out var label, out var accountUuid))
            return;

        _cachedWriteTimeUtc = file.LastWriteTimeUtc;
        _cachedLength = file.Length;
        _cachedLabel = label;
        _cachedAccountUuid = accountUuid;
    }

    private bool TryRead(out string label, out string? accountUuid)
    {
        label = "";
        accountUuid = null;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream, ParseOptions);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("oauthAccount", out var account)
                && account.ValueKind == JsonValueKind.Object)
            {
                accountUuid = ReadString(account, "accountUuid")?.Trim() is { Length: > 0 } uuid ? uuid : null;
                label = FormatLabel(
                    ReadString(account, "displayName") ?? ReadString(account, "fullName"),
                    ReadString(account, "organizationName"),
                    ReadString(account, "organizationType"));
            }
            return true;
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[ClaudeAccountProfileReader] Read failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
            return false;
        }
    }

    /// <summary>
    /// "이름 · 조직" 을 만든다. 조직 이름은 여러 사람이 함께 쓰는 조직(팀·엔터프라이즈)일 때만 붙인다 —
    /// 개인 구독의 조직은 "&lt;이메일&gt;'s Organization" 처럼 자동으로 이름이 붙어 이메일을 드러낼 수 있다.
    /// 같은 이유로 '@' 가 든 값(이름 대신 이메일이 들어 있는 경우 등)은 어느 쪽이든 쓰지 않는다.
    /// </summary>
    internal static string FormatLabel(string? name, string? organizationName, string? organizationType)
    {
        var shownName = Clean(name);
        var shownOrg = IsSharedOrganization(organizationType) ? Clean(organizationName) : null;
        return (shownName, shownOrg) switch
        {
            (not null, not null) => shownName + Separator + shownOrg,
            (not null, null) => shownName,
            (null, not null) => shownOrg,
            _ => "",
        };
    }

    internal static bool IsSharedOrganization(string? organizationType) =>
        organizationType is not null
        && (organizationType.Contains("team", StringComparison.OrdinalIgnoreCase)
            || organizationType.Contains("enterprise", StringComparison.OrdinalIgnoreCase));

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Contains('@')) return null;
        return trimmed;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
