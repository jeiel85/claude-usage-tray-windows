using System.IO;
using System.Text.RegularExpressions;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 이 설치(PC 의 사용자 프로필)를 가리키는 무작위 ID. 처음 한 번 만들어 파일에 저장하고 재시작해도 그대로 쓴다.
///
/// 왜 필요한가: 계정을 알 수 없는 PC(API 키 모드 등)는 알림 사건 키에 계정 대신 기기 구분자를 넣는다(#175).
/// 이 값이 실행마다 바뀌면 재시작 뒤에 직전 알림을 알아보지 못해 같은 알림을 다시 보낸다 — 레이트 리밋 알림은
/// 오늘 기록에 발생 흔적이 있으면 시작 직후 첫 관측에서도 나가므로 실제로 일어난다.
/// 반대로 컴퓨터 이름·사용자 이름은 복제·재설치·같은 이름 설정으로 서로 다른 PC 에서 겹칠 수 있어 쓰지 않는다.
/// 한계: 프로필 폴더(<c>~/.claude</c>)를 통째로 복제하면 이 파일도 함께 복제되어 두 PC 가 같은 ID 를 갖는다.
/// </summary>
internal static class DeviceIdentity
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "claude-usage-tray-device-id");

    private static readonly Regex ValidId = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    private static readonly Lazy<string> CurrentId = new(() => GetOrCreate(DefaultPath));

    /// <summary>이 설치의 ID(32자리 16진수). 첫 사용 때 읽거나 만든다.</summary>
    public static string Current => CurrentId.Value;

    /// <summary>
    /// 파일에서 ID 를 읽고, 없거나 형식이 맞지 않으면 새로 만들어 저장한다.
    ///
    /// Input : ID 를 보관할 파일 경로
    /// Output: 32자리 소문자 16진수 ID
    /// 핵심 로직: 읽기·쓰기가 실패해도 예외를 밖으로 내보내지 않는다 — 알림 경로를 끊는 것보다 이번 실행에서만 쓰는
    ///           임시 ID 로 버티는 편이 낫다(그 경우 재시작 뒤 중복 방지만 약해질 뿐 알림은 잃지 않는다).
    ///           다른 프로그램이 쓰는 중이어도 읽히도록 FileShare.ReadWrite 로 연다.
    /// </summary>
    internal static string GetOrCreate(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var existing = reader.ReadToEnd().Trim();
                if (ValidId.IsMatch(existing))
                    return existing;
            }
        }
        catch
        {
            // 읽지 못하면 아래에서 새로 만든다.
        }

        var created = Guid.NewGuid().ToString("N");
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream);
            writer.Write(created);
        }
        catch
        {
            // 저장하지 못해도 이번 실행에서는 이 값을 쓴다.
        }

        return created;
    }
}
