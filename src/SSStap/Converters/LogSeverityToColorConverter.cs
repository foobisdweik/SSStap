using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SSStap.Services;

namespace SSStap.Converters;

public class LogSeverityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not LogSeverity severity)
            return new SolidColorBrush(Colors.LightGray);

        return severity switch
        {
            LogSeverity.Error => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
            LogSeverity.Success => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            LogSeverity.Section => new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),
            LogSeverity.Info => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
            _ => new SolidColorBrush(Colors.LightGray)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
