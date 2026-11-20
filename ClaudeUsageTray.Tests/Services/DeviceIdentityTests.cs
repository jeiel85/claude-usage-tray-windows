using System.IO;
using System.Text.RegularExpressions;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

// 계정을 모르는 PC 의 알림 사건 키에 넣는 기기 구분자(#175). Codex 리뷰봇 5차 지적:
// 실행마다 새로 뽑는 값이면 재시작 뒤에 직전 알림을 알아보지 못한다(레이트 리밋 알림은 시작 직후 첫 관측에서도 나간다).
public class DeviceIdentityTests
{
    private static readonly Regex ValidId = new("^[0-9a-f]{32}$");

    private static string TempPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"device-id-tests-{Guid.NewGuid():N}");
        return Path.Combine(dir, "nested", "claude-usage-tray-device-id");
    }

    [Fact]
    public void CreatesAValidIdAndStoresIt()
    {
        var path = TempPath();

        var id = DeviceIdentity.GetOrCreate(path);

        Assert.Matches(ValidId, id);
        Assert.True(File.Exists(path));
        Assert.Equal(id, File.ReadAllText(path).Trim());
    }

    // 핵심: 재시작(= 새 프로세스가 같은 파일을 다시 읽음)해도 같은 ID 라야 재시작 뒤에도 직전 알림을 알아본다.
    [Fact]
    public void ReturnsTheSameIdAcrossRestarts()
    {
        var path = TempPath();

        var first = DeviceIdentity.GetOrCreate(path);
        var afterRestart = DeviceIdentity.GetOrCreate(path);
        var afterAnotherRestart = DeviceIdentity.GetOrCreate(path);

        Assert.Equal(first, afterRestart);
        Assert.Equal(first, afterAnotherRestart);
    }

    // 서로 다른 설치는 서로 다른 ID — 이름 기반이 아니라 무작위라 같은 이름 설정으로는 겹치지 않는다.
    [Fact]
    public void DifferentInstallations_GetDifferentIds()
    {
        var ids = Enumerable.Range(0, 20).Select(_ => DeviceIdentity.GetOrCreate(TempPath())).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void IsNotDerivedFromTheMachineOrUserName()
    {
        var id = DeviceIdentity.GetOrCreate(TempPath());

        Assert.DoesNotContain(Environment.MachineName, id, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, id, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-device-id")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]   // 대문자는 우리가 만든 형식이 아니다
    [InlineData("0123456789abcdef0123456789abcde")]    // 31자리
    public void ReplacesACorruptFileWithAValidIdThatThenStaysStable(string content)
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        var id = DeviceIdentity.GetOrCreate(path);

        Assert.Matches(ValidId, id);
        Assert.Equal(id, DeviceIdentity.GetOrCreate(path));
    }

    [Fact]
    public void AcceptsAnExistingIdEvenWithTrailingNewline()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "0123456789abcdef0123456789abcdef\r\n");

        Assert.Equal("0123456789abcdef0123456789abcdef", DeviceIdentity.GetOrCreate(path));
    }

    // 알림 경로를 끊으면 안 된다: 저장할 수 없는 위치여도 예외 없이 이번 실행에서만 쓰는 ID 를 돌려준다.
    [Fact]
    public void FallsBackToATemporaryIdWhenTheFileCannotBeWritten()
    {
        var blocker = Path.Combine(Path.GetTempPath(), $"device-id-blocker-{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "a regular file, so it cannot be used as a directory");
        var path = Path.Combine(blocker, "claude-usage-tray-device-id");

        var id = DeviceIdentity.GetOrCreate(path);

        Assert.Matches(ValidId, id);
        Assert.False(File.Exists(path));
    }

    // 다른 프로그램이 파일을 열어 두고 있어도(백신·동기화 등) 읽혀야 한다 — FileShare.ReadWrite.
    [Fact]
    public void CanReadWhileAnotherHandleHoldsTheFileOpenForWriting()
    {
        var path = TempPath();
        var id = DeviceIdentity.GetOrCreate(path);

        using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal(id, DeviceIdentity.GetOrCreate(path));
    }

    // 앱이 실제로 쓰는 경로(NtfyEventKey 의 기본 기기 구분자)가 이 ID 를 그대로 쓴다.
    [Fact]
    public void TheDefaultDeviceOfEventKeys_IsThePersistedId()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

        var byDefault = NtfyEventKey.ForUsage("Codex", "short", 90, now.AddHours(3), now);
        var byPersistedId = NtfyEventKey.ForUsage("Codex", "short", 90, now.AddHours(3), now, null,
            deviceId: DeviceIdentity.Current);

        Assert.Equal(byPersistedId.SequenceId, byDefault.SequenceId);
    }
}
