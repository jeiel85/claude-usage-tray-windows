using System.Globalization;

namespace ClaudeUsageTray.Services;

public static class Loc
{
    private static readonly string Lang;

    static Loc()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        Lang = culture switch
        {
            "ko" => "ko",
            "zh" => "zh",
            "ja" => "ja",
            _ => "en"
        };
    }

    // Header
    public static string AppTitle => Lang switch
    {
        "ko" => "Claude 사용량",
        "zh" => "Claude 用量",
        "ja" => "Claude 使用量",
        _ => "Claude Usage"
    };

    public static string Updated => Lang switch
    {
        "ko" => "업데이트",
        "zh" => "已更新",
        "ja" => "更新",
        _ => "Updated"
    };

    public static string Refreshing => Lang switch
    {
        "ko" => "새로고침 중...",
        "zh" => "刷新中...",
        "ja" => "更新中...",
        _ => "Refreshing..."
    };

    // Sections
    public static string ApiQuota => Lang switch
    {
        "ko" => "API 할당량",
        "zh" => "API 配额",
        "ja" => "API クォータ",
        _ => "API Quota"
    };

    public static string TodayTokens => Lang switch
    {
        "ko" => "오늘의 토큰",
        "zh" => "今日令牌",
        "ja" => "本日のトークン",
        _ => "Today's Tokens"
    };

    // Windows
    public static string FiveHourWindow => Lang switch
    {
        "ko" => "5시간 윈도우",
        "zh" => "5小时窗口",
        "ja" => "5時間ウィンドウ",
        _ => "5-Hour Window"
    };

    public static string SevenDayWindow => Lang switch
    {
        "ko" => "7일 윈도우",
        "zh" => "7天窗口",
        "ja" => "7日間ウィンドウ",
        _ => "7-Day Window"
    };

    // Token labels
    public static string Input => Lang switch
    {
        "ko" => "입력",
        "zh" => "输入",
        "ja" => "入力",
        _ => "Input"
    };

    public static string Output => Lang switch
    {
        "ko" => "출력",
        "zh" => "输出",
        "ja" => "出力",
        _ => "Output"
    };

    public static string CacheRead => Lang switch
    {
        "ko" => "캐시 읽기",
        "zh" => "缓存读取",
        "ja" => "キャッシュ読み取り",
        _ => "Cache Read"
    };

    public static string CacheWrite => Lang switch
    {
        "ko" => "캐시 쓰기",
        "zh" => "缓存写入",
        "ja" => "キャッシュ書き込み",
        _ => "Cache Write"
    };

    public static string Tokens => Lang switch
    {
        "ko" => "토큰",
        "zh" => "令牌",
        "ja" => "トークン",
        _ => "tokens"
    };

    // Buttons
    public static string Refresh => Lang switch
    {
        "ko" => "새로고침",
        "zh" => "刷新",
        "ja" => "更新",
        _ => "Refresh"
    };

    public static string Quit => Lang switch
    {
        "ko" => "종료",
        "zh" => "退出",
        "ja" => "終了",
        _ => "Quit"
    };

    // Footer
    public static string Sessions(int count) => Lang switch
    {
        "ko" => $"오늘 {count}개 세션",
        "zh" => $"今日 {count} 个会话",
        "ja" => $"本日 {count} セッション",
        _ => $"{count} session(s) today"
    };

    public static string ResetsIn(string time) => Lang switch
    {
        "ko" => $" · {time} 후 초기화",
        "zh" => $" · {time} 后重置",
        "ja" => $" · {time} 後リセット",
        _ => $" · resets {time}"
    };

    public static string UpdatedAt(string time) => Lang switch
    {
        "ko" => $"업데이트 {time}",
        "zh" => $"已更新 {time}",
        "ja" => $"更新 {time}",
        _ => $"Updated {time}"
    };

    // Errors
    public static string NoToken => Lang switch
    {
        "ko" => "인증 토큰 없음. Claude Code 로그인 필요",
        "zh" => "无访问令牌，请登录 Claude Code",
        "ja" => "アクセストークンがありません。Claude Code にログインしてください",
        _ => "No access token. Please log in to Claude Code"
    };

    public static string RateLimited => Lang switch
    {
        "ko" => "레이트 리밋 도달 — 잠시 후 자동 갱신됩니다",
        "zh" => "已达到速率限制 — 稍后自动刷新",
        "ja" => "レート制限に達しました — まもなく自動更新",
        _ => "Rate limited — will auto-refresh shortly"
    };

    public static string ApiError(string msg) => Lang switch
    {
        "ko" => $"API 오류: {msg}",
        "zh" => $"API 错误: {msg}",
        "ja" => $"API エラー: {msg}",
        _ => $"API error: {msg}"
    };
}
