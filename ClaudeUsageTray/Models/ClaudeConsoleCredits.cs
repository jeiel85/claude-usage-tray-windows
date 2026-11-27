namespace ClaudeUsageTray.Models;

/// <summary>
/// Claude Console(platform.claude.com) 개인 조직의 프로모션 크레딧 합계(#354) — Max 구독에 딸린 "API credit (Max)" 등.
/// 금액은 통화 최소 단위(<see cref="Exponent"/> = 2 면 5780 = $57.80). claude.ai 구독의 추가 사용(<c>extra_usage</c>)과는 다른 돈이다.
/// </summary>
public sealed record ClaudeConsoleCredits(
    long RemainingMinor,
    long GrantedMinor,
    int Exponent,
    string Currency,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>게이지 값(0..1) — 쓴 비율. 다른 게이지처럼 쓸수록 찬다.</summary>
    public double UsedFraction => GrantedMinor > 0
        ? Math.Clamp((GrantedMinor - RemainingMinor) / (double)GrantedMinor, 0, 1)
        : 0;
}

public enum ClaudeConsoleSessionState
{
    /// <summary>이 PC 에서 Console 을 연결한 적이 없다 — 아무것도 그리지 않는다.</summary>
    NotConnected,
    /// <summary>읽었다. 프로모션 크레딧이 없으면 <see cref="ClaudeConsoleReadResult.Credits"/> 가 null.</summary>
    Ok,
    /// <summary>Console 세션이 풀렸다 — 다시 연결해야 한다.</summary>
    SignedOut,
    /// <summary>응답을 못 받았거나 예상 밖의 응답이었다. 로그인 여부를 단정할 수 없다.</summary>
    Unavailable,
}

public sealed record ClaudeConsoleReadResult(ClaudeConsoleSessionState State, ClaudeConsoleCredits? Credits = null)
{
    public static ClaudeConsoleReadResult NotConnected { get; } = new(ClaudeConsoleSessionState.NotConnected);
    public static ClaudeConsoleReadResult SignedOut { get; } = new(ClaudeConsoleSessionState.SignedOut);
    public static ClaudeConsoleReadResult Unavailable { get; } = new(ClaudeConsoleSessionState.Unavailable);
    public static ClaudeConsoleReadResult Ok(ClaudeConsoleCredits? credits) => new(ClaudeConsoleSessionState.Ok, credits);
}
