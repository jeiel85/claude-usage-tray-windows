using ClaudeUsageTray.Views;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

[Collection("WpfTests")]
[Trait("Category", "Integration")]
public class UpdateDialogTests
{
    // v1.33.8 회귀 방지: UpdateDialog 가 참조하는 StaticResource(DangerBrush 등)가 하나라도
    // 정의돼 있지 않으면 InitializeComponent() 가 XamlParseException 을 던진다. 그러면 모달이
    // 생성조차 되지 못해 "업데이트가 열리지 않는" 버그가 된다(v1.32.4~v1.33.7 실제 원인).
    // 창을 띄우지 않고 생성만 해도 BAML 로드 시점에 모든 StaticResource 가 해석되므로 이 테스트로 충분하다.
    [Fact]
    public async Task Constructs_WithoutMissingStaticResource()
    {
        await WpfTestHost.RunAsync(() =>
        {
            UpdateDialog? dialog = null;
            var ex = Record.Exception(() =>
            {
                // 마크다운 렌더러(**bold**, `code`, ## 헤더, - 리스트)도 함께 통과시킨다.
                dialog = new UpdateDialog(
                    "v9.9.9",
                    "## v9.9.9\n- **bold** item with `code`\n- plain item",
                    onSkip: () => { });
            });

            Assert.Null(ex);
            Assert.NotNull(dialog);
        });
    }

    // 카운트다운이 끝나면 사용자가 "지금 업데이트"를 누르지 않아도 설치가 시작돼야 한다.
    // StartedAutomatically 는 호출부가 무한 재시도 방지 표식을 남기는 기준이라 함께 검증한다.
    [Fact]
    public async Task Countdown_StartsUpdateAutomatically_WhenItExpires()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateDialog? dialog = null;

        await WpfTestHost.RunAsync(() =>
        {
            dialog = new UpdateDialog("v9.9.9", "- note", onSkip: () => { }, autoUpdateSeconds: 1);
            dialog.OnUpdateRequested += () => started.TrySetResult(dialog!.StartedAutomatically);
        });

        var finished = await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(started.Task, finished);
        Assert.True(await started.Task);

        await WpfTestHost.RunAsync(() => dialog!.Close());
    }

    // autoUpdateSeconds 를 주지 않으면(수동 확인 경로, 또는 직전 자동 적용이 실패한 버전)
    // 모달은 스스로 설치를 시작하지 않아야 한다.
    [Fact]
    public async Task NoCountdown_DoesNotStartUpdateOnItsOwn()
    {
        var requested = false;
        UpdateDialog? dialog = null;

        await WpfTestHost.RunAsync(() =>
        {
            dialog = new UpdateDialog("v9.9.9", "- note", onSkip: () => { });
            dialog.OnUpdateRequested += () => requested = true;
        });

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.False(requested);
        Assert.False(dialog!.StartedAutomatically);

        await WpfTestHost.RunAsync(() => dialog!.Close());
    }
}
