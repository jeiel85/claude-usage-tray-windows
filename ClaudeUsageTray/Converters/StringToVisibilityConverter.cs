using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.Converters;

/// <summary>
/// 빈 문자열이면 Collapsed. ConverterParameter="dash" 를 주면 TokenOrDash 류의
/// "—"(<see cref="Loc.QuotaUnknownMark"/>) 플레이스홀더도 빈 값으로 취급한다.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        var isEmpty = string.IsNullOrEmpty(text)
            || (parameter as string == "dash" && text == Loc.QuotaUnknownMark);
        return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
