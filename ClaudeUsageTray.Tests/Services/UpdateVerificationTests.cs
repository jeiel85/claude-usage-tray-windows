using System.Net.Http;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 무결성 검증 자산(SHA256)이 없는 릴리스를 무인 설치하지 않는다는 정책을 검증한다.
/// 카운트다운 자동 적용이 들어간 뒤로, 사용자가 보지 않는 사이에 검증 불가능한 바이너리가
/// 설치되는 경로가 생기지 않아야 한다.
/// </summary>
public class UpdateVerificationTests
{
    [Fact]
    public async Task Download_RefusesUnverifiableRelease_BeforeFetchingAnything()
    {
        var service = new UpdateService();
        var progressCalls = 0;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAndPrepareUpdateAsync(
                "https://example.invalid/ClaudeUsageTray.exe",
                sha256Url: "",
                onProgress: (_, _) => progressCalls++));

        Assert.Contains("SHA256", ex.Message);
        // onProgress 는 다운로드 시작 시점에 처음 호출된다 — 0 이면 네트워크에 나가기 전에 막힌 것이다.
        Assert.Equal(0, progressCalls);
    }

    [Fact]
    public async Task Download_ProceedsWithoutChecksum_OnlyWhenExplicitlyAllowed()
    {
        var service = new UpdateService();

        var ex = await Record.ExceptionAsync(() =>
            service.DownloadAndPrepareUpdateAsync(
                "https://example.invalid/ClaudeUsageTray.exe",
                sha256Url: "",
                onProgress: (_, _) => { },
                allowUnverified: true));

        // 도달 불가 호스트라 실패는 하지만, 그 실패가 '검증 거부'여서는 안 된다(=가드를 통과했다).
        Assert.NotNull(ex);
        Assert.IsNotType<InvalidOperationException>(ex);
    }

    [Fact]
    public void AutoUpdatePlan_DisablesCountdown_WhenChecksumIsMissing()
    {
        var (seconds, notice) = Plan(canVerify: false);

        Assert.Equal(0, seconds);
        Assert.NotNull(notice);
    }

    // 자동 설치를 꺼 두었더라도 검증 불가 사실은 알려야 한다 — 직접 실행할지 판단하는 데 필요하다.
    [Fact]
    public void AutoUpdatePlan_StillWarnsAboutMissingChecksum_WhenAutoUpdateIsOff()
    {
        var (seconds, notice) = Plan(canVerify: false, autoUpdateEnabled: false);

        Assert.Equal(0, seconds);
        Assert.NotNull(notice);
    }

    [Fact]
    public void AutoUpdatePlan_DisablesCountdown_WhenPreviousAutoAttemptFailed()
    {
        var (seconds, notice) = Plan(autoRetryExhausted: true);

        Assert.Equal(0, seconds);
        Assert.NotNull(notice);
    }

    // 사용자가 끈 경우엔 조용히 수동 모드로 — 본인이 고른 동작이라 경고를 띄우지 않는다.
    [Fact]
    public void AutoUpdatePlan_DisablesCountdownSilently_WhenUserTurnedAutoUpdateOff()
    {
        var (seconds, notice) = Plan(autoUpdateEnabled: false);

        Assert.Equal(0, seconds);
        Assert.Null(notice);
    }

    [Fact]
    public void AutoUpdatePlan_UsesConfiguredCountdown_WhenEverythingIsFine()
    {
        var (seconds, notice) = Plan(countdownSeconds: 25);

        Assert.Equal(25, seconds);
        Assert.Null(notice);
    }

    [Theory]
    [InlineData(0, 60)]      // 미설정(구버전 설정 파일) → 기본값
    [InlineData(-5, 60)]     // 잘못된 값 → 기본값
    [InlineData(3, 10)]      // 하한 미만 → 하한
    [InlineData(9999, 300)]  // 상한 초과 → 상한
    [InlineData(45, 45)]     // 범위 내 → 그대로
    public void AutoUpdateCountdown_IsClampedToUsableRange(int stored, int expected)
    {
        Assert.Equal(expected, MainViewModel.ClampAutoUpdateCountdown(stored));
    }

    private static (int seconds, string? notice) Plan(
        bool canVerify = true, bool autoRetryExhausted = false,
        bool autoUpdateEnabled = true, int countdownSeconds = 60) =>
        MainViewModel.ResolveAutoUpdatePlan(canVerify, autoRetryExhausted, autoUpdateEnabled, countdownSeconds);
}
