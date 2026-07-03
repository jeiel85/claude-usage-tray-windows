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
}
