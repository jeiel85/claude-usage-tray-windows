using System.Text.Json;

namespace ClaudeUsageTray.Services;

/// <summary>
/// JSON 객체의 숫자 속성을 예외 없이 읽는다(#201).
/// <see cref="JsonElement.TryGetInt64(out long)"/> 등은 이름과 달리 값이 숫자가 아니면(null·문자열 등) false 가 아니라
/// <see cref="InvalidOperationException"/> 을 던진다. 그대로 부르면 값 하나 때문에 API 응답·로그 줄 전체가 버려지므로
/// 종류를 먼저 보고 숫자일 때만 읽는다. 속성이 없거나, 숫자가 아니거나, 형식의 범위를 벗어나면 false(값은 0)다.
/// </summary>
internal static class JsonNumber
{
    public static bool TryReadInt64(JsonElement element, string name, out long value)
    {
        value = 0;
        return TryGetNumber(element, name, out var number) && number.TryGetInt64(out value);
    }

    public static bool TryReadInt32(JsonElement element, string name, out int value)
    {
        value = 0;
        return TryGetNumber(element, name, out var number) && number.TryGetInt32(out value);
    }

    public static bool TryReadDecimal(JsonElement element, string name, out decimal value)
    {
        value = 0;
        return TryGetNumber(element, name, out var number) && number.TryGetDecimal(out value);
    }

    // 객체가 아닌 요소에서 TryGetProperty 를 불러도 같은 예외가 나므로 그것도 함께 거른다.
    private static bool TryGetNumber(JsonElement element, string name, out JsonElement number)
    {
        number = default;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out number) &&
               number.ValueKind == JsonValueKind.Number;
    }
}
