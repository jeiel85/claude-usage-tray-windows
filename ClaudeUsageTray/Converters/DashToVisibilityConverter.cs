using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClaudeUsageTray.Converters;

/// <summary>
/// TokenOrDash 류 라벨("—" 플레이스홀더)을 받아, 실제 표시할 값이 없으면 Collapsed 로 접는다.
/// </summary>
public class DashToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        return string.IsNullOrEmpty(text) || text == "—" ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
