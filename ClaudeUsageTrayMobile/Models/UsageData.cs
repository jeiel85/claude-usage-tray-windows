using System.Text.Json.Serialization;

namespace ClaudeUsageTrayMobile.Models;

public class UsageResponse
{
    [JsonPropertyName("five_hour")]
    public UsageWindow? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public UsageWindow? SevenDay { get; set; }

    [JsonPropertyName("seven_day_opus")]
    public UsageWindow? SevenDayOpus { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsageWindow? SevenDaySonnet { get; set; }

    [JsonPropertyName("extra_usage")]
    public ExtraUsage? ExtraUsage { get; set; }
}

public class UsageWindow
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }

    public double UsagePercent => Math.Min(1.0, Utilization / 100.0);

    public DateTimeOffset? ResetsAtParsed => ResetsAt != null
        ? DateTimeOffset.TryParse(ResetsAt, out var dt) ? dt : null
        : null;
}

public class ExtraUsage
{
    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("used_credits")]
    public long? UsedCredits { get; set; }

    [JsonPropertyName("monthly_limit")]
    public long? MonthlyLimit { get; set; }
}
