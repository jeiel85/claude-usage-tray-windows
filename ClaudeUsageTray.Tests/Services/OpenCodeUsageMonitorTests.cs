using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public sealed class OpenCodeUsageMonitorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"opencode-usage-{Guid.NewGuid():N}.db");

    [Fact]
    public void GetSnapshot_ReportsActualPeriodsWithoutInventingQuota()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(9));
        CreateDatabase();
        InsertUsage(now.AddHours(-1), 100, 50, 10, 0, 0.50m);
        InsertUsage(now.AddHours(-6), 30, 20, 0, 0, 0.20m);
        InsertUsage(now.AddDays(-8), 7, 3, 0, 0, 0.10m);
        InsertLimit(now.AddMinutes(-10), "FreeUsageLimitError", "7200");

        var snapshot = new OpenCodeUsageMonitor(_dbPath).GetSnapshot(now);

        Assert.True(snapshot.HasData);
        Assert.Equal(0, snapshot.ShortUsagePercent);
        Assert.NotNull(snapshot.OpenCodeDetails);
        Assert.Equal(160, snapshot.OpenCodeDetails.LastFiveHours.Tokens);
        Assert.Equal(210, snapshot.OpenCodeDetails.LastSevenDays.Tokens);
        Assert.Equal(220, snapshot.OpenCodeDetails.ThisMonth.Tokens);
        Assert.Equal(0.50m, snapshot.OpenCodeDetails.LastFiveHours.CostUsd);
        Assert.Equal("free", snapshot.OpenCodeDetails.LimitKind);
        Assert.Equal(now.AddMinutes(-10).AddHours(2), snapshot.OpenCodeDetails.RetryAt);
    }

    // #201: 토큰·비용 값 하나가 숫자가 아니어도(null·문자열) 그 행의 나머지 값은 집계한다.
    // JsonElement.TryGetInt64/TryGetDecimal 은 숫자가 아닌 요소에서 예외를 던져, 예전에는 그 행 전체가 빠졌다.
    [Fact]
    public void GetSnapshot_NonNumericTokenValues_SkipOnlyThatField()
    {
        // 이 PC 의 오늘 정오 — 고정 시각(+09:00)은 어떤 시간대에서는 현지 자정 근처라 행이 "어제" 로 빠진다.
        var now = new DateTimeOffset(DateTime.Today.AddHours(12));
        CreateDatabase();
        Insert(new
        {
            role = "assistant",
            time = new { created = now.AddMinutes(-10).ToUnixTimeMilliseconds() },
            tokens = new { input = (long?)null, output = 50, cache = new { read = "10", write = 5 } },
            cost = (decimal?)null,
        });

        var snapshot = new OpenCodeUsageMonitor(_dbPath).GetSnapshot(now);

        Assert.True(snapshot.HasData);
        Assert.Equal(1, snapshot.RequestCount);
        Assert.Equal(0, snapshot.TotalInputTokens);
        Assert.Equal(50, snapshot.TotalOutputTokens);
        Assert.Equal(0, snapshot.TotalCacheReadTokens);
        Assert.Equal(5, snapshot.TotalCacheWriteTokens);
        Assert.NotNull(snapshot.OpenCodeDetails);
        Assert.Equal(55, snapshot.OpenCodeDetails.LastFiveHours.Tokens);
        Assert.Equal(0m, snapshot.OpenCodeDetails.LastFiveHours.CostUsd);
    }

    private void CreateDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE message (data TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    private void InsertUsage(DateTimeOffset created, long input, long output, long cacheRead, long cacheWrite, decimal cost)
    {
        Insert(new
        {
            role = "assistant",
            time = new { created = created.ToUnixTimeMilliseconds() },
            tokens = new { input, output, cache = new { read = cacheRead, write = cacheWrite } },
            cost
        });
    }

    private void InsertLimit(DateTimeOffset created, string errorName, string retryAfter)
    {
        Insert(new
        {
            role = "assistant",
            time = new { created = created.ToUnixTimeMilliseconds() },
            error = new { data = new { responseBody = errorName, responseHeaders = new Dictionary<string, string> { ["retry-after"] = retryAfter } } }
        });
    }

    private void Insert(object value)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO message(data) VALUES (@data)";
        command.Parameters.AddWithValue("@data", JsonSerializer.Serialize(value));
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    // 로그인 파일에는 OpenCode 자체 로그인 말고 다른 공급자 키도 함께 들어 있다.
    [Fact]
    public void TryReadAuthProviderId_PicksOpenCodeEntryOnly()
    {
        var authPath = Path.Combine(Path.GetTempPath(), $"opencode-auth-{Guid.NewGuid():N}.json");
        File.WriteAllText(authPath,
            """{"anthropic":{"type":"oauth"},"opencode-go":{"type":"api","key":"redacted"}}""");
        try
        {
            Assert.Equal("opencode-go", OpenCodeUsageMonitor.TryReadAuthProviderId(authPath));
        }
        finally
        {
            File.Delete(authPath);
        }
    }

    [Fact]
    public void TryReadAuthProviderId_ReturnsNullWithoutOpenCodeEntry()
    {
        var authPath = Path.Combine(Path.GetTempPath(), $"opencode-auth-{Guid.NewGuid():N}.json");
        File.WriteAllText(authPath, """{"anthropic":{"type":"oauth"}}""");
        try
        {
            Assert.Null(OpenCodeUsageMonitor.TryReadAuthProviderId(authPath));
        }
        finally
        {
            File.Delete(authPath);
        }
    }

    [Fact]
    public void TryReadAuthProviderId_ReturnsNullWhenFileMissing()
        => Assert.Null(OpenCodeUsageMonitor.TryReadAuthProviderId(
            Path.Combine(Path.GetTempPath(), $"opencode-missing-{Guid.NewGuid():N}.json")));
}
