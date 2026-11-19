using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

/// <summary>겹친 새로고침 합치기(#314) — 동시에 두 번 돌지 않고, 진행 중 요청은 끝난 뒤 한 번만 더 돈다.</summary>
public class CoalescedRefreshTests
{
    private sealed class GatedWork
    {
        private readonly object _lock = new();
        private readonly Queue<TaskCompletionSource> _gates = new();
        public int Started;
        public int Running;
        public int MaxRunning;

        public async Task RunAsync()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                Started++;
                Running++;
                MaxRunning = Math.Max(MaxRunning, Running);
                _gates.Enqueue(gate);
            }
            await gate.Task;
            lock (_lock) Running--;
        }

        public void ReleaseNext()
        {
            TaskCompletionSource gate;
            lock (_lock) gate = _gates.Dequeue();
            gate.SetResult();
        }

        public async Task WaitStartedAsync(int count)
        {
            for (var i = 0; i < 500 && Volatile.Read(ref Started) < count; i++) await Task.Delay(10);
            Assert.Equal(count, Volatile.Read(ref Started));
        }
    }

    [Fact]
    public async Task RequestsDuringARun_AreFoldedIntoOneMoreRun()
    {
        var work = new GatedWork();
        var refresh = new CoalescedRefresh(work.RunAsync);

        var first = refresh.RunAsync();
        await work.WaitStartedAsync(1);
        var second = refresh.RunAsync();
        var third = refresh.RunAsync();
        Assert.Same(first, second);
        Assert.Same(first, third);

        work.ReleaseNext();
        await work.WaitStartedAsync(2);
        Assert.False(first.IsCompleted);   // 요청한 쪽은 다시 돈 실행이 끝날 때 돌아온다

        work.ReleaseNext();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, work.Started);
        Assert.Equal(1, work.MaxRunning);
    }

    [Fact]
    public async Task AfterARunFinishes_TheNextRequestRunsAgain()
    {
        var calls = 0;
        var refresh = new CoalescedRefresh(() => { calls++; return Task.CompletedTask; });

        await refresh.RunAsync();
        await refresh.RunAsync();

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AFailedRun_IsReportedAndDoesNotBlockTheNextOne()
    {
        var fail = true;
        var refresh = new CoalescedRefresh(() => fail ? throw new InvalidOperationException("boom") : Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(refresh.RunAsync);
        fail = false;
        await refresh.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
