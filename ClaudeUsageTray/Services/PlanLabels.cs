using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 공급자별 구독 등급 배지 문구를 만든다("Claude Max 5x", "ChatGPT Plus" …).
///
/// 등급 값은 각 공급자가 로컬에 남긴 자격/사용량 데이터에서 그대로 온다. 여기서는 표시 형태만
/// 정하며, <b>모르는 값을 그럴듯한 등급으로 지어내지 않는다</b> — 알 수 없으면 빈 문자열을 돌려
/// 호출부가 배지를 숨기게 한다. 등급을 잘못 표시하면 사용자가 자기 요금제를 오해하게 되므로,
/// 아는 값만 다듬고 모르는 값은 원문을 보기 좋게만 바꿔 그대로 보여준다.
/// </summary>
public static class PlanLabels
{
    /// <summary>rateLimitTier 에 들어 있는 배수 표기("default_claude_max_5x" → "5x").</summary>
    private static readonly Regex MultiplierPattern =
        new(@"(?<value>\d+)x\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Claude 배지. <paramref name="subscriptionType"/> 는 ~/.claude/.credentials.json 의
    /// <c>claudeAiOauth.subscriptionType</c>("pro"·"max"·"free" …), <paramref name="rateLimitTier"/> 는
    /// 같은 파일의 <c>rateLimitTier</c>("default_claude_max_5x" …) 다. Max 는 5x/20x 로 한도가 4배
    /// 차이 나므로 배수까지 붙여야 등급 표시가 실제 한도와 맞는다.
    /// </summary>
    public static string Claude(string? subscriptionType, string? rateLimitTier)
    {
        var plan = Humanize(subscriptionType);
        if (plan.Length == 0) return "";

        var multiplier = ExtractMultiplier(rateLimitTier);
        return multiplier.Length > 0 && !plan.Contains(multiplier, StringComparison.OrdinalIgnoreCase)
            ? $"Claude {plan} {multiplier}"
            : $"Claude {plan}";
    }

    /// <summary>Codex 배지. rate_limits 의 <c>plan_type</c>("plus"·"pro"·"team" …) 을 받는다.</summary>
    public static string Codex(string? planType)
    {
        var plan = Humanize(planType);
        return plan.Length == 0 ? "" : $"ChatGPT {plan}";
    }

    /// <summary>
    /// OpenCode 배지. OpenCode 가 <c>auth.json</c> 에 남긴 자체 로그인 항목의 키("opencode-go") 를 받는다.
    /// 이 키는 사용자가 어떤 OpenCode 상품으로 로그인했는지를 그대로 가리킨다.
    /// </summary>
    public static string OpenCode(string? authProviderId)
    {
        if (string.IsNullOrWhiteSpace(authProviderId)) return "";

        var id = authProviderId.Trim();
        if (!id.StartsWith("opencode", StringComparison.OrdinalIgnoreCase)) return "";

        var suffix = Humanize(id["opencode".Length..].TrimStart('-', '_'));
        return suffix.Length == 0 ? "OpenCode" : $"OpenCode {suffix}";
    }

    /// <summary>"default_claude_max_5x" → "5x". 배수 표기가 없으면 빈 문자열.</summary>
    private static string ExtractMultiplier(string? rateLimitTier)
    {
        if (string.IsNullOrWhiteSpace(rateLimitTier)) return "";
        var match = MultiplierPattern.Match(rateLimitTier);
        return match.Success ? $"{match.Groups["value"].Value}x" : "";
    }

    /// <summary>"max" → "Max", "team_seat" → "Team Seat". 공백만 남는 값은 빈 문자열.</summary>
    private static string Humanize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var words = raw.Replace('_', ' ').Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(' ', words.Select(word =>
            char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..].ToLowerInvariant()));
    }
}
