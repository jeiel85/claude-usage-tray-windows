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

    public ProviderUsageSnapshot GetTodaySnapshot(long dailyTokenGoal = 100000)
    {
        var snapshot = new ProviderUsageSnapshot();

        if (!File.Exists(_dbPath))
        {
            snapshot.ErrorMessage = Loc.OpenCodeDbNotFound;
            return snapshot;
        }

        try
        {
            var todayMs    = new DateTimeOffset(DateTime.Today).ToUnixTimeMilliseconds();
            var tomorrowMs = new DateTimeOffset(DateTime.Today.AddDays(1)).ToUnixTimeMilliseconds();

            long totalInput = 0, totalOutput = 0, totalCacheRead = 0, totalCacheWrite = 0;
            int requestCount = 0;
            var hourlyTokens = new long[24];

            var connStr = $"Data Source={_dbPath};Mode=ReadOnly;Cache=Shared";
            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT data, json_extract(data, '$.time.created') AS created_ms
                FROM message
                WHERE json_extract(data, '$.role') = 'assistant'
                  AND json_extract(data, '$.time.created') >= @todayMs
                  AND json_extract(data, '$.time.created') < @tomorrowMs
            ";
            cmd.Parameters.AddWithValue("@todayMs", todayMs);
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

                    if (!root.TryGetProperty("tokens", out var tokensEl)) continue;

                    long inp = 0, out_ = 0, cacheRead = 0, cacheWrite = 0;
                    if (tokensEl.TryGetProperty("input",  out var inpEl))  inpEl.TryGetInt64(out inp);
                    if (tokensEl.TryGetProperty("output", out var outEl))  outEl.TryGetInt64(out out_);
                    if (tokensEl.TryGetProperty("cache",  out var cacheEl))
                    {
                        if (cacheEl.TryGetProperty("read",  out var crEl)) crEl.TryGetInt64(out cacheRead);
                        if (cacheEl.TryGetProperty("write", out var cwEl)) cwEl.TryGetInt64(out cacheWrite);
                    }

                    if (inp + out_ == 0) continue;

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
            snapshot.HasData      = requestCount > 0;
            snapshot.ErrorMessage = requestCount == 0 ? Loc.OpenCodeNoUsageToday : null;
            
            // 일일 목표량 기준 퍼센트 계산
            snapshot.ShortUsagePercent = dailyTokenGoal > 0 
                ? Math.Min(1.0, (double)totalOutputTokens / dailyTokenGoal) 
                : 0;
            snapshot.PlanType = "Local";
        }
        catch (Exception ex)
        {
            snapshot.ErrorMessage = ex.Message;
        }

        return snapshot;
    }
}
