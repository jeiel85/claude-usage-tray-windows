using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class UsageSyncService
{
    public const int DefaultApiSnapshotTtlMinutes = 5;
    public const int DefaultLocalSnapshotTtlHours = 24;

    private const string DeviceIdFileName = "usage-sync-device-id.txt";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _appDataDirectory;
    private readonly Func<DateTimeOffset> _now;
    private readonly string _deviceName;
    private readonly object _deviceIdLock = new();
    private string? _deviceId;

    public UsageSyncService()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageTray"),
            () => DateTimeOffset.Now,
            Environment.MachineName)
    {
    }

    public UsageSyncService(string appDataDirectory, Func<DateTimeOffset>? now = null, string? deviceName = null)
    {
        _appDataDirectory = appDataDirectory;
        _now = now ?? (() => DateTimeOffset.Now);
        _deviceName = string.IsNullOrWhiteSpace(deviceName) ? "unknown-device" : deviceName.Trim();
    }

    public string DeviceId => EnsureDeviceId();

    public bool IsConfigured(NotificationSettings settings) =>
        settings.UsageSyncEnabled && !string.IsNullOrWhiteSpace(settings.UsageSyncFolderPath);

    public UsageSyncSnapshot CreateSnapshot(
        string provider,
        string? accountKey,
        UsageSyncQuotaSnapshot? quota,
        UsageSyncLocalTotals localTotals,
        string? errorKind = null)
    {
        var now = _now();
        var normalizedProvider = NormalizeProvider(provider);
        if (quota is { ObservedAtUtc: null })
            quota.ObservedAtUtc = now.ToUniversalTime();

        return new UsageSyncSnapshot
        {
            SchemaVersion = UsageSyncSchema.CurrentVersion,
            AccountHash = BuildAccountHash(normalizedProvider, accountKey),
            Provider = normalizedProvider,
            DeviceId = DeviceId,
            DeviceName = _deviceName,
            LocalDate = DateOnly.FromDateTime(now.LocalDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ObservedAtUtc = now.ToUniversalTime(),
            Quota = quota,
            LocalTotals = NormalizeTotals(localTotals),
            ErrorKind = errorKind ?? "",
            Source = "local",
        };
    }

    public string WriteSnapshot(string syncRoot, UsageSyncSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRoot);
        ArgumentNullException.ThrowIfNull(snapshot);

        var directory = GetSnapshotDirectory(syncRoot, snapshot.AccountHash, snapshot.Provider, snapshot.LocalDate);
        Directory.CreateDirectory(directory);

        var destinationPath = Path.Combine(directory, $"{SanitizePathPart(snapshot.DeviceId)}.json");
        PreservePreviousQuotaIfNeeded(destinationPath, snapshot);
        var tempPath = Path.Combine(directory, $"{SanitizePathPart(snapshot.DeviceId)}.{Guid.NewGuid():N}.tmp");

        using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
        {
            JsonSerializer.Serialize(stream, snapshot, JsonOptions);
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        return destinationPath;
    }

    public UsageSyncReadResult ReadSnapshots(string syncRoot, string provider, string? accountKey, DateOnly localDate)
    {
        if (string.IsNullOrWhiteSpace(syncRoot))
            return new UsageSyncReadResult();

        var normalizedProvider = NormalizeProvider(provider);
        var accountHash = BuildAccountHash(normalizedProvider, accountKey);
        var dateText = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var directory = GetSnapshotDirectory(syncRoot, accountHash, normalizedProvider, dateText);

        if (!Directory.Exists(directory))
            return new UsageSyncReadResult();

        var snapshots = new List<UsageSyncSnapshot>();
        var diagnostics = new List<UsageSyncReadDiagnostic>();

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var snapshot = JsonSerializer.Deserialize<UsageSyncSnapshot>(stream, JsonOptions);
                if (!IsValidSnapshot(snapshot, accountHash, normalizedProvider, dateText))
                {
                    diagnostics.Add(new UsageSyncReadDiagnostic(path, "invalid-contract"));
                    continue;
                }

                snapshot!.LocalTotals = NormalizeTotals(snapshot.LocalTotals);
                if (snapshot.Quota is { } quota)
                    quota.Models ??= [];
                snapshots.Add(snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                diagnostics.Add(new UsageSyncReadDiagnostic(path, ex.GetType().Name));
            }
        }

        return new UsageSyncReadResult
        {
            Snapshots = snapshots,
            Diagnostics = diagnostics,
        };
    }

    public UsageSyncSnapshot? SelectNewestQuotaSnapshot(IEnumerable<UsageSyncSnapshot> snapshots, TimeSpan ttl)
    {
        var nowUtc = _now().ToUniversalTime();
        return snapshots
            .Where(snapshot => snapshot.Quota is { HasData: true })
            .Where(snapshot => IsFresh(snapshot.Quota!.ObservedAtUtc ?? snapshot.ObservedAtUtc, ttl, nowUtc))
            .OrderByDescending(snapshot => snapshot.Quota!.ObservedAtUtc ?? snapshot.ObservedAtUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// 할당량 후보를 한 번에 고른다 — 신선도 기준(<paramref name="freshTtl"/>) 안의 최신 값과,
    /// 신선도를 무시한 최근 <see cref="LastResortMaxAge"/> 안의 최신 관측, 그리고 그 관측 중 창이
    /// 아직 유효해 최후 폴백으로 써도 되는 것. 모든 provider 가 이 한 곳에서 같은 규칙으로 고르므로,
    /// 호출부가 신선도용·최후폴백용 조회를 따로 짝지을 필요가 없다(한쪽만 베껴 쓰면 두 PC 가 동시에
    /// 조회 실패·백오프에 걸렸을 때 공백이 생기는 버그가 재발한다 — #156·#159).
    ///
    /// 공유 폴더는 날짜별로 나뉘어 있어 자정 직후에는 오늘 폴더에 할당량이 아직 없다. 그때만 어제
    /// 폴더를 추가로 읽어, "24시간 이내" 관측을 날짜 경계에서 놓치지 않게 한다. 오늘 폴더에 관측이
    /// 하나라도 있으면 그것이 어제 것보다 항상 새롭기 때문에 어제 폴더는 읽지 않는다.
    ///
    /// 전제: 신선도(<c>ObservedAtUtc</c>) 판정은 다른 기기가 기록한 시각을 이 기기의 시계와 그대로
    /// 비교한다. 기기 간 시계가 크게 어긋나면 판정이 틀릴 수 있다(미래 시각은 5분까지만 허용).
    /// 최후 폴백의 창 유효성은 서버가 내려준 리셋 시각으로 판정하므로 이 전제에 덜 의존한다.
    /// </summary>
    public UsageSyncQuotaCandidates SelectQuotaCandidates(
        string syncRoot,
        string provider,
        string? accountKey,
        DateOnly today,
        IReadOnlyList<UsageSyncSnapshot> todaySnapshots,
        TimeSpan freshTtl)
    {
        IReadOnlyList<UsageSyncSnapshot> pool = todaySnapshots;
        if (!todaySnapshots.Any(static snapshot => snapshot.Quota is { HasData: true }))
        {
            var yesterday = ReadSnapshots(syncRoot, provider, accountKey, today.AddDays(-1));
            if (yesterday.Snapshots.Count > 0)
                pool = [.. todaySnapshots, .. yesterday.Snapshots];
        }

        // 신선한 값도 창 검사를 거친다 — 몇 분 전 관측이라도 그 사이 리셋을 지났다면(13:58 관측,
        // 14:00 리셋, 14:01 조회) 리셋 전 %를 그대로 보여주게 된다.
        var now = _now();
        var fresh = SelectNewestQuotaSnapshot(pool, freshTtl);
        if (!IsQuotaWindowStillActive(fresh, now))
            fresh = null;
        var lastObserved = SelectNewestQuotaSnapshot(pool, LastResortMaxAge);
        var lastResort = IsQuotaWindowStillActive(lastObserved, now) ? lastObserved : null;
        return new UsageSyncQuotaCandidates(fresh, lastObserved, lastResort);
    }

    /// <summary>
    /// 최후 폴백 후보로 볼 관측의 최대 나이. 신선도 기준(최대 60분)을 넘긴 값이라도 이 안이면
    /// 창이 아직 유효한지 따져 볼 가치가 있다 — 사용량 %는 창이 끝날 때까지 증가만 하므로
    /// 오래된 값도 "적어도 이만큼은 썼다"는 유효한 하한이다.
    /// </summary>
    public static readonly TimeSpan LastResortMaxAge = TimeSpan.FromDays(1);

    /// <summary>
    /// 스냅샷이 담은 할당량 창이 지금도 유효한지 — 하나라도 리셋이 지난 창이 있다면 그 %는
    /// 실제로는 0%대로 돌아갔을 값이라, 오래된 스냅샷을 최후 폴백으로 쓰면 부풀려 보인다.
    /// provider 마다 창을 담는 모양이 달라 각각 확인한다.
    /// <list type="bullet">
    /// <item>Claude·Codex: <c>ShortResetAt</c>/<c>LongResetAt</c> 중 있는 것만 검사한다(Codex 주간
    /// 전용 플랜은 Short 가 비어 있다). 둘 다 모르는 구버전 스냅샷은 판단 근거가 없어 허용한다.</item>
    /// <item>OpenCode: 롤링·주간·월간 세 창이 서로 독립적으로 리셋되므로 하나라도 지났으면 거부한다.</item>
    /// <item>Antigravity: 모델별 리셋만 있다. 화면(ApplyQuota)이 리셋을 모르거나 지난 모델을 걸러내므로,
    /// 아직 보여줄 모델이 하나도 없으면 거부한다.</item>
    /// </list>
    /// </summary>
    public static bool IsQuotaWindowStillActive(UsageSyncSnapshot? candidate, DateTimeOffset now)
    {
        if (candidate?.Quota is not { HasData: true } quota)
            return false;
        if (quota.ShortResetAt is { } shortResetAt && shortResetAt <= now)
            return false;
        if (quota.LongResetAt is { } longResetAt && longResetAt <= now)
            return false;
        if (quota.OpenCode is { } openCode &&
            (openCode.Rolling.ResetAt <= now || openCode.Weekly.ResetAt <= now || openCode.Monthly.ResetAt <= now))
            return false;
        if (quota.Models is { Length: > 0 } models &&
            !models.Any(model => model.ResetAt is { } resetAt && resetAt > now))
            return false;
        return true;
    }

    public UsageSyncMergedLocalTotals MergeLocalTotals(IEnumerable<UsageSyncSnapshot> snapshots, TimeSpan ttl)
    {
        var nowUtc = _now().ToUniversalTime();
        var latestPerDevice = snapshots
            .Where(snapshot => IsFresh(snapshot, ttl, nowUtc))
            .Where(snapshot => snapshot.LocalTotals.HasData)
            .GroupBy(snapshot => snapshot.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(snapshot => snapshot.ObservedAtUtc).First())
            .ToList();

        var merged = new UsageSyncMergedLocalTotals
        {
            DeviceCount = latestPerDevice.Count,
            LatestObservedAtUtc = latestPerDevice.Count == 0
                ? null
                : latestPerDevice.Max(snapshot => snapshot.ObservedAtUtc),
            HourlyTokens = new long[24],
        };

        foreach (var snapshot in latestPerDevice)
        {
            var totals = snapshot.LocalTotals;
            merged.InputTokens += totals.InputTokens;
            merged.OutputTokens += totals.OutputTokens;
            merged.CacheReadTokens += totals.CacheReadTokens;
            merged.CacheWriteTokens += totals.CacheWriteTokens;
            merged.SessionCount += totals.SessionCount;
            merged.RequestCount += totals.RequestCount;

            for (var i = 0; i < merged.HourlyTokens.Length && i < totals.HourlyTokens.Length; i++)
            {
                merged.HourlyTokens[i] += totals.HourlyTokens[i];
            }
        }

        return merged;
    }

    public static string BuildAccountHash(string provider, string? accountKey)
    {
        var material = $"{NormalizeProvider(provider)}:{(string.IsNullOrWhiteSpace(accountKey) ? "default" : accountKey.Trim())}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GetSnapshotDirectory(string syncRoot, string accountHash, string provider, string localDate) =>
        Path.Combine(syncRoot, SanitizePathPart(accountHash), SanitizePathPart(provider), SanitizePathPart(localDate));

    private static bool IsValidSnapshot(UsageSyncSnapshot? snapshot, string accountHash, string provider, string dateText) =>
        snapshot is not null &&
        snapshot.SchemaVersion == UsageSyncSchema.CurrentVersion &&
        string.Equals(snapshot.AccountHash, accountHash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(snapshot.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(snapshot.LocalDate, dateText, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(snapshot.DeviceId);

    private static bool IsFresh(UsageSyncSnapshot snapshot, TimeSpan ttl, DateTimeOffset nowUtc)
    {
        return IsFresh(snapshot.ObservedAtUtc, ttl, nowUtc);
    }

    private static bool IsFresh(DateTimeOffset observedAt, TimeSpan ttl, DateTimeOffset nowUtc)
    {
        var observed = observedAt.ToUniversalTime();
        return observed >= nowUtc - ttl && observed <= nowUtc.AddMinutes(5);
    }

    private static void PreservePreviousQuotaIfNeeded(string destinationPath, UsageSyncSnapshot snapshot)
    {
        if (snapshot.Quota is { HasData: true } || !File.Exists(destinationPath))
            return;

        try
        {
            using var stream = new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var previous = JsonSerializer.Deserialize<UsageSyncSnapshot>(stream, JsonOptions);
            if (previous?.Quota is { HasData: true } previousQuota &&
                string.Equals(previous.AccountHash, snapshot.AccountHash, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(previous.Provider, snapshot.Provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(previous.LocalDate, snapshot.LocalDate, StringComparison.Ordinal) &&
                string.Equals(previous.DeviceId, snapshot.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.Quota = previousQuota;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            GC.KeepAlive(ex);
        }
    }

    private static UsageSyncLocalTotals NormalizeTotals(UsageSyncLocalTotals? totals)
    {
        totals ??= new UsageSyncLocalTotals();
        var hourly = new long[24];
        if (totals.HourlyTokens is not null)
        {
            for (var i = 0; i < hourly.Length && i < totals.HourlyTokens.Length; i++)
            {
                hourly[i] = totals.HourlyTokens[i];
            }
        }

        totals.HourlyTokens = hourly;
        return totals;
    }

    private string EnsureDeviceId()
    {
        lock (_deviceIdLock)
        {
            if (!string.IsNullOrWhiteSpace(_deviceId))
                return _deviceId;

            Directory.CreateDirectory(_appDataDirectory);
            var path = Path.Combine(_appDataDirectory, DeviceIdFileName);
            if (File.Exists(path))
            {
                using var readStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(readStream, Encoding.UTF8);
                var existing = reader.ReadToEnd().Trim();
                if (IsValidDeviceId(existing))
                {
                    _deviceId = existing;
                    return _deviceId;
                }
            }

            _deviceId = Guid.NewGuid().ToString("N");
            using var writeStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(writeStream, Encoding.UTF8);
            writer.Write(_deviceId);
            return _deviceId;
        }
    }

    private static bool IsValidDeviceId(string value) =>
        value.Length == 32 && value.All(static c => Uri.IsHexDigit(c));

    private static string NormalizeProvider(string provider) =>
        SanitizePathPart(string.IsNullOrWhiteSpace(provider) ? UsageProviderKind.Claude : provider.Trim().ToLowerInvariant());

    private static string SanitizePathPart(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }
}
