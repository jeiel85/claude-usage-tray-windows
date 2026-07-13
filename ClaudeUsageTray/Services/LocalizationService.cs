using System.Globalization;

namespace ClaudeUsageTray.Services;

public static class Loc
{
    private static string Lang;

    public static event Action? LanguageChanged;

    static Loc()
    {
        Lang = DetectSystemLang();
    }

    private static string DetectSystemLang()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return culture switch { "ko" => "ko", "zh" => "zh", "ja" => "ja", _ => "en" };
    }

    public static void SetLanguage(string langCode)
    {
        Lang = langCode == "system" ? DetectSystemLang() : langCode;
        LanguageChanged?.Invoke();
    }

    public static string CurrentLang => Lang;

    public static string AgentUsageTitle => Lang switch
    {
        "ko" => "AI 에이전트 사용량",
        "zh" => "AI 代理使用量",
        "ja" => "AI エージェント使用量",
        _ => "AI Agent Usage"
    };

    public static string ClaudeUsageTitle => Lang switch
    {
        "ko" => "Claude 사용량",
        "zh" => "Claude 用量",
        "ja" => "Claude 使用量",
        _ => "Claude Usage"
    };

    public static string CodexUsageTitle => Lang switch
    {
        "ko" => "Codex 사용량",
        "zh" => "Codex 用量",
        "ja" => "Codex 使用量",
        _ => "Codex Usage"
    };

    public static string GeminiCliUsageTitle => Lang switch
    {
        "ko" => "Gemini CLI 사용량",
        "zh" => "Gemini CLI 用量",
        "ja" => "Gemini CLI 使用量",
        _ => "Gemini CLI Usage"
    };

    public static string ProviderSection => Lang switch
    {
        "ko" => "차트 표시 기준",
        "zh" => "图表显示基准",
        "ja" => "チャート表示基準",
        _ => "Chart Display Base"
    };

    public static string ProviderClaude => Lang switch
    {
        "ko" => "Claude",
        "zh" => "Claude",
        "ja" => "Claude",
        _ => "Claude"
    };

    public static string ProviderCodex => Lang switch
    {
        "ko" => "Codex (ChatGPT plan)",
        "zh" => "Codex (ChatGPT plan)",
        "ja" => "Codex (ChatGPT plan)",
        _ => "Codex (ChatGPT plan)"
    };

    public static string ProviderGeminiCli => Lang switch
    {
        "ko" => "Gemini CLI",
        "zh" => "Gemini CLI",
        "ja" => "Gemini CLI",
        _ => "Gemini CLI"
    };

    public static string ProviderCodexNote => Lang switch
    {
        "ko" => "ChatGPT plan 기준 · 로컬 Codex 세션 데이터",
        "zh" => "基于 ChatGPT plan · 本地 Codex 会话数据",
        "ja" => "ChatGPT plan 基準 · ローカル Codex セッションデータ",
        _ => "Based on ChatGPT plan · local Codex session data"
    };

    public static string ProviderGeminiCliNote => Lang switch
    {
        "ko" => "Gemini CLI 로컬 기준 · 공식 수치 아님",
        "zh" => "Gemini CLI 本地基准 · 非官方数据",
        "ja" => "Gemini CLI ローカル基準 · 公式数値ではありません",
        _ => "Gemini CLI local logs · not official quota"
    };

    // Header
    public static string AppTitle => ClaudeUsageTitle;

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

    public static string UsageQuota => Lang switch
    {
        "ko" => "사용량 할당량",
        "zh" => "用量配额",
        "ja" => "使用量クォータ",
        _ => "Usage Quota"
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

    // v1.26.0: Codex 의 primary/secondary 처럼 윈도우 의미가 플랜별로 가변인 경우용 라벨.
    // Anthropic 의 5h/7d 처럼 시간 기반으로 단정할 수 없는 케이스에 사용.
    public static string ShortWindow => Lang switch
    {
        "ko" => "단기 윈도우",
        "zh" => "短期窗口",
        "ja" => "短期ウィンドウ",
        _ => "Short window"
    };

    public static string LongWindow => Lang switch
    {
        "ko" => "장기 윈도우",
        "zh" => "长期窗口",
        "ja" => "長期ウィンドウ",
        _ => "Long window"
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

    public static string InputTooltip => Lang switch
    {
        "ko" => "사용자가 보낸 질문, 코드, 지시사항 등 모델에 전달된 텍스트량입니다.",
        "zh" => "用户发送的问题、代码、指令等传递给模型的内容分量。",
        "ja" => "ユーザーが送信した質問、コード、指示などモデルに渡されたテキスト量です。",
        _ => "The amount of text sent to the model, such as questions, code, and instructions."
    };

    public static string OutputTooltip => Lang switch
    {
        "ko" => "사용자의 질문에 대해 Claude 모델이 생성하여 답변한 텍스트량입니다.",
        "zh" => "Claude 模型针对用户问题生成并回答的内容分量。",
        "ja" => "ユーザーの質問に対して Claude モデルが生成して回答したテキスト量です。",
        _ => "The amount of text generated by the Claude model in response to the user's question."
    };

    public static string CacheReadTooltip => Lang switch
    {
        "ko" => "이전 대화 내용이 서버에 저장되어 있어, 다시 계산하지 않고 재사용된 분량입니다. (비용과 시간이 절약됩니다.)",
        "zh" => "以前的对话内容已存储在服务器中，无需重新计算即可重用的分量。（节省成本和时间）",
        "ja" => "以前の会話内容がサーバーに保存されており、再計算せずに再利用された分量です。（コストと時間の節約になります）",
        _ => "Content from previous conversations stored on the server and reused without re-computation. (Saves time and cost)"
    };

    public static string CacheWriteTooltip => Lang switch
    {
        "ko" => "다음에 질문할 때 재사용할 수 있도록 현재 질문 내용의 일부를 서버 캐시에 새롭게 저장한 분량입니다.",
        "zh" => "将当前提问内容的一部分新存储到服务器缓存中，以便下次提问时重用。",
        "ja" => "次回質問時に再利用できるよう、現在の質問内容の一部をサーバーキャッシュに新しく保存した分量です。",
        _ => "Content from the current request newly stored in the server cache for reuse in future requests."
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

    public static string ResetsInEstimated(string time) => Lang switch
    {
        "ko" => $" · 약 {time} 후 초기화(예상)",
        "zh" => $" · 约 {time} 后重置（估算）",
        "ja" => $" · 約 {time} 後リセット（推定）",
        _ => $" · resets ~{time} (estimated)"
    };

    public static string UsageSummary(double usedPercent) => Lang switch
    {
        "ko" => $"{Math.Clamp(usedPercent, 0, 1):P0} 사용 · 잔량 {(1.0 - Math.Clamp(usedPercent, 0, 1)):P0}",
        "zh" => $"已用 {Math.Clamp(usedPercent, 0, 1):P0} · 剩余 {(1.0 - Math.Clamp(usedPercent, 0, 1)):P0}",
        "ja" => $"{Math.Clamp(usedPercent, 0, 1):P0} 使用 · 残り {(1.0 - Math.Clamp(usedPercent, 0, 1)):P0}",
        _ => $"{Math.Clamp(usedPercent, 0, 1):P0} used · {(1.0 - Math.Clamp(usedPercent, 0, 1)):P0} remaining"
    };

    // 시간 진행률 마커 툴팁 — 시간 경과 vs 사용량을 나란히 보여주고, 색(주황=초과)과 일치하는 페이스 판정을 덧붙인다.
    // settled=false(윈도우 초반, 통계적으로 무의미한 구간)면 빠름/여유 판정을 유보하고 "측정 중"으로 표시한다.
    public static string PaceTip(double timePercent, double usagePercent, bool settled = true)
    {
        double t = Math.Clamp(timePercent, 0, 1);
        double u = Math.Clamp(usagePercent, 0, 1);
        if (!settled)
        {
            return Lang switch
            {
                "ko" => $"시간 {t:P0} 경과 · 사용 {u:P0} · 페이스 측정 중",
                "zh" => $"时间 {t:P0} · 已用 {u:P0} · 计算节奏中",
                "ja" => $"経過 {t:P0} · 使用 {u:P0} · ペース測定中",
                _ => $"Time {t:P0} · Used {u:P0} · measuring pace"
            };
        }
        int diffPts = (int)Math.Round(Math.Abs(u - t) * 100);
        bool ahead = u - t > 0.005;
        bool behind = t - u > 0.005;
        return Lang switch
        {
            "ko" => $"시간 {t:P0} 경과 · 사용 {u:P0}" + (ahead ? $" · {diffPts}%p 빠름" : behind ? $" · {diffPts}%p 여유" : " · 적정 페이스"),
            "zh" => $"时间 {t:P0} · 已用 {u:P0}" + (ahead ? $" · 快 {diffPts}%p" : behind ? $" · 慢 {diffPts}%p" : " · 节奏适中"),
            "ja" => $"経過 {t:P0} · 使用 {u:P0}" + (ahead ? $" · {diffPts}%p 速い" : behind ? $" · {diffPts}%p 余裕" : " · 適正ペース"),
            _ => $"Time {t:P0} · Used {u:P0}" + (ahead ? $" · {diffPts}pp ahead" : behind ? $" · {diffPts}pp behind" : " · on pace")
        };
    }

    public static string UpdatedAt(string time) => Lang switch
    {
        "ko" => $"업데이트 {time}",
        "zh" => $"已更新 {time}",
        "ja" => $"更新 {time}",
        _ => $"Updated {time}"
    };

    public static string Usage => Lang switch
    {
        "ko" => "사용량",
        "zh" => "用量",
        "ja" => "使用量",
        _ => "Usage"
    };

    // Notifications
    public static string NotificationTitle => Lang switch
    {
        "ko" => "에이전트 사용량 알림",
        "zh" => "智能体用量提醒",
        "ja" => "エージェント使用量アラート",
        _ => "Agent Usage Alert"
    };

    public static string NotificationBody(int percent, string window, string resetLabel, string agent = "") => Lang switch
    {
        "ko" => $"{(agent.Length > 0 ? $"[{agent}] " : "")}{window}{(window.EndsWith("량") ? "이" : "가")} {percent}%에 도달했습니다{(resetLabel.Length > 0 ? " ·" + resetLabel : "")}",
        "zh" => $"{(agent.Length > 0 ? $"[{agent}] " : "")}{window} 已达到 {percent}%{(resetLabel.Length > 0 ? " ·" + resetLabel : "")}",
        "ja" => $"{(agent.Length > 0 ? $"[{agent}] " : "")}{window} が {percent}% に達しました{(resetLabel.Length > 0 ? " ·" + resetLabel : "")}",
        _ => $"{(agent.Length > 0 ? $"[{agent}] " : "")}{window} reached {percent}%{(resetLabel.Length > 0 ? " ·" + resetLabel : "")}"
    };

    public static string RateLimitTitle => Lang switch
    {
        "ko" => "Claude 실시간 사용 제한 발생",
        "zh" => "Claude 已达到速率限制",
        "ja" => "Claude レート制限に達しました",
        _ => "Claude Rate Limit Reached"
    };

    public static string EarlyExhaustionTitle => Lang switch
    {
        "ko" => "Claude 조기 소진 경고",
        "zh" => "Claude 提前耗尽警告",
        "ja" => "Claude 早期枯渇警告",
        _ => "Claude Early Exhaustion"
    };

    public static string EarlyExhaustionBody(string depletionTime, string resetTime) => Lang switch
    {
        "ko" => $"예상보다 빠른 소진 속도입니다. 이대로면 {depletionTime}경 소진 예상 (원래 초기화: {resetTime})",
        "zh" => $"消耗速度超出预期。按此速度将于 {depletionTime} 耗尽（原重置时间：{resetTime}）",
        "ja" => $"予想より早い枯渇ペースです。このままでは {depletionTime} 頃に枯渇する見込み（本来のリセット：{resetTime}）",
        _ => $"Depleting faster than expected. At this rate will exhaust by {depletionTime} (original reset: {resetTime})"
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
        "ko" => "실시간 사용 제한 알림",
        "zh" => "速率限制通知",
        "ja" => "レート制限通知",
        _ => "Rate limit alert"
    };

    public static string NotifyQuotaReset => Lang switch
    {
        "ko" => "할당량 초기화 시 알림",
        "zh" => "配额重置通知",
        "ja" => "クォータのリセット通知",
        _ => "Quota reset alert"
    };

    public static string QuotaResetTitle => Lang switch
    {
        "ko" => "Claude 할당량 초기화됨",
        "zh" => "Claude 配额已重置",
        "ja" => "Claude クォータがリセットされました",
        _ => "Claude Quota Reset"
    };

    public static string QuotaResetBody => Lang switch
    {
        "ko" => "이제 다시 Claude를 사용할 수 있습니다!",
        "zh" => "现在可以再次使用 Claude 了！",
        "ja" => "Claude を再び使用できるようになりました！",
        _ => "You can use Claude again now!"
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
        "ko" => "ntfy.sh는 무료 오픈소스 푸시 알림 서비스예요. 앱을 설치하고 토픽을 구독하면 에이전트 사용량 알림을 스마트폰에서 바로 받을 수 있어요.",
        "zh" => "ntfy.sh 是免费的开源推送通知服务。安装应用并订阅主题后，即可在手机上接收智能体用量提醒。",
        "ja" => "ntfy.sh は無料のオープンソース Push 通知サービスです。アプリをインストールしてトピックを購読すると、エージェントの使用量アラートをスマホで受け取れます。",
        _ => "ntfy.sh is a free, open-source push notification service. Install the app, subscribe to a topic, and receive agent usage alerts directly on your phone."
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

    public static string NtfySecurityWarning => Lang switch
    {
        "ko" => "⚠️ 예측 불가능한 긴 이름(20자 이상)을 사용하세요. 짧거나 익숙한 이름은 제3자가 메시지를 보낼 수 있습니다.",
        "zh" => "⚠️ 请使用难以预测的长名称（20字以上）。短名称或常见名称可能被他人用来发送消息。",
        "ja" => "⚠️ 予測困難な長い名前（20文字以上）を使用してください。短い名前は第三者がメッセージを送る可能性があります。",
        _ => "⚠️ Use a long, unpredictable name (20+ chars). Short or guessable names can be used by others to send fake alerts."
    };

    public static string NtfyTopicTooShort => Lang switch
    {
        "ko" => "토픽 이름은 20자 이상이어야 합니다.",
        "zh" => "主题名称必须至少20个字。",
        "ja" => "トピック名は20文字以上にしてください。",
        _ => "Topic name must be at least 20 characters."
    };

    public static string NtfyTopicInvalidChars => Lang switch
    {
        "ko" => "토픽 이름에는 영문 소문자, 숫자, dash(-), underscore(_), @, 점(.)만 사용할 수 있습니다.",
        "zh" => "主题名称只能使用小写字母、数字、破折号(-)、下划线(_)、@和点(.)。",
        "ja" => "トピック名には小文字、数字、dash(-)、underscore(_)、@、点(.)のみ使用できます。",
        _ => "Topic name can only contain lowercase letters, numbers, dash(-), underscore(_), @, and dot(.)"
    };

    public static string NtfySendFromThisPc => Lang switch
    {
        "ko" => "이 PC에서 ntfy 알림 발송",
        "zh" => "从此电脑发送 ntfy 通知",
        "ja" => "このPCからntfy通知を送信",
        _ => "Send ntfy notifications from this PC"
    };

    public static string NtfySendFromThisPcHint => Lang switch
    {
        "ko" => "여러 PC에서 같은 토픽 사용 시, 한 PC에서만 활성화하세요",
        "zh" => "多台电脑使用相同主题时，只在一台上启用",
        "ja" => "複数のPCで同じトピックを使う場合、1台のみ有効にしてください",
        _ => "If multiple PCs share the same topic, enable only on one"
    };

    // v1.27.0 표시 옵션 토글
    public static string DisplayOptionsSection => Lang switch
    {
        "ko" => "표시 옵션",
        "zh" => "显示选项",
        "ja" => "表示オプション",
        _ => "Display options"
    };

    public static string ShowCodexPlanBadge => Lang switch
    {
        "ko" => "Codex 플랜 배지 표시 (예: ChatGPT Plus)",
        "zh" => "显示 Codex 套餐徽章 (例: ChatGPT Plus)",
        "ja" => "Codex プランバッジを表示 (例: ChatGPT Plus)",
        _ => "Show Codex plan badge (e.g. ChatGPT Plus)"
    };

    public static string ShowAbsoluteResetTime => Lang switch
    {
        "ko" => "리셋 라벨에 절대 시각 병기",
        "zh" => "在重置标签中并列显示具体时间",
        "ja" => "リセットラベルに絶対時刻を併記",
        _ => "Show absolute reset time alongside countdown"
    };

    public static string ShowAbsoluteResetTimeHint => Lang switch
    {
        "ko" => "예: \"1h 23m 후 리셋 (18:30)\"",
        "zh" => "示例: \"1h 23m 后重置 (18:30)\"",
        "ja" => "例: \"1h 23m 後にリセット (18:30)\"",
        _ => "e.g. \"resets in 1h 23m (18:30)\""
    };

    public static string KeepPopupAboveTaskbar => Lang switch
    {
        "ko" => "작업표시줄 위에 팝업 고정",
        "zh" => "将弹窗固定在任务栏上方",
        "ja" => "ポップアップをタスクバー上に固定",
        _ => "Keep popup above the taskbar"
    };

    public static string KeepPopupAboveTaskbarHint => Lang switch
    {
        "ko" => "포커스를 잃어도 닫히지 않고 살짝 투명하게 유지",
        "zh" => "失去焦点也不会关闭，并保持轻微透明",
        "ja" => "フォーカスを失っても閉じず、少し透過した状態を保つ",
        _ => "Stays open when focus is lost and remains slightly translucent"
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
        "ko" => $"새 버전 {version} 업데이트",
        "zh" => $"新版本 {version} 可用",
        "ja" => $"新バージョン {version} が利用可能",
        _ => $"Update {version} available"
    };

    // Errors
    // 액세스 토큰 파일(.credentials.json 의 claudeAiOauth)이 없어 usage API 호출 전에 실패한 경우.
    // 데스크톱 앱만 쓰거나 새 PC라 CLI 로그인을 한 적 없는 환경에서 발생 — 재로그인이 유일한 해결책이라
    // 막연한 에러 대신 구체적 조치(터미널에서 claude 로그인)를 안내한다.
    public static string NoToken => Lang switch
    {
        "ko" => "액세스 토큰이 없습니다 — 터미널에서 Claude Code에 로그인(claude → /login)한 뒤 앱을 다시 시작하세요",
        "zh" => "未找到访问令牌 — 请在终端登录 Claude Code(claude → /login)后重启本应用",
        "ja" => "アクセストークンがありません — ターミナルで Claude Code にログイン(claude → /login)してからアプリを再起動してください",
        _ => "No access token — log in to Claude Code in a terminal (claude → /login), then restart the app"
    };

    public static string RateLimited => Lang switch
    {
        "ko" => "일시적으로 사용량이 많아 제한되었습니다 — 잠시 후 갱신됩니다",
        "zh" => "已达到速率限制 — 稍后自动刷新",
        "ja" => "レート制限に達しました — まもなく自動更新",
        _ => "Rate limited — will auto-refresh shortly"
    };

    public static string RateLimitedUntil(string time) => Lang switch
    {
        "ko" => $"사용 제한 중 — {time} 이후 다시 가능",
        "zh" => $"API 受限中 — {time} 后重试",
        "ja" => $"API 制限中 — {time} 以降に再試行",
        _ => $"Rate limited — retry after {time}"
    };

    // 쿨다운 안내(에러 아님): 회색 톤으로 자동 재시도 시점만 부드럽게 알려준다
    public static string ApiCooldownNote(string time) => Lang switch
    {
        "ko" => $"API 응답 대기 중 — {time}에 자동 재시도",
        "zh" => $"等待 API 响应 — 将在 {time} 自动重试",
        "ja" => $"API 応答待ち — {time} に自動再試行",
        _ => $"Waiting for API — auto retry at {time}"
    };

    // 403 permission_error 안내: 토큰 스코프 부족이라 자동 회복 불가 — 사용자 액션 유도
    public static string ApiPermissionDeniedNote => Lang switch
    {
        "ko" => "이 계정의 OAuth 토큰에는 사용량 API 권한이 없습니다 — 5h/7d 게이지는 표시되지 않으며, 로컬 토큰 집계는 정상 동작합니다.",
        "zh" => "当前账号的 OAuth 令牌没有访问用量 API 的权限 — 无法显示 5h/7d 进度条，但本地用量统计仍正常工作。",
        "ja" => "このアカウントの OAuth トークンには使用量 API へのアクセス権限がありません — 5h/7d ゲージは表示されませんが、ローカル使用量集計は正常に動作します。",
        _ => "This account's OAuth token lacks permission for the usage API — 5h/7d gauges will not appear, but local token aggregation continues to work."
    };

    // 403 + "currently not allowed" 패턴: 일시적 차단 (신규 계정 검증/조직 OAuth API 미활성)
    // — 영구 차단 아니라 24시간 내 자동 해소될 가능성 높다는 뉘앙스 전달
    public static string ApiOAuthNotAllowedNote => Lang switch
    {
        "ko" => "조직(Organization) OAuth API 가 아직 활성화되지 않았습니다 — 신규 계정/플랜 검증 진행 중일 수 있습니다. 24시간 후에도 동일하면 워크스페이스 설정 또는 Anthropic 지원 문의를 권장합니다. (로컬 토큰 집계는 정상)",
        "zh" => "组织 OAuth API 尚未激活 — 可能是新账号/订阅验证进行中。如 24 小时后仍未恢复，建议检查工作区设置或联系 Anthropic 支持。（本地用量统计正常）",
        "ja" => "組織の OAuth API が未有効化です — 新規アカウント/プラン認証中の可能性があります。24時間経っても解消されない場合はワークスペース設定または Anthropic サポートへ。（ローカル使用量集計は正常）",
        _ => "OAuth API for this organization isn't active yet — likely a new-account or plan-verification gate. If it persists past 24 hours, check workspace settings or contact Anthropic support. (Local token aggregation continues to work.)"
    };

    // 403 + "currently not allowed" 가 24h 이상 지속 — 신규 계정 유예 시간을 넘어섰으니
    // 자동 해소를 더는 기대하지 말고 명확히 사용자 액션을 유도한다.
    // {elapsedLabel} = 첫 감지 이후 경과 시간 라벨 (예: "27시간", "2일")
    public static string ApiOAuthNotAllowedEscalatedNote(string elapsedLabel) => Lang switch
    {
        "ko" => $"조직(Organization) OAuth API 미활성 상태가 {elapsedLabel}째 지속 중입니다 — 신규 계정 유예 시간을 넘어섰으니 console.anthropic.com 에서 워크스페이스 설정을 확인하거나 Anthropic 지원에 문의하세요. (로컬 토큰 집계는 정상)",
        "zh" => $"组织 OAuth API 未激活已持续 {elapsedLabel} — 已超过新账号宽限期，请到 console.anthropic.com 检查工作区设置或联系 Anthropic 支持。（本地用量统计正常）",
        "ja" => $"組織 OAuth API 未有効化の状態が {elapsedLabel} 続いています — 新規アカウントの猶予を超過しているため console.anthropic.com でワークスペース設定を確認するか Anthropic サポートへご連絡ください。（ローカル使用量集計は正常）",
        _ => $"Organization OAuth API has been inactive for {elapsedLabel} — past the new-account grace window. Review your workspace settings at console.anthropic.com or contact Anthropic support. (Local token aggregation continues to work.)"
    };

    // 경과 시간 라벨 — "27시간" / "2일" 식으로 자연스러운 단위
    public static string ElapsedDurationLabel(TimeSpan span)
    {
        var totalDays = (int)span.TotalDays;
        var totalHours = (int)span.TotalHours;
        if (totalDays >= 2)
        {
            return Lang switch
            {
                "ko" => $"{totalDays}일",
                "zh" => $"{totalDays}天",
                "ja" => $"{totalDays}日",
                _ => $"{totalDays} days"
            };
        }
        return Lang switch
        {
            "ko" => $"{totalHours}시간",
            "zh" => $"{totalHours}小时",
            "ja" => $"{totalHours}時間",
            _ => totalHours == 1 ? "1 hour" : $"{totalHours} hours"
        };
    }

    public static string HistoryTitleFor(string provider) => Lang switch
    {
        "ko" => $"{provider} {HistoryTitle}",
        "zh" => $"{provider} {HistoryTitle}",
        "ja" => $"{provider} {HistoryTitle}",
        _ => $"{provider} {HistoryTitle}"
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

    public static string TestNotificationSentNtfyDisabled => Lang switch
    {
        "ko" => "✓ Windows 알림 전송됨 (이 PC의 ntfy 발송 꺼짐)",
        "zh" => "✓ Windows 通知已发送（此电脑的 ntfy 发送已关闭）",
        "ja" => "✓ Windows 通知送信済み（このPCの ntfy 送信はオフ）",
        _ => "✓ Windows toast sent (ntfy sending is off on this PC)"
    };

    public static string TestNotificationFailedNtfy => Lang switch
    {
        "ko" => "⚠ Windows 알림 전송됨, ntfy 전송 실패",
        "zh" => "⚠ Windows 通知已发送，ntfy 发送失败",
        "ja" => "⚠ Windows 通知送信済み、ntfy 送信失敗",
        _ => "⚠ Windows toast sent, ntfy failed"
    };

    public static string NtfyTestSendFailed => Lang switch
    {
        "ko" => "ntfy 테스트 알림 전송에 실패했습니다.",
        "zh" => "ntfy 测试通知发送失败。",
        "ja" => "ntfy テスト通知の送信に失敗しました。",
        _ => "Failed to send ntfy test notification."
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
        "ko" => "✓ 최신버전",
        "zh" => "✓ 最新版",
        "ja" => "✓ 最新版",
        _ => "✓ Latest"
    };

    public static string UpdateCheckFailed => Lang switch
    {
        "ko" => "업데이트 확인 실패",
        "zh" => "检查失败",
        "ja" => "確認失敗",
        _ => "Check failed"
    };

    /// <summary>GitHub API 무인증 60/h rate limit 도달 — 사용자에게 재시도 시각과 직접 다운로드 옵션 안내.</summary>
    public static string UpdateCheckRateLimited(string retryAt) => Lang switch
    {
        "ko" => string.IsNullOrEmpty(retryAt)
            ? "GitHub API 한도 초과 — 잠시 후 재시도하거나 GitHub Releases 페이지에서 직접 받기"
            : $"GitHub API 한도 초과 ({retryAt}에 재시도 가능) — 또는 Releases 페이지에서 직접 받기",
        "zh" => string.IsNullOrEmpty(retryAt)
            ? "GitHub API 限额已用完 — 请稍后重试或前往 Releases 页面直接下载"
            : $"GitHub API 限额已用完（{retryAt} 后可重试）— 或前往 Releases 页面直接下载",
        "ja" => string.IsNullOrEmpty(retryAt)
            ? "GitHub API レート制限 — 後で再試行するか、Releases ページから直接ダウンロード"
            : $"GitHub API レート制限（{retryAt} 以降で再試行可）— または Releases ページから直接ダウンロード",
        _ => string.IsNullOrEmpty(retryAt)
            ? "GitHub API rate limit hit — retry later or download from the Releases page"
            : $"GitHub API rate limit (retry after {retryAt}) — or download from the Releases page"
    };

    /// <summary>네트워크 도달 불가 (DNS/연결 실패).</summary>
    public static string UpdateCheckNetworkError => Lang switch
    {
        "ko" => "네트워크 연결 실패 — 인터넷 연결을 확인해주세요",
        "zh" => "网络连接失败 — 请检查网络连接",
        "ja" => "ネットワーク接続失敗 — インターネット接続を確認してください",
        _ => "Network unreachable — please check your internet connection"
    };

    /// <summary>응답 지연 (15초 timeout).</summary>
    public static string UpdateCheckTimeout => Lang switch
    {
        "ko" => "GitHub 응답 지연 — 잠시 후 다시 시도해주세요",
        "zh" => "GitHub 响应超时 — 请稍后重试",
        "ja" => "GitHub の応答が遅延 — 後で再試行してください",
        _ => "GitHub timed out — please retry shortly"
    };

    /// <summary>그 외 GitHub API 에러 (5xx 등).</summary>
    public static string UpdateCheckApiError(int statusCode) => Lang switch
    {
        "ko" => $"GitHub API 오류 (HTTP {statusCode}) — Releases 페이지에서 직접 받기 권장",
        "zh" => $"GitHub API 错误 (HTTP {statusCode}) — 建议前往 Releases 页面直接下载",
        "ja" => $"GitHub API エラー (HTTP {statusCode}) — Releases ページから直接ダウンロード推奨",
        _ => $"GitHub API error (HTTP {statusCode}) — try downloading from the Releases page"
    };

    /// <summary>API 오류 발생 시 Releases 페이지 열기 제안 다이얼로그 본문.</summary>
    public static string UpdateCheckApiErrorDialogPrompt => Lang switch
    {
        "ko" => "버전 확인이 불가능합니다.\n\nReleases 페이지에서 직접 최신 버전을 다운로드할 수 있습니다.\n지금 Releases 페이지를 열겠습니까?",
        "zh" => "无法检查版本更新。\n\n您可以在 Releases 页面直接下载最新版本。\n是否立即打开 Releases 页面？",
        "ja" => "バージョン確認ができません。\n\nReleases ページから直接最新バージョンをダウンロードできます。\n今すぐ Releases ページを開きますか？",
        _ => "Unable to check for updates.\n\nYou can download the latest version directly from the Releases page.\nOpen the Releases page now?"
    };

    /// <summary>"Releases 페이지 열기" 액션 버튼 라벨.</summary>
    public static string OpenReleasesPage => Lang switch
    {
        "ko" => "Releases 페이지 열기 ↗",
        "zh" => "打开 Releases 页面 ↗",
        "ja" => "Releases ページを開く ↗",
        _ => "Open Releases page ↗"
    };

    /// <summary>
    /// 릴리즈 노트에서 현재 언어 블록만 추출한다.
    /// <!-- ko -->...<!-- /ko --> 형식. 해당 언어 블록이 없으면 en 블록을 반환한다.
    /// </summary>
    public static string FilterReleaseNotes(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var section = ExtractLangBlock(raw, Lang);
        if (!string.IsNullOrWhiteSpace(section)) return section.Trim();

        var en = ExtractLangBlock(raw, "en");
        if (!string.IsNullOrWhiteSpace(en)) return en.Trim();

        return raw.Trim();
    }

    private static string ExtractLangBlock(string text, string lang)
    {
        var open  = $"<!-- {lang} -->";
        var close = $"<!-- /{lang} -->";
        var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return "";
        start += open.Length;
        var end = text.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return "";
        return text[start..end];
    }

    public static string DepletionAt(string time) => Lang switch
    {
        "ko" => $"⚡ 이 속도면 {time}경 조기 소진",
        "zh" => $"⚡ 按此速度 {time} 提前耗尽",
        "ja" => $"⚡ このペースなら {time} 頃に早期枯渇",
        _ => $"⚡ at this rate, depletes early ~{time}"
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

    public static string ExtraUsageExhausted => Lang switch
    {
        "ko" => "기본 사용량 소진 - 추가 사용량 모니터링 중",
        "zh" => "基本用量耗尽 - 监控额外用量中",
        "ja" => "基本使用量枯渇 - 追加使用量を監視中",
        _ => "Base quota exhausted - monitoring extra usage"
    };

    public static string ExtraCreditsLabel => Lang switch
    {
        "ko" => "추가 크레딧",
        "zh" => "额外积分",
        "ja" => "追加クレジット",
        _ => "Extra Credits"
    };

    public static string ExtraCredits(double used, double limit) => Lang switch
    {
        "ko" => $"{used:N0} / {limit:N0} 크레딧",
        "zh" => $"{used:N0} / {limit:N0} 积分",
        "ja" => $"{used:N0} / {limit:N0} クレジット",
        _ => $"{used:N0} / {limit:N0} credits"
    };

    public static string ExtraCreditsUsedOnly(double used) => Lang switch
    {
        "ko" => $"{used:N0} 크레딧 사용됨 (한도 미설정)",
        "zh" => $"已使用 {used:N0} 积分（未设限额）",
        "ja" => $"{used:N0} クレジット使用済み（上限未設定）",
        _ => $"{used:N0} credits used (no limit set)"
    };

    // Chart toggle labels
    public static string SevenDayToggle => Lang switch
    {
        "ko" => "7일",
        "zh" => "7天",
        "ja" => "7日",
        _ => "7d"
    };

    public static string TodayToggle => Lang switch
    {
        "ko" => "오늘",
        "zh" => "今日",
        "ja" => "今日",
        _ => "Today"
    };

    public static string ExportCsvTooltip => Lang switch
    {
        "ko" => "CSV로 내보내기",
        "zh" => "导出为 CSV",
        "ja" => "CSV に書き出す",
        _ => "Export to CSV"
    };

    public static string Disclaimer => Lang switch
    {
        "ko" => "이 앱은 참고용 도구입니다. 표시되는 수치는 공식 수치와 다를 수 있으며, 요금·과금 관련 문제에 대해 개발자는 책임을 지지 않습니다. 정확한 사용량은 Anthropic 공식 콘솔에서 확인하세요.",
        "zh" => "本应用仅供参考。显示数值可能与官方数据不同，开发者不对任何计费问题承担责任。请通过 Anthropic 官方控制台确认准确用量。",
        "ja" => "このアプリは参考ツールです。表示される数値は公式データと異なる場合があります。料金関連の問題について開発者は責任を負いません。正確な使用量は Anthropic の公式コンソールで確認してください。",
        _ => "This app is a reference tool only. Displayed values may differ from official figures. The developer is not liable for any billing issues. Please verify accurate usage on the official Anthropic console."
    };

    public static string GenericDisclaimer => Lang switch
    {
        "ko" => "이 앱은 참고용 도구입니다. 표시되는 수치는 로컬 로그 또는 제공된 quota 개념을 바탕으로 계산될 수 있으며, 공식 수치와 다를 수 있습니다.",
        "zh" => "本应用仅供参考。显示数值可能基于本地日志或提供的配额概念计算，可能与官方数据不同。",
        "ja" => "このアプリは参考ツールです。表示値はローカルログや公開されたクォータ概念を元に計算される場合があり、公式データと異なることがあります。",
        _ => "This app is a reference tool only. Displayed values may be derived from local logs or published quota concepts and may differ from official figures."
    };

    public static string CodexSourceNotFound => Lang switch
    {
        "ko" => "Codex 로컬 세션 폴더를 찾지 못했습니다.",
        "zh" => "未找到 Codex 本地会话文件夹。",
        "ja" => "Codex のローカルセッションフォルダが見つかりません。",
        _ => "Codex local session folder was not found."
    };

    public static string CodexNoUsageToday => Lang switch
    {
        "ko" => "오늘 Codex 사용 기록이 아직 없습니다.",
        "zh" => "今天还没有 Codex 使用记录。",
        "ja" => "本지의 Codex 使用記録はまだありません。",
        _ => "No Codex usage has been recorded today yet."
    };

    public static string ClaudeNoUsageToday => Lang switch
    {
        "ko" => "오늘 사용 기록이 아직 없습니다.",
        "zh" => "今天还没有使用记录。",
        "ja" => "本日の使用記録はまだありません。",
        _ => "No usage has been recorded today yet."
    };

    public static string GeminiCliSourceNotFound => Lang switch
    {
        "ko" => "Gemini CLI 사용자 폴더를 찾지 못했습니다.",
        "zh" => "未找到 Gemini CLI 用户目录。",
        "ja" => "Gemini CLI のユーザーディレクトリが見つかりません。",
        _ => "Gemini CLI user directory was not found."
    };

    public static string GeminiCliSessionPathMissing => Lang switch
    {
        "ko" => "Gemini CLI 세션 폴더를 아직 찾지 못했습니다.",
        "zh" => "尚未找到 Gemini CLI 会话文件夹。",
        "ja" => "Gemini CLI のセッションフォルダがまだ見つかりません。",
        _ => "Gemini CLI session folder was not found yet."
    };

    public static string GeminiCliNoUsageToday => Lang switch
    {
        "ko" => "오늘 Gemini CLI 세션 기록이 아직 없습니다.",
        "zh" => "今天还没有 Gemini CLI 会话记录。",
        "ja" => "本日の Gemini CLI セッション記録はまだありません。",
        _ => "No Gemini CLI session has been recorded today yet."
    };

    public static string GeminiCliEstimateOnly => Lang switch
    {
        "ko" => "Gemini CLI 로컬 로그 기반 추정치입니다. 공식 대시보드와 차이가 있을 수 있습니다.",
        "zh" => "基于 Gemini CLI 本地日志的估算值，可能与官方数据存在差异。",
        "ja" => "Gemini CLI のローカルログに基づく推定値です。公式値と差が出る場合があります。",
        _ => "Estimated from Gemini CLI local logs and may differ from official dashboard values."
    };

    public static string GeminiCliRequestSummary(int requests, long outputTokens) => Lang switch
    {
        "ko" => $"오늘 {requests}회 요청 · 출력 {FormatTokenShort(outputTokens)} 토큰",
        "zh" => $"今日 {requests} 次请求 · 输出 {FormatTokenShort(outputTokens)} 词元",
        "ja" => $"本日 {requests} リクエスト · 出力 {FormatTokenShort(outputTokens)} トークン",
        _ => $"Today {requests} requests · {FormatTokenShort(outputTokens)} output tokens"
    };

    public static string TrayStatusClaude(double percent) => Lang switch
    {
        "ko" => $"Claude {percent:P0}",
        _ => $"Claude {percent:P0}"
    };

    public static string TrayStatusCodex(double percent, string? dataSource) => Lang switch
    {
        "ko" => dataSource == "Direct API" ? $"Codex {percent:P0}" : $"Codex {percent:P0} (Log)",
        _ => dataSource == "Direct API" ? $"Codex {percent:P0}" : $"Codex {percent:P0} (Log)"
    };

    public static string TrayStatusGemini(int requests, long outputTokens) => Lang switch
    {
        "ko" => requests > 0 ? $"Gemini {requests}회 · {FormatTokenShort(outputTokens)}" : "Gemini",
        _ => requests > 0 ? $"Gemini {requests} req · {FormatTokenShort(outputTokens)}" : "Gemini"
    };

    public static string TrayStatusOpenCode(int requests, long inputTokens, long outputTokens) => Lang switch
    {
        "ko" => requests > 0 ? $"OpenCode {requests}회 · {FormatTokenShort(inputTokens + outputTokens)}" : "OpenCode",
        _ => requests > 0 ? $"OpenCode {requests} req · {FormatTokenShort(inputTokens + outputTokens)}" : "OpenCode"
    };

    public static string OpenCodeUsageTitle => Lang switch
    {
        "ko" => "OpenCode 사용량",
        "zh" => "OpenCode 用量",
        "ja" => "OpenCode 使用量",
        _ => "OpenCode Usage"
    };

    public static string ProviderOpenCode => "OpenCode";

    public static string ProviderOpenCodeNote => Lang switch
    {
        "ko" => "OpenCode 구독 플랜 기준 · 로컬 DB에서 읽음",
        "zh" => "基于 OpenCode 订阅计划 · 从本地 DB 读取",
        "ja" => "OpenCode サブスクリプション基準 · ローカル DB から読み込み",
        _ => "Based on OpenCode subscription · read from local DB"
    };

    public static string OpenCodeDbNotFound => Lang switch
    {
        "ko" => "OpenCode 로컬 DB를 찾지 못했습니다.",
        "zh" => "未找到 OpenCode 本地数据库。",
        "ja" => "OpenCode のローカル DB が見つかりません。",
        _ => "OpenCode local database was not found."
    };

    public static string OpenCodeNoUsageToday => Lang switch
    {
        "ko" => "오늘 OpenCode 사용 기록이 아직 없습니다.",
        "zh" => "今天还没有 OpenCode 使用记录。",
        "ja" => "本日の OpenCode 使用記録はまだありません。",
        _ => "No OpenCode usage has been recorded today yet."
    };

    public static string GeminiRequests => CurrentLang switch
    {
        "ko" => "요청 횟수",
        "zh" => "请求次数",
        "ja" => "リクエスト数",
        _ => "Requests"
    };

    public static string TrayDisplayMode => CurrentLang switch
    {
        "ko" => "트레이 표시 기준",
        "zh" => "托盘显示标准",
        "ja" => "トレイ表示基準",
        _ => "Tray Display Base"
    };

    public static string DailyTokenGoal => CurrentLang switch
    {
        "ko" => "일일 목표 토큰 (출력)",
        "zh" => "每日目标令牌 (输出)",
        "ja" => "1日の目標トークン (出力)",
        _ => "Daily Token Goal (Output)"
    };

    public static string ProjectPage => CurrentLang switch
    {
        "ko" => "프로젝트 페이지",
        "zh" => "项目页面",
        "ja" => "プロジェクトページ",
        _ => "Project Page"
    };

    public static string HideInactiveProviders => CurrentLang switch
    {
        "ko" => "데이터 없는 공급자 숨기기",
        "zh" => "隐藏无数据的供应商",
        "ja" => "データのないプロバイダーを隠す",
        _ => "Hide inactive providers"
    };

    public static string VisibleProviders => CurrentLang switch
    {
        "ko" => "표시할 에이전트 선택",
        "zh" => "选择要显示的代理",
        "ja" => "表示するエージェントを選択",
        _ => "Select agents to display"
    };


    public static string SubscriptionExpired => CurrentLang switch
    {
        "ko" => "구독 만료",
        "zh" => "订阅已过期",
        "ja" => "サブスクリプション期限切れ",
        _ => "Sub Expired"
    };

    private static string FormatTokenShort(long tokens) =>

        tokens >= 1_000_000 ? $"{tokens / 1_000_000.0:F1}M" :
        tokens >= 1_000 ? $"{tokens / 1_000.0:F1}K" :
        tokens.ToString();

    public static string ApiError(string msg) => Lang switch
    {
        "ko" => $"API 오류: {msg}",
        "zh" => $"API 错误: {msg}",
        "ja" => $"API エラー: {msg}",
        _ => $"API error: {msg}"
    };

    public static string ApiPermissionDenied => Lang switch
    {
        "ko" => "API 접근 권한이 없거나 요청을 처리할 수 없습니다. (잠시 후 자동 재시도)",
        "zh" => "无 API 访问权限或无法处理请求。（稍后将自动重试）",
        "ja" => "API アクセス権限がないか、リクエストを処理できません。（しばらくして自動再試行）",
        _ => "No API permission or unable to process request. (Auto-retry shortly)"
    };

    public static string UpdateHashMismatch => Lang switch
    {
        "ko" => "다운로드된 파일이 손상되었거나 변조되었습니다. 다시 시도하거나 개발자에게 보고하세요.",
        "zh" => "下载的文件已损坏或被篡改。请重试或向开发者报告。",
        "ja" => "ダウンロードしたファイルが破損しているか、改ざんされています。再試行するか開発者に報告してください。",
        _ => "Downloaded file is corrupted or may have been tampered with. Please retry or report to the developer."
    };

    public static string UpdateDownloadFailed(string msg) => Lang switch
    {
        "ko" => $"업데이트 다운로드 실패: {msg}",
        "zh" => $"更新下载失败: {msg}",
        "ja" => $"アップデートダウンロード失敗: {msg}",
        _ => $"Update download failed: {msg}"
    };

    public static string Unavailable => Lang switch
    {
        "ko" => "데이터 없음",
        "zh" => "无数据",
        "ja" => "データなし",
        _ => "No data"
    };

    // Language
    public static string LanguageSection => Lang switch
    {
        "ko" => "언어",
        "zh" => "语言",
        "ja" => "言語",
        _ => "Language"
    };

    public static string LanguageSystem => Lang switch
    {
        "ko" => "시스템 언어",
        "zh" => "系统语言",
        "ja" => "システム言語",
        _ => "System Language"
    };

    // Polling interval
    public static string PollingInterval => Lang switch
    {
        "ko" => "갱신 주기",
        "zh" => "刷新间隔",
        "ja" => "更新間隔",
        _ => "Refresh Interval"
    };

    public static string PollingIntervalHint => Lang switch
    {
        "ko" => "분 단위 (최소 1분, 기본값: 2분)",
        "zh" => "分钟 (最小1分钟, 默认2分钟)",
        "ja" => "分単位 (最小1分, デフォルト2分)",
        _ => "Minutes (min 1, default 2)"
    };

    public static string PollingIntervalInvalid => Lang switch
    {
        "ko" => "1 이상의 숫자를 입력하세요",
        "zh" => "请输入1以上的数字",
        "ja" => "1以上の数字を入力してください",
        _ => "Enter a number greater than 0"
    };

    public static string UsageSyncTitle => Lang switch
    {
        "ko" => "다중 PC 동기화",
        "zh" => "多设备同步",
        "ja" => "複数PC同期",
        _ => "Multi-PC Sync"
    };

    public static string UsageSyncEnabled => Lang switch
    {
        "ko" => "공유 폴더로 사용량 동기화",
        "zh" => "通过共享文件夹同步用量",
        "ja" => "共有フォルダーで使用量を同期",
        _ => "Sync usage through a shared folder"
    };

    public static string UsageSyncDescription => Lang switch
    {
        "ko" => "각 PC가 민감정보 없는 일일 스냅샷만 저장하고, 화면에는 장치별 사용량 합산과 최신 할당량을 표시합니다.",
        "zh" => "每台设备只保存不含敏感信息的每日快照，并在界面中合并显示各设备用量和最新配额。",
        "ja" => "各PCは機密情報を含まない日次スナップショットだけを保存し、画面では端末別使用量の合算と最新の上限を表示します。",
        _ => "Each PC stores only non-sensitive daily snapshots and shows merged device usage with the newest quota."
    };

    public static string UsageSyncFolder => Lang switch
    {
        "ko" => "공유 폴더",
        "zh" => "共享文件夹",
        "ja" => "共有フォルダー",
        _ => "Shared folder"
    };

    public static string UsageSyncBrowse => Lang switch
    {
        "ko" => "찾기",
        "zh" => "浏览",
        "ja" => "参照",
        _ => "Browse"
    };

    public static string UsageSyncDisabled => Lang switch
    {
        "ko" => "동기화 꺼짐",
        "zh" => "同步已关闭",
        "ja" => "同期はオフです",
        _ => "Sync off"
    };

    public static string UsageSyncFolderRequired => Lang switch
    {
        "ko" => "켜려면 공유 폴더를 선택하세요.",
        "zh" => "启用前请选择共享文件夹。",
        "ja" => "有効にするには共有フォルダーを選択してください。",
        _ => "Choose a shared folder to turn sync on."
    };

    public static string UsageSyncReady => Lang switch
    {
        "ko" => "동기화 준비됨",
        "zh" => "同步已就绪",
        "ja" => "同期準備完了",
        _ => "Sync ready"
    };

    public static string UsageSyncFolderWillBeCreated => Lang switch
    {
        "ko" => "다음 갱신 때 폴더를 만들고 스냅샷을 저장합니다.",
        "zh" => "下次刷新时会创建文件夹并保存快照。",
        "ja" => "次回更新時にフォルダーを作成してスナップショットを保存します。",
        _ => "The folder will be created and written on the next refresh."
    };

    public static string UsageSyncBrowseDialogTitle => Lang switch
    {
        "ko" => "사용량 동기화 폴더 선택",
        "zh" => "选择用量同步文件夹",
        "ja" => "使用量同期フォルダーを選択",
        _ => "Choose usage sync folder"
    };

    public static string UsageSyncFailed(string message) => Lang switch
    {
        "ko" => $"동기화 실패: {message}",
        "zh" => $"同步失败: {message}",
        "ja" => $"同期失敗: {message}",
        _ => $"Sync failed: {message}"
    };

    public static string UsageSyncMergedDevices(int count) => Lang switch
    {
        "ko" => $"{count}대 PC 합산",
        "zh" => $"已合并 {count} 台设备",
        "ja" => $"{count}台のPCを合算",
        _ => $"Merged from {count} PCs"
    };

    public static string UsageSyncQuotaFromDevice(string deviceName, string observedAt) => Lang switch
    {
        "ko" => $"{deviceName}의 {observedAt} 스냅샷으로 표시 중",
        "zh" => $"正在显示 {deviceName} 于 {observedAt} 的快照",
        "ja" => $"{deviceName} の {observedAt} スナップショットを表示中",
        _ => $"Showing {deviceName} snapshot from {observedAt}"
    };

    // ===== 날씨 (v1.29.0) =====
    public static string WeatherTab => Lang switch
    {
        "ko" => "날씨",
        "zh" => "天气",
        "ja" => "天気",
        _ => "Weather"
    };

    public static string WeatherSettingsTitle => Lang switch
    {
        "ko" => "날씨 알림 설정",
        "zh" => "天气通知设置",
        "ja" => "天気通知設定",
        _ => "Weather Alert Settings"
    };

    public static string WeatherEnabled => Lang switch
    {
        "ko" => "날씨 알림 사용",
        "zh" => "启用天气通知",
        "ja" => "天気通知を有効にする",
        _ => "Enable weather alerts"
    };

    public static string WeatherShowInTrayTooltip => Lang switch
    {
        "ko" => "트레이 툴팁에 날씨 표시",
        "zh" => "在托盘提示中显示天气",
        "ja" => "トレイツールチップに天気を表示",
        _ => "Show weather in tray tooltip"
    };

    public static string WeatherLocation => Lang switch
    {
        "ko" => "위치",
        "zh" => "位置",
        "ja" => "位置",
        _ => "Location"
    };

    public static string WeatherSearchPlaceholder => Lang switch
    {
        "ko" => "도시명 또는 우편번호 검색...",
        "zh" => "搜索城市或邮政编码...",
        "ja" => "都市名または郵便番号で検索...",
        _ => "Search city or postal code..."
    };

    public static string WeatherSearch => Lang switch
    {
        "ko" => "검색",
        "zh" => "搜索",
        "ja" => "検索",
        _ => "Search"
    };

    public static string WeatherUseCurrentLocation => Lang switch
    {
        "ko" => "현재 위치 사용",
        "zh" => "使用当前位置",
        "ja" => "現在地を使用",
        _ => "Use current location"
    };

    public static string WeatherDailyForecast => Lang switch
    {
        "ko" => "매일 예보 알림",
        "zh" => "每日预报通知",
        "ja" => "毎日の予報通知",
        _ => "Daily forecast alert"
    };

    public static string WeatherDailyForecastTime => Lang switch
    {
        "ko" => "알림 시각 (HH:mm)",
        "zh" => "通知时间 (HH:mm)",
        "ja" => "通知時刻 (HH:mm)",
        _ => "Alert time (HH:mm)"
    };

    public static string WeatherConditionAlerts => Lang switch
    {
        "ko" => "기상 조건 알림",
        "zh" => "天气条件通知",
        "ja" => "気象条件通知",
        _ => "Condition alerts"
    };

    public static string WeatherRainProbabilityThreshold => Lang switch
    {
        "ko" => "강수확률 임계값 (%)",
        "zh" => "降水概率阈值 (%)",
        "ja" => "降水確率閾値 (%)",
        _ => "Rain probability threshold (%)"
    };

    public static string WeatherHighTemperatureThreshold => Lang switch
    {
        "ko" => "폭염 임계값 (°C)",
        "zh" => "高温阈值 (°C)",
        "ja" => "高温閾値 (°C)",
        _ => "Heat threshold (°C)"
    };

    public static string WeatherLowTemperatureThreshold => Lang switch
    {
        "ko" => "한파 임계값 (°C)",
        "zh" => "低温阈值 (°C)",
        "ja" => "低温閾値 (°C)",
        _ => "Cold threshold (°C)"
    };

    public static string WeatherWindSpeedThreshold => Lang switch
    {
        "ko" => "강풍 임계값 (km/h)",
        "zh" => "强风阈值 (km/h)",
        "ja" => "強風閾値 (km/h)",
        _ => "Wind threshold (km/h)"
    };

    public static string WeatherOfficialAlerts => Lang switch
    {
        "ko" => "공식 기상 특보 알림",
        "zh" => "官方气象警报通知",
        "ja" => "公式気象警報通知",
        _ => "Official weather alerts"
    };

    public static string WeatherOfficialAlertsHint => Lang switch
    {
        "ko" => "미국 위치에서만 NWS(미국 기상청) 특보를 조회합니다",
        "zh" => "仅美国位置可获取 NWS（美国气象局）警报",
        "ja" => "米国内の位置でのみ NWS（米国気象局）警報を取得",
        _ => "NWS alerts available for US locations only"
    };

    public static string WeatherSearchNoResults => Lang switch
    {
        "ko" => "검색 결과 없음",
        "zh" => "无搜索结果",
        "ja" => "検索結果なし",
        _ => "No results found"
    };

    public static string WeatherSearchFailed => Lang switch
    {
        "ko" => "위치 검색 실패",
        "zh" => "位置搜索失败",
        "ja" => "位置検索に失敗",
        _ => "Location search failed"
    };

    public static string WeatherCurrentUnavailable => Lang switch
    {
        "ko" => "날씨 정보 없음",
        "zh" => "天气信息不可用",
        "ja" => "天気情報なし",
        _ => "Weather unavailable"
    };

    public static string WeatherForecastTitle => Lang switch
    {
        "ko" => "오늘의 날씨",
        "zh" => "今日天气",
        "ja" => "今日の天気",
        _ => "Today's Weather"
    };

    public static string TestWeatherNotification => Lang switch
    {
        "ko" => "날씨 알림 테스트",
        "zh" => "天气通知测试",
        "ja" => "天気通知テスト",
        _ => "Test weather alert"
    };

    public static string TestWeatherHint => Lang switch
    {
        "ko" => "현재 날씨 정보로 알림을 전송합니다",
        "zh" => "当前天气信息发送通知",
        "ja" => "現在の天気情報で通知を送信します",
        _ => "Sends a notification with current weather info"
    };

    public static string TestWeatherNoLocation => Lang switch
    {
        "ko" => "위치가 설정되지 않았습니다",
        "zh" => "未设置位置",
        "ja" => "位置が設定されていません",
        _ => "No location set"
    };

    public static string TestWeatherNoData => Lang switch
    {
        "ko" => "날씨 데이터를 불러오지 못했습니다",
        "zh" => "无法加载天气数据",
        "ja" => "天気データを読み込めませんでした",
        _ => "Could not load weather data"
    };

    public static string WeatherWarningTitle => Lang switch
    {
        "ko" => "기상 알림",
        "zh" => "天气警报",
        "ja" => "気象アラート",
        _ => "Weather Alert"
    };

    public static string WeatherClear => Lang switch
    {
        "ko" => "맑음",
        "zh" => "晴",
        "ja" => "晴れ",
        _ => "Clear"
    };

    public static string WeatherMainlyClear => Lang switch
    {
        "ko" => "대체로 맑음",
        "zh" => "大部晴朗",
        "ja" => "おおむね晴れ",
        _ => "Mainly Clear"
    };

    public static string WeatherPartlyCloudy => Lang switch
    {
        "ko" => "구름 조금",
        "zh" => "多云",
        "ja" => "所々曇り",
        _ => "Partly Cloudy"
    };

    public static string WeatherOvercast => Lang switch
    {
        "ko" => "흐림",
        "zh" => "阴",
        "ja" => "曇り",
        _ => "Overcast"
    };

    public static string WeatherFog => Lang switch
    {
        "ko" => "안개",
        "zh" => "雾",
        "ja" => "霧",
        _ => "Fog"
    };

    public static string WeatherDrizzle => Lang switch
    {
        "ko" => "이슬비",
        "zh" => "毛毛雨",
        "ja" => "霧雨",
        _ => "Drizzle"
    };

    public static string WeatherRain => Lang switch
    {
        "ko" => "비",
        "zh" => "雨",
        "ja" => "雨",
        _ => "Rain"
    };

    public static string WeatherSnow => Lang switch
    {
        "ko" => "눈",
        "zh" => "雪",
        "ja" => "雪",
        _ => "Snow"
    };

    public static string WeatherThunderstorm => Lang switch
    {
        "ko" => "뇌우",
        "zh" => "雷暴",
        "ja" => "雷雨",
        _ => "Thunderstorm"
    };

    public static string WeatherUnknown => Lang switch
    {
        "ko" => "알 수 없음",
        "zh" => "未知",
        "ja" => "不明",
        _ => "Unknown"
    };

    public static string WeatherRainProbability(int percent) => Lang switch
    {
        "ko" => $"강수확률 {percent}%",
        "zh" => $"降水概率 {percent}%",
        "ja" => $"降水確率 {percent}%",
        _ => $"Rain {percent}%"
    };

    public static string WeatherRainWarning(int percent) => Lang switch
    {
        "ko" => $"강수확률 {percent}%",
        "zh" => $"降水概率 {percent}%",
        "ja" => $"降水確率 {percent}%",
        _ => $"Precipitation probability {percent}%"
    };

    public static string WeatherHeatWarning(double temp) => Lang switch
    {
        "ko" => $"최고 {temp:F0}°C 예보",
        "zh" => $"预报最高 {temp:F0}°C",
        "ja" => $"最高 {temp:F0}°C 予報",
        _ => $"High of {temp:F0}°C forecast"
    };

    public static string WeatherColdWarning(double temp) => Lang switch
    {
        "ko" => $"최저 {temp:F0}°C 예보",
        "zh" => $"预报最低 {temp:F0}°C",
        "ja" => $"最低 {temp:F0}°C 予報",
        _ => $"Low of {temp:F0}°C forecast"
    };

    public static string WeatherWindWarning(double speed) => Lang switch
    {
        "ko" => $"최대 풍속 {speed:F0} km/h 예보",
        "zh" => $"预报最大风速 {speed:F0} km/h",
        "ja" => $"最大風速 {speed:F0} km/h 予報",
        _ => $"Wind up to {speed:F0} km/h forecast"
    };

    public static string WeatherTooltipFormat(string location, double temp, string condition) => Lang switch
    {
        "ko" => $"{location} {temp:F0}°C {condition}",
        "zh" => $"{location} {temp:F0}°C {condition}",
        "ja" => $"{location} {temp:F0}°C {condition}",
        _ => $"{location} {temp:F0}°C {condition}"
    };

    public static string WeatherDailyTemp(double min, double max) => Lang switch
    {
        "ko" => $"최저 {min:F0}°C / 최고 {max:F0}°C",
        "zh" => $"最低 {min:F0}°C / 最高 {max:F0}°C",
        "ja" => $"最低 {min:F0}°C / 最高 {max:F0}°C",
        _ => $"Low {min:F0}°C / High {max:F0}°C"
    };

    public static string WeatherCurrentTemp(double temp) => Lang switch
    {
        "ko" => $"현재 {temp:F0}°C",
        "zh" => $"当前 {temp:F0}°C",
        "ja" => $"現在 {temp:F0}°C",
        _ => $"Now {temp:F0}°C"
    };

    public static string WeatherFeelsLike(double temp) => Lang switch
    {
        "ko" => $"체감 {temp:F0}°C",
        "zh" => $"体感 {temp:F0}°C",
        "ja" => $"体感 {temp:F0}°C",
        _ => $"feels like {temp:F0}°C"
    };
}
