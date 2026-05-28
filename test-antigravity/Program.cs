// One-shot smoke test for AntigravityUsageMonitor.
// Run: dotnet run --project test-antigravity from ClaudeUsageTray/.
using System;
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
        Console.WriteLine($"TierName         = {snap.TierName}");
        Console.WriteLine($"PaidTierName     = {snap.PaidTierName}");
        Console.WriteLine($"Models.Count     = {snap.Models.Count}");
        foreach (var m in snap.Models)
        {
            Console.WriteLine($"  - {m.ModelId,-24} type={m.TokenType,-10} remaining={m.RemainingFraction,6:0.000}  reset={m.ResetTime:o}");
        }
        return snap.HasData ? 0 : 1;
    }
}
