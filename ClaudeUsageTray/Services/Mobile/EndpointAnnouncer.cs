namespace ClaudeUsageTray.Services.Mobile;

/// <summary>
/// 이 PC 의 경로에 새 것이 생기면 푸시를 등록한 휴대폰에 알린다(#225, Homebase <c>EndpointAnnouncer</c> 이식, M-23).
/// 휴대폰은 연결에 성공한 뒤에야 <c>/api/v1/endpoints</c> 를 읽으므로, 앱을 다시 시작해 Quick Tunnel 주소가 바뀌면(또는 LAN IP 가
/// 바뀌면) 그 경로로만 닿던 휴대폰은 다시 페어링하기 전까지 끊긴다 — 이 클래스가 그 틈을 메운다.
/// <list type="bullet">
/// <item>목록이 두 번 연속 같을 때만 알린다 — 시작 직후 터널이 리스너보다 몇 초 늦게 열리므로, 반쪽 목록을 보내지 않게.</item>
/// <item>전에 알리지 않은 경로가 있을 때만 보낸다. 경로가 빠지기만 한 경우(Wi-Fi 끊김)는 보내지 않는다 — Wi-Fi 가 오락가락해도
/// 푸시 한도(ntfy.sh: 휴대폰마다 하루 수백 건)를 쓰지 않게.</item>
/// <item>휴대폰마다 따로 기억한다. 받지 못한 휴대폰에만 <see cref="RetryAfter"/> 뒤 다시 보낸다. 나중에 푸시를 등록한 휴대폰도
/// 한 번 받는다. 기억은 메모리에만 둔다 — 앱을 다시 시작하면 한 번 다시 알린다(휴대폰에는 아무것도 뜨지 않는다).</item>
/// </list>
/// </summary>
public sealed class EndpointAnnouncer : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    public delegate Task<IReadOnlyList<string>> SendEndpoints(
        IReadOnlyList<MobileEndpoint> endpoints, IReadOnlyCollection<string> deviceIds, CancellationToken ct);

    private readonly Func<IReadOnlyList<MobileEndpoint>> _endpoints;
    private readonly Func<IReadOnlyList<string>> _targets;
    private readonly SendEndpoints _send;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _tick = new(1, 1);
    private CancellationTokenSource? _cts;

    private HashSet<MobileEndpoint>? _previous;

    /// <summary>기기 ID → 그 휴대폰이 마지막으로 받은 경로 목록.</summary>
    private readonly Dictionary<string, HashSet<MobileEndpoint>> _announced = new(StringComparer.Ordinal);
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;

    /// <param name="endpoints">지금 이 PC 의 경로(서버가 열려 있지 않으면 빈 목록)</param>
    /// <param name="targets">경로 갱신 푸시를 받을 수 있는 기기 ID(구독·토큰 해시가 있는 기기)</param>
    /// <param name="send">보낸 뒤 받은 기기 ID 를 돌려준다</param>
    public EndpointAnnouncer(
        Func<IReadOnlyList<MobileEndpoint>> endpoints,
        Func<IReadOnlyList<string>> targets,
        SendEndpoints send,
        TimeProvider? time = null)
    {
        _endpoints = endpoints;
        _targets = targets;
        _send = send;
        _time = time ?? TimeProvider.System;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _cts is not null;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => LoopAsync(token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
        }
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, _time, ct);
                await TickAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 경로 알림은 부가 기능 — 실패가 사용량 표시를 멈추면 안 된다. 다음 시도는 잠시 뒤로 미룬다.
                GC.KeepAlive(ex);
                lock (_gate) _nextAttempt = _time.GetUtcNow() + RetryAfter;
            }
        }
    }

    /// <summary>
    /// Input : 없음(현재 경로·대상을 읽는다)
    /// Output: 푸시를 시도했는가
    /// 핵심 로직: 클래스 설명의 규칙을 한 번 적용한다. 테스트가 시간을 직접 돌리며 부를 수 있게 공개한다.
    /// </summary>
    public async Task<bool> TickAsync(CancellationToken ct = default)
    {
        await _tick.WaitAsync(ct);
        try
        {
            var current = _endpoints().ToHashSet();
            List<string> due;
            lock (_gate)
            {
                var stable = _previous is not null && _previous.SetEquals(current);
                _previous = current;
                if (!stable || current.Count == 0) return false;

                var targets = _targets();
                foreach (var gone in _announced.Keys.Except(targets).ToList()) _announced.Remove(gone);
                due = targets.Where(id => !_announced.TryGetValue(id, out var told) || !current.IsSubsetOf(told)).ToList();
                if (due.Count == 0 || _time.GetUtcNow() < _nextAttempt) return false;
            }

            var delivered = await _send(current.ToList(), due, ct);
            lock (_gate)
            {
                foreach (var id in delivered) _announced[id] = current;
                _nextAttempt = delivered.Count < due.Count ? _time.GetUtcNow() + RetryAfter : DateTimeOffset.MinValue;
            }
            return true;
        }
        finally
        {
            _tick.Release();
        }
    }

    public void Dispose()
    {
        Stop();
        _tick.Dispose();
    }
}
