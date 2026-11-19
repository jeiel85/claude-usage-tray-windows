using System.IO;
using System.Xml.Linq;
using Xunit;

namespace ClaudeUsageTray.Tests.Views;

/// <summary>
/// 팝업 막대 묶음 규칙(#322, #325): 모든 <c>ProgressBar</c> 는 <c>Value="{Binding}"</c> 가 아니라
/// <c>views:GaugeAnimation.Value</c> 로 묶는다. 새 공급자 막대를 예전 방식으로 넣으면 그 막대만 차오르지 않고,
/// 페이스 오버레이 두 겹 중 하나만 바뀌면 두 막대가 어긋나 보인다. 템플릿 안의 행까지 보려고 XAML 원문을 읽는다
/// (실행 중 트리에는 항목이 있을 때만 행이 생긴다).
/// </summary>
public class PopupGaugeBindingRuleTests
{
    private const string WpfNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string PopupXamlPath()
    {
        var root = Environment.GetEnvironmentVariable("CUT_REPO_ROOT") is { Length: > 0 } env ? new DirectoryInfo(env) : null;
        var dir = root ?? new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "claude-usage-tray-windows.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ClaudeUsageTray", "Views", "UsagePopup.xaml");
    }

    [Fact]
    public void EveryPopupBar_IsBoundThroughGaugeAnimation()
    {
        var doc = XDocument.Load(PopupXamlPath(), LoadOptions.SetLineInfo);
        var bars = doc.Descendants(XName.Get("ProgressBar", WpfNs)).ToList();
        Assert.NotEmpty(bars);

        foreach (var bar in bars)
        {
            var line = ((System.Xml.IXmlLineInfo)bar).LineNumber;
            Assert.True(bar.Attribute("Value") is null, $"UsagePopup.xaml:{line} ProgressBar sets Value directly");
            Assert.Empty(bar.Elements(XName.Get("ProgressBar.Value", WpfNs)));
            Assert.True(bar.Attributes().Any(a => a.Name.LocalName == "GaugeAnimation.Value"),
                $"UsagePopup.xaml:{line} ProgressBar is not bound through views:GaugeAnimation.Value");
        }
    }

    [Fact]
    public void BarStyles_DoNotSetValue()
    {
        var doc = XDocument.Load(PopupXamlPath());
        var valueSetters = doc.Descendants(XName.Get("Style", WpfNs))
            .Where(s => (string?)s.Attribute("TargetType") == "ProgressBar")
            .SelectMany(s => s.Descendants(XName.Get("Setter", WpfNs)))
            .Where(s => (string?)s.Attribute("Property") == "Value");
        Assert.Empty(valueSetters);
    }
}
