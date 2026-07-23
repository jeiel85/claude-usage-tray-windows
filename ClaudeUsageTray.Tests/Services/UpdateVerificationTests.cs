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
        var (seconds, notice) = MainViewModel.ResolveAutoUpdatePlan(canVerify: false, autoRetryExhausted: false);

        Assert.Equal(0, seconds);
        Assert.NotNull(notice);
    }

    [Fact]
    public void AutoUpdatePlan_DisablesCountdown_WhenPreviousAutoAttemptFailed()
    {
        var (seconds, notice) = MainViewModel.ResolveAutoUpdatePlan(canVerify: true, autoRetryExhausted: true);

        Assert.Equal(0, seconds);
        Assert.NotNull(notice);
    }

    [Fact]
    public void AutoUpdatePlan_AllowsCountdown_WhenVerifiableAndNotRetried()
    {
        var (seconds, notice) = MainViewModel.ResolveAutoUpdatePlan(canVerify: true, autoRetryExhausted: false);

        Assert.True(seconds > 0);
        Assert.Null(notice);
    }
}
