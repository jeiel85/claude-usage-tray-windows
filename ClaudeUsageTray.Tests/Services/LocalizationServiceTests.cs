using System.Reflection;
using System.Text.RegularExpressions;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

// Loc.Lang 은 프로세스 전역 상태다. 이 클래스는 언어를 바꿔 두고 문구를 확인하는데,
// 다른 컬렉션이 병렬로 MainViewModel 을 만들면 그 생성자의 LoadSettings() → Loc.SetLanguage()
// 가 중간에 언어를 갈아엎어 단정이 깨진다(CI 에서 ko 케이스가 영어 문구를 받아 실패).
// Loc 을 건드리는 테스트를 한 컬렉션으로 묶어 직렬화한다.
[Collection("WpfTests")]
public class LocalizationServiceTests
{
    [Theory]
    [InlineData("ko", "새 버전 v9.9.9 업데이트", "클릭")]
    [InlineData("zh", "新版本 v9.9.9 可用", "点击")]
    [InlineData("ja", "新バージョン v9.9.9 が利用可能", "クリック")]
    [InlineData("en", "Update v9.9.9 available", "click")]
    public void UpdateAvailable_DoesNotIncludeInstallPrompt(string lang, string expected, string forbidden)
    {
        var originalLang = Loc.CurrentLang;

        try
        {
            Loc.SetLanguage(lang);

            var result = Loc.UpdateAvailable("v9.9.9");

            Assert.Equal(expected, result);
            Assert.False(result.Contains(forbidden, StringComparison.OrdinalIgnoreCase), result);
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }

    // "No access token found" 는 UI에 그대로 노출되면 안 되고(내부 sentinel),
    // 대신 언어별로 구체적 로그인 조치를 안내해야 한다.
    [Theory]
    [InlineData("ko")]
    [InlineData("zh")]
    [InlineData("ja")]
    [InlineData("en")]
    public void NoToken_IsActionable_AndNeverTheRawSentinel(string lang)
    {
        var originalLang = Loc.CurrentLang;

        try
        {
            Loc.SetLanguage(lang);

            var msg = Loc.NoToken;

            Assert.False(string.IsNullOrWhiteSpace(msg));
            // 내부용 원문 sentinel 이 사용자에게 그대로 보이면 안 된다.
            Assert.NotEqual(UsageApiService.NoTokenError, msg);
            // 무엇을 해야 하는지(claude 로그인)를 담고 있어야 한다.
            Assert.Contains("claude", msg, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }

    // #198: Codex 로그인 만료 안내는 언어마다 따로 있어야 하고(영어로 떨어지지 않게), 어느 앱 얘기인지 담아야 한다.
    [Theory]
    [InlineData("ko")]
    [InlineData("zh")]
    [InlineData("ja")]
    [InlineData("en")]
    public void CodexSignInExpiredNotice_IsLocalized_AndNamesCodex(string lang)
    {
        var originalLang = Loc.CurrentLang;

        try
        {
            Loc.SetLanguage("en");
            var english = Loc.CodexSignInExpiredNotice;
            Loc.SetLanguage(lang);

            var msg = Loc.CodexSignInExpiredNotice;

            Assert.Contains("Codex", msg);
            if (lang != "en")
                Assert.NotEqual(english, msg);
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }

    // 일본어·중국어 화면에 한글이 섞이지 않는다 — CodexNoUsageToday(ja) 가 "本지의 …" 였던 회귀 방지(#198).
    // Loc 의 매개변수 없는 문자열 속성을 전부 훑으므로 새 문구도 자동으로 검사된다.
    [Theory]
    [InlineData("ja")]
    [InlineData("zh")]
    public void JapaneseAndChineseTexts_ContainNoHangul(string lang)
    {
        var originalLang = Loc.CurrentLang;

        try
        {
            Loc.SetLanguage(lang);

            var offenders = typeof(Loc)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                .Select(p => (p.Name, Text: (string?)p.GetValue(null) ?? ""))
                .Where(entry => Hangul.IsMatch(entry.Text))
                .Select(entry => $"{entry.Name}: {entry.Text}")
                .ToList();

            Assert.Empty(offenders);
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }

    // 완성형 음절뿐 아니라 자모(ㄱ·ㅏ 등 IME 실수로 섞이기 쉬운 글자)도 잡는다.
    private static readonly Regex Hangul = new("[ᄀ-ᇿ㄰-㆏가-힣]");

    // window_minutes 를 창 길이에 맞는 라벨로 변환한다(주간/5시간/일 단위).
    [Theory]
    [InlineData(10080, "ko", "주간 윈도우")]
    [InlineData(10080, "en", "Weekly window")]
    [InlineData(10080, "ja", "週間ウィンドウ")]
    [InlineData(300, "ko", "5시간 윈도우")]
    [InlineData(300, "en", "5-hour window")]
    [InlineData(1440, "ko", "1일 윈도우")]
    [InlineData(20160, "ko", "2주 윈도우")]
    public void CodexWindowLabel_FormatsByWindowLength(int minutes, string lang, string expected)
    {
        var originalLang = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage(lang);
            Assert.Equal(expected, Loc.CodexWindowLabel(minutes));
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }

    // 초기화 알림은 공급자 이름을 그대로 써야 한다 — 예전에는 Codex 초기화도 "Claude 할당량 초기화됨" 으로 나갔다.
    [Theory]
    [InlineData("ko", "Codex 할당량 초기화됨", "이제 다시 Codex를 사용할 수 있습니다!")]
    [InlineData("zh", "Codex 配额已重置", "现在可以再次使用 Codex 了！")]
    [InlineData("ja", "Codex クォータがリセットされました", "Codex を再び使用できるようになりました！")]
    [InlineData("en", "Codex Quota Reset", "You can use Codex again now!")]
    public void QuotaReset_UsesTheGivenAgentName(string lang, string expectedTitle, string expectedBody)
    {
        var originalLang = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage(lang);

            Assert.Equal(expectedTitle, Loc.QuotaResetTitle("Codex"));
            Assert.Equal(expectedBody, Loc.QuotaResetBody("Codex"));
            Assert.DoesNotContain("Claude", Loc.QuotaResetTitle("Codex"));
            Assert.DoesNotContain("Claude", Loc.QuotaResetBody("Codex"));
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }

    // Claude 의 초기화 알림 문구는 이번 변경 전과 한 글자도 달라지면 안 된다.
    [Theory]
    [InlineData("ko", "Claude 할당량 초기화됨", "이제 다시 Claude를 사용할 수 있습니다!")]
    [InlineData("en", "Claude Quota Reset", "You can use Claude again now!")]
    public void QuotaReset_ClaudeTextIsUnchanged(string lang, string expectedTitle, string expectedBody)
    {
        var originalLang = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage(lang);

            Assert.Equal(expectedTitle, Loc.QuotaResetTitle("Claude"));
            Assert.Equal(expectedBody, Loc.QuotaResetBody("Claude"));
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }

    // 창 길이를 모르면(null/0) 기존 "단기 윈도우"로 폴백한다.
    [Fact]
    public void CodexWindowLabel_FallsBackWhenUnknown()
    {
        var originalLang = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage("ko");
            Assert.Equal(Loc.ShortWindow, Loc.CodexWindowLabel(null));
            Assert.Equal(Loc.ShortWindow, Loc.CodexWindowLabel(0));
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }
}
