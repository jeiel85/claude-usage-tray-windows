using System.Threading.Channels;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>
/// 피드 안의 항목. 계정 키 원문을 들고 있다가 보낼 때 해시로 바꾼다(<see cref="MobileUsageFeed.Current"/>) —
/// 계정을 모를 때 해시에 섞을 pcId 가 기기 저장소를 만든 뒤에야 정해지기 때문이다.
/// </summary>
public sealed record MobileFeedEntry(
    string Provider,
    string? AccountKey,
    string AccountLabel,
    DateTimeOffset ObservedAt,
    string ErrorKind,
    Models.UsageSyncQuotaSnapshot? Quota,
    MobileTodayTotals? Today);

/// <summary>
/// 휴대폰에 보낼 "지금 이 PC 의 사용량" 을 provider 별로 하나씩 들고 있다가, 바뀌면 실시간 구독자에게 알린다.
///
/// 왜 이런 모양인가: Homebase 는 휴대폰이 볼 때만 1초마다 샘플링하지만(M-13), 이 앱의 사용량은 이미 갱신 주기
/// (기본 수 분)마다 조회되고 있다. 그래서 새로 수집하지 않고 기존 갱신 결과를 넘겨받기만 한다 — 휴대폰을 연결해도
/// API 호출 수가 늘지 않는다(레이트 리밋에 영향 없음).
/// </summary>
public sealed class MobileUsageFeed
{
    /// <summary>
    /// 이보다 오래된 항목은 보내지 않는다. provider 를 끄면 더 이상 갱신되지 않으므로, 꺼진 provider 가
    /// 휴대폰에 영원히 남지 않게 하는 정리 기준이다(자동 정리 외에 <see cref="Retain"/> 으로 즉시 뺄 수도 있다).
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    private static readonly string[] Order = ["claude", "codex", "opencode", "antigravity"];

    private readonly object _gate = new();
    private readonly Dictionary<string, MobileFeedEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Channel<bool>> _subscribers = [];
    private readonly TimeProvider _time;

    public MobileUsageFeed(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>
    /// Input : provider 한 개의 최신 사용량
    /// Output: 없음(구독자에게 "바뀜" 신호)
    /// 핵심 로직: 같은 provider 는 덮어쓴다. 내용 비교 없이 매번 알린다 — 갱신 주기가 분 단위라 휴대폰이 받는 양이 작고,
    ///           관측 시각(observedAt)이 바뀐 것 자체가 "PC 가 살아서 갱신 중" 이라는 정보다.
    ///           단, 이번 조회가 실패·쿨다운으로 쿼터 없이 왔으면 같은 계정의 직전 성공 쿼터를 원래 관측 시각 그대로
    ///           이어 붙인다(Codex 리뷰 P2) — 휴대폰은 이 PC 의 스냅샷을 통째로 교체하므로, 그대로 두면 API 한 번
    ///           실패에 게이지가 사라진다. 오류 종류·오늘 토큰은 새 값을 쓴다. 너무 오래된(MaxAge 초과) 쿼터는 잇지 않는다.
    ///           이어 붙이려면 쿼터 자체에 관측 시각이 있어야 한다 — Claude·Codex 쿼터는 관측 시각 없이 오므로(공유 폴더 경로는
    ///           UsageSyncService.CreateSnapshot 이 채운다) 여기서 새로 받은 쿼터에 이번 관측 시각을 찍는다. 찍지 않으면
    ///           이어 붙인 쿼터가 실패할 때마다 "방금 관측" 으로 보이고, MaxAge 판정도 매번 연장되며, 휴대폰은 다른 PC 의
    ///           더 새로운 성공 값보다 이 낡은 값을 고른다(Codex 리뷰 P2).
    /// </summary>
    public void Publish(MobileFeedEntry entry)
    {
        lock (_gate)
        {
            if (entry.Quota is { HasData: true, ObservedAtUtc: null } fresh)
                fresh.ObservedAtUtc = entry.ObservedAt.ToUniversalTime();

            if (entry.Quota is not { HasData: true }
                && _entries.TryGetValue(entry.Provider, out var previous)
                && previous.Quota is { HasData: true, ObservedAtUtc: { } previousObservedAt } previousQuota
                && string.Equals(previous.AccountKey, entry.AccountKey, StringComparison.Ordinal)
                && _time.GetUtcNow() - previousObservedAt <= MaxAge)
            {
                entry = entry with { Quota = previousQuota };
            }
            _entries[entry.Provider] = entry;
            Notify();
        }
    }

    /// <summary>켜진 provider 만 남긴다(설정에서 끈 provider 를 휴대폰에서 바로 지우려고).</summary>
    public void Retain(IReadOnlyCollection<string> providers)
    {
        lock (_gate)
        {
            var removed = _entries.Keys.Where(k => !providers.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var key in removed) _entries.Remove(key);
            if (removed.Count > 0) Notify();
        }
    }

    /// <summary>보낼 목록. 계정 키는 여기서 해시로 바뀌고 원문은 밖으로 나가지 않는다.</summary>
    public IReadOnlyList<MobileProviderUsage> Current(string pcId)
    {
        var now = _time.GetUtcNow();
        List<MobileFeedEntry> entries;
        lock (_gate)
        {
            entries = _entries.Values
                .Where(e => now - e.ObservedAt <= MaxAge)
                .OrderBy(e => Array.IndexOf(Order, e.Provider) is var i && i >= 0 ? i : int.MaxValue)
                .ThenBy(e => e.Provider, StringComparer.Ordinal)
                .ToList();
        }
        return entries.Select(e =>
        {
            var (hash, scope) = MobileAccountHash.For(e.Provider, e.AccountKey, pcId);
            return new MobileProviderUsage(e.Provider, hash, scope, e.AccountLabel, e.ObservedAt, e.ErrorKind, e.Quota, e.Today);
        }).ToList();
    }

    /// <summary>
    /// 실시간 연결 하나의 구독. 용량 1·DropWrite 채널이라 느린 휴대폰에게 신호가 쌓이지 않는다 — 신호를 받으면
    /// 그때의 최신 상태를 통째로 보내므로 중간 신호를 잃어도 상관없다.
    /// </summary>
    public Subscription Subscribe()
    {
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });
        lock (_gate) _subscribers.Add(channel);
        return new Subscription(this, channel);
    }

    public int Watchers
    {
        get
        {
            lock (_gate) return _subscribers.Count;
        }
    }

    private void Notify()
    {
        foreach (var s in _subscribers) s.Writer.TryWrite(true);
    }

    private void Unsubscribe(Channel<bool> channel)
    {
        lock (_gate) _subscribers.Remove(channel);
        channel.Writer.TryComplete();
    }

    public sealed class Subscription : IDisposable
    {
        private readonly MobileUsageFeed _feed;
        private readonly Channel<bool> _channel;
        private bool _disposed;

        internal Subscription(MobileUsageFeed feed, Channel<bool> channel)
        {
            _feed = feed;
            _channel = channel;
        }

        /// <summary>
        /// 다음 변경 신호를 기다린다. <paramref name="timeout"/> 이 지나면 신호 없이 true 를 돌려준다 —
        /// 그때도 다시 보내서 휴대폰이 "마지막 수신 시각" 으로 PC 생존을 판단할 수 있게 한다.
        /// 구독이 끝났으면 false.
        /// </summary>
        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(cts.Token)) return false;
                _channel.Reader.TryRead(out _);
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _feed.Unsubscribe(_channel);
        }
    }
}
