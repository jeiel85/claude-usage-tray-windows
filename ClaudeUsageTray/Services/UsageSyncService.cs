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
