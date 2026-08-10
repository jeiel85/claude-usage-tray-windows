using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;
using Microsoft.Data.Sqlite;

namespace ClaudeUsageTray.Services;

public class OpenCodeUsageMonitor
{
    private readonly string _dbPath;

    public OpenCodeUsageMonitor()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "opencode", "opencode.db"))
    {
    }

    public OpenCodeUsageMonitor(string dbPath)
    {
        _dbPath = dbPath;
    }

    public ProviderUsageSnapshot GetTodaySnapshot()
        => GetSnapshot(DateTimeOffset.Now);

    internal ProviderUsageSnapshot GetSnapshot(DateTimeOffset now)
    {
        var snapshot = new ProviderUsageSnapshot();

        if (!File.Exists(_dbPath))
        {
            snapshot.ErrorMessage = Loc.OpenCodeDbNotFound;
            return snapshot;
        }

        try
        {
            var localNow = now.LocalDateTime;
            var today = localNow.Date;
            var todayMs = new DateTimeOffset(today).ToUnixTimeMilliseconds();
            var tomorrowMs = new DateTimeOffset(today.AddDays(1)).ToUnixTimeMilliseconds();
            var fiveHoursMs = now.AddHours(-5).ToUnixTimeMilliseconds();
            var sevenDaysMs = now.AddDays(-7).ToUnixTimeMilliseconds();
            var monthStartMs = new DateTimeOffset(new DateTime(localNow.Year, localNow.Month, 1)).ToUnixTimeMilliseconds();
            var scanStartMs = Math.Min(sevenDaysMs, monthStartMs);

            long totalInput = 0, totalOutput = 0, totalCacheRead = 0, totalCacheWrite = 0;
            int requestCount = 0;
            var hourlyTokens = new long[24];
            var details = new OpenCodeUsageDetails();
            long latestLimitMs = 0;

            var connStr = $"Data Source={_dbPath};Mode=ReadOnly;Cache=Shared";
            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT data, json_extract(data, '$.time.created') AS created_ms
                FROM message
                WHERE json_extract(data, '$.role') = 'assistant'
                  AND json_extract(data, '$.time.created') >= @scanStartMs
                  AND json_extract(data, '$.time.created') < @tomorrowMs
            ";
            cmd.Parameters.AddWithValue("@scanStartMs", scanStartMs);
            cmd.Parameters.AddWithValue("@tomorrowMs", tomorrowMs);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var dataJson  = reader.GetString(0);
                var createdMs = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);

                try
                {
                    using var doc  = JsonDocument.Parse(dataJson);
                    var root = doc.RootElement;

                    DetectLimit(root, createdMs, ref latestLimitMs, details);
                    if (!root.TryGetProperty("tokens", out var tokensEl)) continue;

                    long inp = 0, out_ = 0, cacheRead = 0, cacheWrite = 0;
                    if (tokensEl.TryGetProperty("input",  out var inpEl))  inpEl.TryGetInt64(out inp);
                    if (tokensEl.TryGetProperty("output", out var outEl))  outEl.TryGetInt64(out out_);
                    if (tokensEl.TryGetProperty("cache",  out var cacheEl))
                    {
                        if (cacheEl.TryGetProperty("read",  out var crEl)) crEl.TryGetInt64(out cacheRead);
                        if (cacheEl.TryGetProperty("write", out var cwEl)) cwEl.TryGetInt64(out cacheWrite);
                    }

                    var totalTokens = inp + out_ + cacheRead + cacheWrite;
                    if (totalTokens == 0) continue;

                    decimal cost = 0;
                    if (root.TryGetProperty("cost", out var costEl) && costEl.TryGetDecimal(out var parsedCost))
                        cost = parsedCost;

                    AddPeriod(details.LastSevenDays, createdMs >= sevenDaysMs, totalTokens, cost);
                    AddPeriod(details.ThisMonth, createdMs >= monthStartMs, totalTokens, cost);
                    AddPeriod(details.LastFiveHours, createdMs >= fiveHoursMs, totalTokens, cost);

                    if (createdMs < todayMs) continue;

                    totalInput      += inp;
                    totalOutput     += out_;
                    totalCacheRead  += cacheRead;
                    totalCacheWrite += cacheWrite;
                    requestCount++;

                    if (createdMs > 0)
                    {
                        var hour = DateTimeOffset.FromUnixTimeMilliseconds(createdMs).LocalDateTime.Hour;
                        hourlyTokens[hour] += out_;
                    }
                }
                catch { /* ignore malformed rows */ }
            }

            snapshot.TotalInputTokens      = totalInput;
            snapshot.TotalOutputTokens     = totalOutput;
            snapshot.TotalCacheReadTokens  = totalCacheRead;
            snapshot.TotalCacheWriteTokens = totalCacheWrite;
            snapshot.RequestCount = requestCount;
            snapshot.HourlyTokens = hourlyTokens;
            if (details.RetryAt is not { } retryAt || retryAt <= now)
            {
                details.LimitKind = null;
                details.RetryAt = null;
            }
            snapshot.OpenCodeDetails = details;
            snapshot.HasData      = requestCount > 0;
            snapshot.ErrorMessage = requestCount == 0 ? Loc.OpenCodeNoUsageToday : null;
            
            // OpenCode free/Zen limits are not exposed in the local database.
            // Never turn a recent local maximum into a provider quota.
            snapshot.ShortUsagePercent = 0;
            snapshot.IsLimited = details.LimitKind != null;
            snapshot.PlanType = "Local";
        }
        catch (Exception ex)
        {
            snapshot.ErrorMessage = ex.Message;
        }

        return snapshot;
    }

    private static void AddPeriod(OpenCodePeriodUsage period, bool include, long tokens, decimal cost)
    {
        if (!include) return;
        period.Tokens += tokens;
        period.Requests++;
        period.CostUsd += cost;
    }

    private static void DetectLimit(JsonElement root, long createdMs, ref long latestLimitMs, OpenCodeUsageDetails details)
    {
        if (!root.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object
            || createdMs < latestLimitMs) return;
        var raw = error.GetRawText();
        string? kind = raw.Contains("GoUsageLimitError", StringComparison.Ordinal) ? "go"
            : raw.Contains("FreeUsageLimitError", StringComparison.Ordinal) ? "free"
            : null;
        if (kind == null) return;

        latestLimitMs = createdMs;
        details.LimitKind = kind;
        if (error.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("responseHeaders", out var headers)
            && headers.ValueKind == JsonValueKind.Object
            && headers.TryGetProperty("retry-after", out var retry)
            && retry.ValueKind == JsonValueKind.String
            && double.TryParse(retry.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            details.RetryAt = DateTimeOffset.FromUnixTimeMilliseconds(createdMs).AddSeconds(seconds);
        }
    }
}
