// One-shot smoke test for AntigravityUsageMonitor.
// Run: dotnet run --project test-antigravity from ClaudeUsageTray/.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ClaudeUsageTray.Services;

internal static class Program
{
    private static async Task<int> Main()
    {
        using var mon = new AntigravityUsageMonitor();
        var snap = await mon.GetSnapshotAsync();
        Console.WriteLine($"HasData          = {snap.HasData}");
        Console.WriteLine($"ErrorMessage     = {snap.ErrorMessage}");
        Console.WriteLine($"IsInformational  = {snap.IsInformational}");
        Console.WriteLine($"TierName         = {snap.TierName}");
        Console.WriteLine($"PaidTierName     = {snap.PaidTierName}");
        Console.WriteLine($"Buckets.Count    = {snap.Models.Count}");
        foreach (var m in snap.Models)
        {
            Console.WriteLine($"  - {m.DisplayName,-38} {m.RemainingFraction,6:P0} left  window={m.TokenType,-7} id={m.ModelId,-12} reset={m.ResetTime:u}");
        }
        await VerifyRefreshPathAsync();
        return snap.HasData ? 0 : 1;
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
