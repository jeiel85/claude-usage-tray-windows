using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ClaudeUsageTray.Tests;

/// <summary>
/// <c>Loc</c> 의 언어는 프로세스 전역 값이다. 언어를 바꾸는 테스트와 문구를 읽는 테스트가 나란히 돌면 서로의 언어를 보고
/// 실패한다 — 로컬에서는 통과하고 CI 러너에서만 가끔 떨어져 릴리스 태그를 다시 달게 된다(v1.50.1). 그래서 <c>Loc.</c> 을
/// 직접 쓰는 테스트 클래스는 모두 <c>[Collection("WpfTests")]</c> 에 둔다는 관례를 원문 검사로 지킨다(#213).
/// 제품 코드를 거쳐 문구를 간접적으로 읽는 테스트까지는 잡지 못한다 — 그런 테스트도 언어를 고정하려면 <c>Loc.SetLanguage</c>
/// 를 부르게 되므로 대부분 여기 걸린다.
/// </summary>
public class LocCollectionRuleTests
{
    private static readonly Regex TopLevelClass = new(
        @"^(?:public |internal )?(?:sealed |static |abstract |partial )*class (\w+)", RegexOptions.Multiline);

    private static readonly Regex LocUse = new(@"(?<![\w.])Loc\.\w");

    private static DirectoryInfo TestProjectDir()
    {
        var root = Environment.GetEnvironmentVariable("CUT_REPO_ROOT") is { Length: > 0 } env ? new DirectoryInfo(env) : null;
        var dir = root ?? new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "claude-usage-tray-windows.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return new DirectoryInfo(Path.Combine(dir!.FullName, "ClaudeUsageTray.Tests"));
    }

    [Fact]
    public void TestClassesUsingLoc_AreInTheWpfTestsCollection()
    {
        var violations = new List<string>();
        var checkedClasses = 0;

        foreach (var file in TestProjectDir().EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(TestProjectDir().FullName, file.FullName);
            if (relative.StartsWith("bin", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("obj", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = File.ReadAllText(file.FullName).Replace("\r\n", "\n");
            var classes = TopLevelClass.Matches(text);
            for (var i = 0; i < classes.Count; i++)
            {
                var start = classes[i].Index;
                var end = i + 1 < classes.Count ? classes[i + 1].Index : text.Length;
                var body = text[start..end];
                if (!body.Contains("[Fact") && !body.Contains("[Theory")) continue;
                if (!LocUse.IsMatch(StripComments(body))) continue;

                checkedClasses++;
                if (!AttributesAbove(text, start).Any(a => a.Contains("Collection(\"WpfTests\")")))
                    violations.Add($"{relative}: {classes[i].Groups[1].Value}");
            }
        }

        // 검사가 아무것도 보지 않고 통과하지 않게 — 지금도 Loc 를 쓰는 테스트 클래스가 여럿 있다.
        Assert.True(checkedClasses >= 10, $"only {checkedClasses} Loc test classes found — is the scan broken?");
        Assert.True(violations.Count == 0,
            "These test classes use Loc but are not in [Collection(\"WpfTests\")]:\n" + string.Join("\n", violations));
    }

    /// <summary>클래스 선언 바로 위의 특성 줄들(문서 주석·빈 줄은 건너뜀).</summary>
    private static IEnumerable<string> AttributesAbove(string text, int classStart)
    {
        var lines = text[..classStart].Split('\n');
        for (var i = lines.Length - 2; i >= 0; i--) // 마지막 조각은 선언 줄의 앞부분(빈 문자열)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[')) { yield return line; continue; }
            if (line.Length == 0 || line.StartsWith("///") || line.StartsWith("//")) continue;
            yield break;
        }
    }

    private static string StripComments(string code) =>
        Regex.Replace(code, @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
}
