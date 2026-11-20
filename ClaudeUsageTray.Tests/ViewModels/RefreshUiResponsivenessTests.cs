using System.IO;
using System.Windows.Threading;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

/// <summary>
/// 새로고침 중 UI 정지 회귀(#311, #314). Codex 로그 스캔·OpenCode DB 읽기는 이 PC 에서 수 초~십수 초 걸린다.
/// 읽기를 UI 스레드에서 하면 팝업·설정 창이 그동안 멈춘다 — 느린 가짜 읽기를 끼우고, 새로고침하는 동안
/// 디스패처 타이머가 끊기지 않고 도는지(가장 긴 틈)를 잰다.
/// </summary>
[Collection("WpfTests")]
[Trait("Category", "Integration")]
public class RefreshUiResponsivenessTests : IDisposable
{
    // 읽기는 1.5초 붙잡고, UI 틈은 0.7초 미만이어야 한다 — 읽기가 UI 스레드에서 돌면 틈이 1.5초를 넘는다.
    private static readonly TimeSpan SlowRead = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan MaxUiGap = TimeSpan.FromMilliseconds(700);

    private readonly string _historyDir = Path.Combine(Path.GetTempPath(), "cut-refresh-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_historyDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static ProviderUsageSnapshot SlowSnapshot(ref int reads)
    {
        Interlocked.Increment(ref reads);
        Thread.Sleep(SlowRead);   // 동기로 붙잡는 읽기 — 실제 로그 스캔과 같은 모양
        return new ProviderUsageSnapshot();
    }

    private static async Task<TimeSpan> LongestUiGapDuring(Func<Task> refresh)
    {
        var longest = TimeSpan.Zero;
        var last = DateTime.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            if (now - last > longest) longest = now - last;
            last = now;
        };
        timer.Start();
        try
        {
            await refresh();
            var end = DateTime.UtcNow;
            if (end - last > longest) longest = end - last;
        }
        finally { timer.Stop(); }
        return longest;
    }

    [Fact]
    public async Task CodexRefresh_DoesNotHoldTheUiThreadWhileReading()
    {
        var reads = 0;
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new CodexViewModel(() => Task.FromResult(SlowSnapshot(ref reads)), new HistoryService(_historyDir));

            var gap = await LongestUiGapDuring(() => vm.RefreshAsync(
                showAbsoluteResetTime: false, ntfyTopic: "", notificationsEnabled: false, notifyOnQuotaReset: false,
                thresholds: [], showUsageAlert: (_, _, _, _, _) => { }, showQuotaResetAlert: _ => { }));

            Assert.Equal(1, reads);
            Assert.True(gap < MaxUiGap, $"Codex 새로고침 중 UI 가 {gap.TotalMilliseconds:F0}ms 멈췄다");
        });
    }

    [Fact]
    public async Task OpenCodeRefresh_DoesNotHoldTheUiThreadWhileReading()
    {
        var reads = 0;
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new OpenCodeViewModel(new OpenCodeUsageMonitor(), new HistoryService(_historyDir),
                webUsage: null, readSnapshot: () => SlowSnapshot(ref reads));

            var gap = await LongestUiGapDuring(vm.RefreshAsync);

            Assert.Equal(1, reads);
            Assert.True(gap < MaxUiGap, $"OpenCode 새로고침 중 UI 가 {gap.TotalMilliseconds:F0}ms 멈췄다");
        });
    }
}
