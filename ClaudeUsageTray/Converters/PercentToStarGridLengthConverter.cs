using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClaudeUsageTray.Converters;

/// <summary>
/// 0~1 비율을 Grid ColumnDefinition 의 star <see cref="GridLength"/> 로 변환한다.
/// 시간 진행률 마커를 막대 위 정확한 비율 위치에 세우기 위한 용도로,
/// 두 개의 star 컬럼(비율 : 1-비율)으로 나눈 뒤 경계에 마커를 놓는다.
/// ConverterParameter="inverse" 이면 (1 - 비율) 을 반환해 나머지 컬럼 폭을 만든다.
/// </summary>
public class PercentToStarGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double v = value is double d ? d : 0;
        v = Math.Clamp(v, 0, 1);
        if (parameter as string == "inverse") v = 1 - v;
        return new GridLength(v, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
