using System.Text;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 이 PC 를 다른 기기에 보여 줄 이름(#271). 설정의 'PC 이름'이 있으면 그것, 비어 있으면 Windows 컴퓨터 이름.
///
/// 왜 정적 값인가: 휴대폰 서버·앱 푸시·PC 동기화·공유 폴더 스냅샷·ntfy 알림이 모두 같은 이름을 써야 하고,
/// 설정을 바꾸면 다시 시작하지 않아도 다음 전송부터 바뀌어야 한다. 각 구성 요소가 생성 때 이름을 복사해 두면
/// 설정이 바뀔 때마다 모두 다시 만들어야 한다 — 그래서 보낼 때마다 여기서 읽는다.
/// 이름은 식별자가 아니다(PC 구분은 pcId·DeviceId). 바뀌어도 페어링·중복 판정에는 영향이 없다.
/// </summary>
public static class PcName
{
    /// <summary>
    /// 설정에서 받는 최대 길이. 휴대폰 칩·알림 보조 문구에 들어가므로 짧게 둔다.
    /// 받는 쪽(<see cref="Clean"/>)은 예전·다른 버전과 맞추려고 64자까지 받는다.
    /// </summary>
    public const int MaxAliasLength = 32;

    private const int MaxReceivedLength = 64;

    private static volatile string _alias = "";

    /// <summary>지금 보낼 이름.</summary>
    public static string Current => _alias is { Length: > 0 } alias ? alias : Environment.MachineName;

    /// <summary>설정의 별칭. 비어 있으면 컴퓨터 이름을 쓴다.</summary>
    public static string Alias => _alias;

    /// <summary>
    /// 이름을 보낼 때마다 읽을 함수. 고정 이름을 주면(테스트) 그 이름을, 아니면 <see cref="Current"/> 를 읽는다.
    /// </summary>
    public static Func<string> Source(string? fixedName)
    {
        if (string.IsNullOrWhiteSpace(fixedName)) return () => Current;
        var name = fixedName.Trim();
        return () => name;
    }

    /// <summary>설정을 읽거나 저장한 뒤 부른다.</summary>
    public static void SetAlias(string? alias) => _alias = Clean(alias, MaxAliasLength);

    /// <summary>
    /// Input : 사용자 입력 또는 다른 PC 가 보낸 이름, 최대 길이
    /// Output: 앞뒤 공백을 없애고, 제어 문자(줄바꿈 등)를 공백으로 바꿔 연속 공백을 하나로 줄이고, 최대 길이로 자른 이름
    /// 핵심 로직: 이름은 알림 한 줄·칩 한 칸에 들어간다 — 줄바꿈이 섞이면 ntfy 본문 끝의 "— 이름" 줄이 깨진다.
    ///           서로게이트 쌍 가운데에서 자르지 않는다(이모지가 반쪽으로 남지 않게).
    /// </summary>
    public static string Clean(string? name, int maxLength = MaxReceivedLength)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var ch in name)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) sb.Append(' ');
            pendingSpace = false;
            sb.Append(ch);
        }
        if (sb.Length <= maxLength) return sb.ToString();
        var cut = maxLength;
        if (char.IsHighSurrogate(sb[cut - 1])) cut--;
        return sb.ToString(0, cut).TrimEnd();
    }
}
