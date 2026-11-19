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

    // 목록 행 막대는 새로고침마다 새로 만들어지므로 이름이 있어야 이전 길이에서 차오른다(#325).
    // 이름 앞말은 목록마다 달라야 다른 목록의 같은 한도 ID 를 이어 받지 않는다.
    [Fact]
    public void EveryListRowBar_HasAGaugeKeyWithItsOwnScope()
    {
        var doc = XDocument.Load(PopupXamlPath(), LoadOptions.SetLineInfo);
        var rowBars = doc.Descendants(XName.Get("DataTemplate", WpfNs))
            .SelectMany(t => t.Descendants(XName.Get("ProgressBar", WpfNs)))
            .ToList();
        Assert.NotEmpty(rowBars);

        var scopes = new List<string>();
        foreach (var bar in rowBars)
        {
            var line = ((System.Xml.IXmlLineInfo)bar).LineNumber;
            var key = bar.Attributes().FirstOrDefault(a => a.Name.LocalName == "GaugeAnimation.Key")?.Value;
            Assert.True(key is not null, $"UsagePopup.xaml:{line} list row ProgressBar has no views:GaugeAnimation.Key");
            var scope = System.Text.RegularExpressions.Regex.Match(key!, @"StringFormat='([^':]+):\{0\}'").Groups[1].Value;
            Assert.True(scope.Length > 0, $"UsagePopup.xaml:{line} GaugeAnimation.Key needs StringFormat='<list>:{{0}}': {key}");
            scopes.Add(scope);
        }
        Assert.Equal(scopes.Count, scopes.Distinct(StringComparer.Ordinal).Count());
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

    /// <summary>
    /// #237: 시간선 마커는 <c>views:TimelineMarker</c> 하나로 그린다. 게이지마다 열 두 개짜리 Grid 를 복사하던 때는
    /// 한 곳만 고치면 나머지가 어긋났다. 막대가 있는 틀(높이 8)마다 마커가 하나씩 있는지도 본다.
    /// </summary>
    [Fact]
    public void TimelineMarkers_UseTheSharedElement()
    {
        var text = File.ReadAllText(PopupXamlPath());
        Assert.DoesNotContain("ConverterParameter=inverse", text);
        Assert.DoesNotContain("Background=\"#E2E8F0\"", text);

        var doc = XDocument.Load(PopupXamlPath(), LoadOptions.SetLineInfo);
        const string ViewsNs = "clr-namespace:ClaudeUsageTray.Views";
        var markers = doc.Descendants(XName.Get("TimelineMarker", ViewsNs)).ToList();
        Assert.NotEmpty(markers);

        // 막대 틀(높이 8 Grid 에 ProgressBar)마다 마커가 정확히 하나.
        var frames = doc.Descendants(XName.Get("Grid", WpfNs))
            .Where(g => (string?)g.Attribute("Height") == "8" && g.Elements(XName.Get("ProgressBar", WpfNs)).Any());
        foreach (var frame in frames)
        {
            var line = ((System.Xml.IXmlLineInfo)frame).LineNumber;
            Assert.True(frame.Elements(XName.Get("TimelineMarker", ViewsNs)).Count() == 1,
                $"UsagePopup.xaml:{line} gauge frame needs exactly one views:TimelineMarker");
        }
        foreach (var marker in markers)
        {
            var line = ((System.Xml.IXmlLineInfo)marker).LineNumber;
            Assert.NotNull(marker.Attribute("Percent"));
            Assert.True(((string?)marker.Attribute("Visibility"))?.Contains("BoolToVisibility") == true,
                $"UsagePopup.xaml:{line} TimelineMarker must hide when the window length is unknown");
            Assert.True(marker.Parent?.Elements(XName.Get("ProgressBar", WpfNs)).Any() == true,
                $"UsagePopup.xaml:{line} TimelineMarker is not overlaid on a bar");
        }
    }
}
