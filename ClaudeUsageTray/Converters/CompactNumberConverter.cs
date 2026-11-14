using System.Globalization;
using System.Windows.Data;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.Converters;

/// <summary>
/// 토큰 수를 "2.5M"·"120.0K" 로 줄여 보인다(#224). 좁은 타일에 "2,500,000" 처럼 쓰면 자릿수 읽기가 어렵고
/// 글자가 타일 폭을 꽉 채워 화면이 빽빽해 보였다. 정확한 값은 툴팁(StringFormat N0)으로 그대로 남긴다.
/// </summary>
public sealed class CompactNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        long l => UsageCalculator.FormatTokenShort(l),
        int i => UsageCalculator.FormatTokenShort(i),
        _ => value?.ToString() ?? "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
