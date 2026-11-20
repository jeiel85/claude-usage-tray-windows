using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeUsageTray.Models;

public class ClaudeCredentials
{
    [JsonPropertyName("claudeAiOauth")]
    public ClaudeAiOauth? ClaudeAiOauth { get; set; }

    [JsonPropertyName("organizationUuid")]
    public string? OrganizationUuid { get; set; }
}

public class ClaudeAiOauth
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expiresAt")]
    public long ExpiresAt { get; set; }

    /// <summary>
    /// 원문 그대로 받는다 — 형식이 예상(문자열 배열)과 달라도 자격 파일 전체 파싱이 실패해 "토큰 없음"이 되지 않도록.
    /// </summary>
    [JsonPropertyName("scopes")]
    public JsonElement? ScopesJson { get; set; }

    /// <summary>저장된 scope 목록. 문자열 배열 또는 공백 구분 문자열을 받고, 그 밖의 형식이면 null.</summary>
    [JsonIgnore]
    public string[]? Scopes => ScopesJson switch
    {
        { ValueKind: JsonValueKind.Array } array => array.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToArray(),
        { ValueKind: JsonValueKind.String } text => text.GetString()!
            .Split(' ', StringSplitOptions.RemoveEmptyEntries),
        _ => null
    };

    // Non-serialized mutable fields used during refresh
    [System.Text.Json.Serialization.JsonIgnore]
    public string? NewAccessToken { get; set; }

    [JsonPropertyName("subscriptionType")]
    public string? SubscriptionType { get; set; }

    [JsonPropertyName("rateLimitTier")]
    public string? RateLimitTier { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= ExpiresAt;
}
