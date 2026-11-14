using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>페어링된 휴대폰 하나. 토큰 평문은 저장하지 않는다.</summary>
public sealed record MobileDeviceInfo(
    string DeviceId,
    string Name,
    string? Model,
    DateTimeOffset PairedAt,
    DateTimeOffset? LastSeen);

/// <summary>
/// 페어링된 휴대폰 목록과 이 PC 의 pcId 를 <c>%APPDATA%\ClaudeUsageTray\mobile\state.json</c> 에 둔다.
///
/// 왜 이렇게 했나:
/// - 토큰은 SHA-256 만 저장한다(Homebase M-2). 파일이 새어도 휴대폰 행세를 할 수 없다.
/// - pcId 는 이 파일에서 따로 만든다. <see cref="DeviceIdentity"/>(~/.claude 안)는 프로필 폴더를 통째로 복제하면
///   두 PC 가 같은 값을 갖는데, 휴대폰은 pcId 로 PC 를 구분하므로 겹치면 한 PC 가 다른 PC 를 덮어쓴다.
/// - 원자적 쓰기(임시 파일 → 이동). 파일이 깨졌으면 원본을 <c>state.json.corrupt-*</c> 로 남기고 빈 상태로 시작한다 —
///   트레이 본기능(사용량 표시)을 멈추지 않고, 휴대폰은 401 을 받아 다시 페어링하라는 안내를 본다.
/// </summary>
public sealed class MobileDeviceStore
{
    private static readonly TimeSpan LastSeenWriteInterval = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions FileJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private State _state;

    public event Action? Changed;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageTray", "mobile");

    public MobileDeviceStore(string? directory = null, TimeProvider? time = null)
    {
        _path = Path.Combine(directory ?? DefaultDirectory, "state.json");
        _time = time ?? TimeProvider.System;
        _state = Load();
    }

    public string PcId
    {
        get
        {
            lock (_gate) return _state.PcId;
        }
    }

    public IReadOnlyList<MobileDeviceInfo> List()
    {
        lock (_gate) return _state.Devices.Select(ToInfo).OrderBy(d => d.PairedAt).ToList();
    }

    public MobileDeviceInfo Add(string name, string? model, string tokenSha256Hex)
    {
        MobileDeviceInfo info;
        lock (_gate)
        {
            var record = new DeviceRecord
            {
                DeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Name = name,
                Model = model,
                TokenSha256 = tokenSha256Hex,
                PairedAt = _time.GetUtcNow(),
            };
            _state.Devices.Add(record);
            Save();
            info = ToInfo(record);
        }
        Changed?.Invoke();
        return info;
    }

    public bool Revoke(string deviceId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _state.Devices.RemoveAll(d => d.DeviceId == deviceId) > 0;
            if (removed) Save();
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    /// <summary>
    /// Input : Bearer 토큰 평문
    /// Output: 그 토큰의 기기, 없으면 null
    /// 핵심 로직: 해시끼리 고정시간 비교. 마지막 접속 시각은 10분에 한 번만 파일에 쓴다 —
    ///           WebSocket 재연결마다 디스크를 쓰지 않게(값은 "대략 언제" 를 보여 주는 용도뿐이다).
    /// </summary>
    public MobileDeviceInfo? Authenticate(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var hash = Encoding.ASCII.GetBytes(PairingService.HashToken(token));
        lock (_gate)
        {
            foreach (var d in _state.Devices)
            {
                if (!CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(d.TokenSha256 ?? ""))) continue;
                var now = _time.GetUtcNow();
                if (d.LastSeen is null || now - d.LastSeen.Value >= LastSeenWriteInterval)
                {
                    d.LastSeen = now;
                    Save();
                }
                return ToInfo(d);
            }
        }
        return null;
    }

    /// <summary>실시간 전송마다 부르는 가벼운 확인 — 트레이에서 해제한 휴대폰을 다음 전송 전에 끊는다.</summary>
    public bool IsPaired(string deviceId)
    {
        lock (_gate) return _state.Devices.Any(d => d.DeviceId == deviceId);
    }

    private static MobileDeviceInfo ToInfo(DeviceRecord d) =>
        new(d.DeviceId ?? "", d.Name ?? "", d.Model, d.PairedAt, d.LastSeen);

    private State Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var loaded = JsonSerializer.Deserialize<State>(stream, FileJson);
                if (loaded is not null && IsValidPcId(loaded.PcId))
                {
                    loaded.Devices = (loaded.Devices ?? [])
                        .Where(d => !string.IsNullOrEmpty(d.DeviceId) && d.TokenSha256 is { Length: 64 })
                        .ToList();
                    return loaded;
                }
                PreserveCorrupt();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            PreserveCorrupt();
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[MobileDeviceStore] state.json unreadable: {ex.Message}");
#endif
        }

        var fresh = new State { PcId = Guid.NewGuid().ToString("N") };
        _state = fresh;
        Save();
        return fresh;
    }

    private void PreserveCorrupt()
    {
        try
        {
            if (File.Exists(_path))
                File.Move(_path, _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GC.KeepAlive(ex);
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory, $"state.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
            {
                JsonSerializer.Serialize(stream, _state, FileJson);
            }
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 저장 실패는 이번 실행의 메모리 상태로 버틴다(재시작하면 그 페어링만 잃는다).
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[MobileDeviceStore] save failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
        }
    }

    private static bool IsValidPcId(string? pcId) =>
        pcId is { Length: 32 } && pcId.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed class State
    {
        public string PcId { get; set; } = "";
        public List<DeviceRecord> Devices { get; set; } = [];
    }

    private sealed class DeviceRecord
    {
        public string? DeviceId { get; set; }
        public string? Name { get; set; }
        public string? Model { get; set; }
        public string? TokenSha256 { get; set; }
        public DateTimeOffset PairedAt { get; set; }
        public DateTimeOffset? LastSeen { get; set; }
    }
}
