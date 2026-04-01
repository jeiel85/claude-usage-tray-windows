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

    public static string CurrentLang => Lang;

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

    // Notifications
    public static string NotificationTitle => Lang switch
    {
        "ko" => "Claude 사용량 알림",
        "zh" => "Claude 用量提醒",
        "ja" => "Claude 使用量アラート",
        _ => "Claude Usage Alert"
    };

    public static string NotificationBody(int percent, string window, string resetLabel) => Lang switch
    {
        "ko" => $"{window}가 {percent}%에 도달했습니다{(resetLabel.Length > 0 ? " ·" + resetLabel : "")}",
        "zh" => $"{window} 已达到 {percent}%{(resetLabel.Length > 0 ? " ·" + resetLabel : "")}",
        "ja" => $"{window} が {percent}% に達しました{(resetLabel.Length > 0 ? " ·" + resetLabel : "")}",
        _ => $"{window} reached {percent}%{(resetLabel.Length > 0 ? " ·" + resetLabel : "")}"
    };

    public static string RateLimitTitle => Lang switch
    {
        "ko" => "Claude 레이트 리밋 도달",
        "zh" => "Claude 已达到速率限制",
        "ja" => "Claude レート制限に達しました",
        _ => "Claude Rate Limit Reached"
    };

    // Settings
    public static string Notifications => Lang switch
    {
        "ko" => "알림 설정",
        "zh" => "通知设置",
        "ja" => "通知設定",
        _ => "Notifications"
    };

    public static string NotificationsEnabled => Lang switch
    {
        "ko" => "알림 사용",
        "zh" => "启用通知",
        "ja" => "通知を有効にする",
        _ => "Enable notifications"
    };

    public static string NotifyRateLimit => Lang switch
    {
        "ko" => "레이트 리밋 알림",
        "zh" => "速率限制通知",
        "ja" => "レート制限通知",
        _ => "Rate limit alert"
    };

    public static string NtfyTitle => Lang switch
    {
        "ko" => "스마트폰 알림 (ntfy)",
        "zh" => "手机推送通知 (ntfy)",
        "ja" => "スマホ通知 (ntfy)",
        _ => "Phone Push Notifications (ntfy)"
    };

    public static string NtfyDesc => Lang switch
    {
        "ko" => "ntfy.sh는 무료 오픈소스 푸시 알림 서비스예요. 앱을 설치하고 토픽을 구독하면 Claude 사용량 알림을 스마트폰에서 바로 받을 수 있어요.",
        "zh" => "ntfy.sh 是免费的开源推送通知服务。安装应用并订阅主题后，即可在手机上接收 Claude 用量提醒。",
        "ja" => "ntfy.sh は無料のオープンソース Push 通知サービスです。アプリをインストールしてトピックを購読すると、Claude の使用量アラートをスマホで受け取れます。",
        _ => "ntfy.sh is a free, open-source push notification service. Install the app, subscribe to a topic, and receive Claude usage alerts directly on your phone."
    };

    public static string NtfyDownload => Lang switch
    {
        "ko" => "ntfy 앱 다운로드 (iOS · Android) →",
        "zh" => "下载 ntfy 应用（iOS · Android）→",
        "ja" => "ntfy アプリをダウンロード（iOS · Android）→",
        _ => "Download ntfy app (iOS · Android) →"
    };

    public static string NtfyStep2 => Lang switch
    {
        "ko" => "② 앱에서 + 버튼 → 아래 토픽 이름으로 구독",
        "zh" => "② 在应用中点击 + → 用下方主题名称订阅",
        "ja" => "② アプリで + ボタン → 下のトピック名で購読",
        _ => "② In the app tap + → subscribe with the topic below"
    };

    public static string NtfyStep3 => Lang switch
    {
        "ko" => "③ 아래 입력창에 토픽 이름 입력 후 Enter",
        "zh" => "③ 在下方输入主题名称后按 Enter",
        "ja" => "③ 下の入力欄にトピック名を入力して Enter",
        _ => "③ Enter the same topic name in the field below"
    };

    public static string NtfyTopic => Lang switch
    {
        "ko" => "토픽 이름",
        "zh" => "主题名称",
        "ja" => "トピック名",
        _ => "Topic name"
    };

    public static string NtfyPlaceholder => Lang switch
    {
        "ko" => "예: claude-usage-홍길동  (본인만 알 법한 이름 권장)",
        "zh" => "例: claude-usage-yourname（建议使用不易猜到的名称）",
        "ja" => "例: claude-usage-yourname（推測されにくい名前を推奨）",
        _ => "e.g. claude-usage-yourname  (use something unique)"
    };

    public static string ThresholdsLabel => Lang switch
    {
        "ko" => "5시간 윈도우 임계값",
        "zh" => "5小时窗口阈值",
        "ja" => "5時間ウィンドウ閾値",
        _ => "5-Hour window thresholds"
    };

    // Update
    public static string UpdateAvailable(string version) => Lang switch
    {
        "ko" => $"새 버전 {version} 업데이트 — 클릭하여 설치",
        "zh" => $"新版本 {version} 可用 — 点击安装",
        "ja" => $"新バージョン {version} が利用可能 — クリックしてインストール",
        _ => $"Update {version} available — click to install"
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

    public static string RateLimitedUntil(string time) => Lang switch
    {
        "ko" => $"API 제한 중 — {time} 이후 재시도",
        "zh" => $"API 受限中 — {time} 后重试",
        "ja" => $"API 制限中 — {time} 以降に再試行",
        _ => $"Rate limited — retry after {time}"
    };

    public static string HistoryTitle => Lang switch
    {
        "ko" => "7일 사용 추이",
        "zh" => "7天使用趋势",
        "ja" => "7日間使用推移",
        _ => "7-Day Usage Trend"
    };

    public static string HourlyChartTitle => Lang switch
    {
        "ko" => "오늘 시간대별 사용량",
        "zh" => "今日各时段用量",
        "ja" => "本日時間帯別使用量",
        _ => "Today's Hourly Usage"
    };

    public static string TestNotification => Lang switch
    {
        "ko" => "알림 테스트",
        "zh" => "测试通知",
        "ja" => "通知テスト",
        _ => "Test notification"
    };

    public static string TestNotificationBody => Lang switch
    {
        "ko" => "알림이 정상적으로 작동하고 있어요!",
        "zh" => "通知工作正常！",
        "ja" => "通知が正常に動作しています！",
        _ => "Notifications are working correctly!"
    };

    public static string TestNotificationHint => Lang switch
    {
        "ko" => "Windows 토스트 + 스마트폰 알림(ntfy 설정 시) 동시 테스트",
        "zh" => "同时测试 Windows 通知 + 手机推送（已设置 ntfy 时）",
        "ja" => "Windows トースト + スマホ通知（ntfy 設定済みの場合）を同時テスト",
        _ => "Tests Windows toast + phone push (if ntfy topic is set)"
    };

    public static string TestNotificationSent => Lang switch
    {
        "ko" => "✓ 전송됨 (Windows + 스마트폰)",
        "zh" => "✓ 已发送（Windows + 手机）",
        "ja" => "✓ 送信済み（Windows + スマホ）",
        _ => "✓ Sent (Windows + phone)"
    };

    public static string TestNotificationSentNoNtfy => Lang switch
    {
        "ko" => "✓ Windows 알림 전송됨 (ntfy 미설정)",
        "zh" => "✓ Windows 通知已发送（未设置 ntfy）",
        "ja" => "✓ Windows 通知送信済み（ntfy 未設定）",
        _ => "✓ Windows toast sent (ntfy not configured)"
    };

    public static string StartWithWindows => Lang switch
    {
        "ko" => "윈도우 시작 시 자동 실행",
        "zh" => "随 Windows 启动",
        "ja" => "Windows 起動時に自動起動",
        _ => "Start with Windows"
    };

    // Update dialog
    public static string UpdateDialogTitle => Lang switch
    {
        "ko" => "업데이트 사용 가능",
        "zh" => "有新版本可用",
        "ja" => "アップデートが利用可能",
        _ => "Update Available"
    };

    public static string WhatsNew => Lang switch
    {
        "ko" => "변경사항",
        "zh" => "更新内容",
        "ja" => "更新内容",
        _ => "What's New"
    };

    public static string UpdateNow => Lang switch
    {
        "ko" => "지금 업데이트",
        "zh" => "立即更新",
        "ja" => "今すぐ更新",
        _ => "Update Now"
    };

    public static string SkipThisVersion => Lang switch
    {
        "ko" => "이번 버전 건너뛰기",
        "zh" => "跳过此版本",
        "ja" => "このバージョンをスキップ",
        _ => "Skip This Version"
    };

    public static string Later => Lang switch
    {
        "ko" => "나중에",
        "zh" => "稍后",
        "ja" => "後で",
        _ => "Later"
    };

    public static string CheckUpdate => Lang switch
    {
        "ko" => "업데이트 확인",
        "zh" => "检查更新",
        "ja" => "アップデートを確認",
        _ => "Check for updates"
    };

    public static string CheckingUpdate => Lang switch
    {
        "ko" => "업데이트 확인 중...",
        "zh" => "检查更新中...",
        "ja" => "アップデートを確認中...",
        _ => "Checking for updates..."
    };

    public static string DownloadingUpdate => Lang switch
    {
        "ko" => "다운로드 중...",
        "zh" => "下载中...",
        "ja" => "ダウンロード中...",
        _ => "Downloading..."
    };

    public static string AlreadyUpToDate => Lang switch
    {
        "ko" => "✓ 최신 버전입니다",
        "zh" => "✓ 已是最新版本",
        "ja" => "✓ 最新バージョンです",
        _ => "✓ Already up to date"
    };

    public static string DepletionAt(string time) => Lang switch
    {
        "ko" => $"⚡ 추세대로면 {time}경 소진",
        "zh" => $"⚡ 按当前趋势 {time} 耗尽",
        "ja" => $"⚡ このペースなら {time} 頃に枯渇",
        _ => $"⚡ at this rate, depletes ~{time}"
    };

    public static string CostEstimate(double usd) => Lang switch
    {
        "ko" => $"≈ ${usd:F3} (Sonnet 기준 참고값)",
        "zh" => $"≈ ${usd:F3}（Sonnet 参考价格）",
        "ja" => $"≈ ${usd:F3}（Sonnet 基準の参考値）",
        _ => $"≈ ${usd:F3} (Sonnet API reference)"
    };

    public static string ExtraUsageTitle => Lang switch
    {
        "ko" => "추가 구매 사용량",
        "zh" => "额外购买用量",
        "ja" => "追加購入使用量",
        _ => "Extra Usage"
    };

    public static string ExtraCredits(double used, double limit) => Lang switch
    {
        "ko" => $"{used:N0} / {limit:N0} 크레딧",
        "zh" => $"{used:N0} / {limit:N0} 积分",
        "ja" => $"{used:N0} / {limit:N0} クレジット",
        _ => $"{used:N0} / {limit:N0} credits"
    };

    public static string ApiError(string msg) => Lang switch
    {
        "ko" => $"API 오류: {msg}",
        "zh" => $"API 错误: {msg}",
        "ja" => $"API エラー: {msg}",
        _ => $"API error: {msg}"
    };

    // Accounts
    public static string AccountsTitle => Lang switch
    {
        "ko" => "계정 관리",
        "zh" => "账号管理",
        "ja" => "アカウント管理",
        _ => "Accounts"
    };

    public static string AccountAdd => Lang switch
    {
        "ko" => "+ 추가",
        "zh" => "+ 添加",
        "ja" => "+ 追加",
        _ => "+ Add"
    };

    public static string AccountNamePlaceholder => Lang switch
    {
        "ko" => "계정 이름 (예: 업무용)",
        "zh" => "账号名称（如: 工作）",
        "ja" => "アカウント名（例: 仕事用）",
        _ => "Account name (e.g. Work)"
    };

    public static string AccountHint => Lang switch
    {
        "ko" => "이름 입력 후 + 추가 클릭 → Claude 설정 폴더 선택 (기본: ~/.claude)",
        "zh" => "输入名称后点击 + 添加 → 选择 Claude 配置文件夹（默认: ~/.claude）",
        "ja" => "名前を入力して + 追加をクリック → Claude 設定フォルダを選択（既定: ~/.claude）",
        _ => "Enter name and click + Add → select Claude config folder (default: ~/.claude)"
    };
}
