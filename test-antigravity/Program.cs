// One-shot smoke test for AntigravityUsageMonitor.
// Run: dotnet run --project test-antigravity (저장소 루트에서).
// --raw 를 붙이면 앱이 읽는 응답 원문도 함께 찍는다 — 새 한도(예: 이미지 생성)가 어느 필드로 오는지 확인할 때 쓴다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ClaudeUsageTray.Services;

internal static class Program
{
    private const string CloudCodeBase = "https://daily-cloudcode-pa.googleapis.com/v1internal";

    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--raw"))
            await DumpRawAsync();

        using var mon = new AntigravityUsageMonitor();
        var snap = await mon.GetSnapshotAsync();
        Console.WriteLine($"HasData          = {snap.HasData}");
        Console.WriteLine($"ErrorMessage     = {snap.ErrorMessage}");
        Console.WriteLine($"IsInformational  = {snap.IsInformational}");
        Console.WriteLine($"TierName         = {snap.TierName}");
        Console.WriteLine($"PaidTierName     = {snap.PaidTierName}");
        Console.WriteLine($"AiCredits        = {snap.AiCredits?.ToString("N0") ?? "(not eligible)"}");
        Console.WriteLine($"Buckets.Count    = {snap.Models.Count}");
        foreach (var m in snap.Models)
        {
            Console.WriteLine($"  - {m.DisplayName,-38} {m.RemainingFraction,6:P0} left  window={m.TokenType,-7} id={m.ModelId,-12} reset={m.ResetTime:u}");
        }
        await VerifyRefreshPathAsync();
        return snap.HasData ? 0 : 1;
    }

    /// <summary>
    /// 앱이 부르는 응답의 원문을 찍는다. 개인 정보(이메일·프로젝트 ID·토큰)는 찍지 않는다.
    ///  - retrieveUserQuotaSummary: 그대로(그룹·버킷 전체)
    ///  - loadCodeAssist: tier 이름과 availableCredits 만
    ///  - fetchAvailableModels: quotaInfo 가 있는 모델의 ID·표시 이름·잔여 비율·리셋 시각만
    /// 저장된 access_token 이 만료됐으면 Antigravity 를 한 번 실행한 뒤 다시 돌린다.
    /// </summary>
    private static async Task DumpRawAsync()
    {
        Console.WriteLine("-- raw responses --");
        var cred = AntigravityUsageMonitor.ReadCredStore();
        if (cred?.AccessToken is not { Length: > 0 } token ||
            cred.ExpiresAt is not { } expiry || expiry <= DateTimeOffset.UtcNow)
        {
            Console.WriteLine("access_token 없음 또는 만료 — Antigravity 를 한 번 실행한 뒤 다시 시도하세요.");
            Console.WriteLine();
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("antigravity/2.6.0");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var assist = await PostAsync(http, "loadCodeAssist", "{}");
        string projectBody = "{}";
        if (assist is not null)
        {
            var root = assist.RootElement;
            if (root.TryGetProperty("cloudaicompanionProject", out var project) && project.ValueKind == JsonValueKind.String)
                projectBody = JsonSerializer.Serialize(new { project = project.GetString() });

            Console.WriteLine("[loadCodeAssist]");
            foreach (var name in new[] { "currentTier", "paidTier" })
            {
                if (!root.TryGetProperty(name, out var tier) || tier.ValueKind != JsonValueKind.Object) continue;
                var tierName = tier.TryGetProperty("name", out var n) ? n.ToString() : "";
                var credits = tier.TryGetProperty("availableCredits", out var c) ? c.GetRawText() : "(none)";
                Console.WriteLine($"  {name}: name={tierName} availableCredits={credits}");
            }
        }

        using var summary = await PostAsync(http, "retrieveUserQuotaSummary", projectBody);
        if (summary is not null)
        {
            Console.WriteLine("[retrieveUserQuotaSummary]");
            Console.WriteLine(JsonSerializer.Serialize(summary.RootElement, new JsonSerializerOptions { WriteIndented = true }));
        }

        using var models = await PostAsync(http, "fetchAvailableModels", projectBody);
        if (models is not null &&
            models.RootElement.TryGetProperty("models", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            Console.WriteLine("[fetchAvailableModels — quotaInfo 가 있는 모델]");
            foreach (var model in map.EnumerateObject())
            {
                if (!model.Value.TryGetProperty("quotaInfo", out var quota)) continue;
                var label = model.Value.TryGetProperty("displayName", out var d) ? d.ToString() : "";
                Console.WriteLine($"  {model.Name,-40} {label,-32} quotaInfo={quota.GetRawText()}");
            }
        }
        Console.WriteLine();
    }

    private static async Task<JsonDocument?> PostAsync(HttpClient http, string method, string body)
    {
        using var resp = await http.PostAsync($"{CloudCodeBase}:{method}", new StringContent(body, Encoding.UTF8, "application/json"));
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"[{method}] HTTP {(int)resp.StatusCode}");
            return null;
        }
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 저장된 access_token 이 만료됐을 때 타는 경로를 미리 확인한다.
    /// 설치된 바이너리에서 찾아낸 client 자격 증명으로 실제 갱신이 되는지까지 본다.
    /// </summary>
    private static async Task VerifyRefreshPathAsync()
    {
        Console.WriteLine();
        Console.WriteLine("-- refresh path --");

        var path = AntigravityOAuthClientLocator.FindLanguageServerPath();
        Console.WriteLine($"language_server  = {path ?? "(not found)"}");
        if (path is null) return;

        var started = DateTimeOffset.UtcNow;
        IReadOnlyList<AntigravityOAuthClient> candidates;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            candidates = AntigravityOAuthClientLocator.ScanCandidates(fs);
        }
        Console.WriteLine($"scan             = {(DateTimeOffset.UtcNow - started).TotalSeconds:0.0}s, {candidates.Count} candidate pair(s)");
        foreach (var c in candidates)
        {
            Console.WriteLine($"  - {c.ClientId}  secret={c.ClientSecret[..14]}…({c.ClientSecret.Length})");
        }
        if (candidates.Count == 0) return;

        var refreshToken = AntigravityUsageMonitor.ReadCredStore()?.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken)) { Console.WriteLine("refresh_token    = (unavailable)"); return; }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        foreach (var c in candidates)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = c.ClientId,
                ["client_secret"] = c.ClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            });
            using var resp = await http.PostAsync("https://oauth2.googleapis.com/token", form);
            Console.WriteLine($"  try {c.ClientId[..20]}… -> HTTP {(int)resp.StatusCode}");
            if (resp.IsSuccessStatusCode)
            {
                Console.WriteLine("  => refresh OK (앱이 꺼진 채 토큰이 만료돼도 갱신 가능)");
                return;
            }
        }
        Console.WriteLine("  => 어떤 조합으로도 갱신하지 못함");
    }
}
