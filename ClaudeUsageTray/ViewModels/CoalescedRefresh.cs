namespace ClaudeUsageTray.ViewModels;

/// <summary>
/// 겹쳐 들어온 새로고침을 하나로 합친다(#314). 진행 중에 들어온 요청은 따로 돌리지 않고, 지금 것이 끝나면 한 번만 더 돈다 —
/// 그 사이 바뀐 설정(키·표시 공급자 등)도 반영되고, 요청한 쪽은 그 마지막 실행이 끝날 때 돌아온다.
/// 공급자 읽기가 <c>Task.Run</c> 으로 옮겨진 뒤(#311) 주기 갱신과 설정 저장·수동 새로고침이 겹치면 Codex 로그 스캔·
/// OpenCode DB 읽기가 동시에 두 번 돌고, 먼저 끝난 쪽이 로딩 표시를 꺼 버렸다.
/// </summary>
internal sealed class CoalescedRefresh(Func<Task> work)
{
    private readonly object _lock = new();
    private TaskCompletionSource? _running;
    private bool _again;

    public Task RunAsync()
    {
        TaskCompletionSource started;
        lock (_lock)
        {
            if (_running != null)
            {
                _again = true;
                return _running.Task;
            }
            started = _running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        _ = LoopAsync(started);
        return started.Task;
    }

    private async Task LoopAsync(TaskCompletionSource done)
    {
        try
        {
            while (true)
            {
                await work();
                lock (_lock)
                {
                    if (!_again)
                    {
                        _running = null;
                        break;
                    }
                    _again = false;
                }
            }
            done.SetResult();
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _running = null;
                _again = false;
            }
            done.SetException(ex);
        }
    }
}
