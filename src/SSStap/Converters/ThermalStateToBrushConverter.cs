using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SSStap.Converters;

public class ThermalStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int state)
        {
            return state switch
            {
                0 => Brushes.Green,      // Nominal
                1 => Brushes.Goldenrod,  // Fair
                2 => Brushes.OrangeRed,  // Serious
                3 => Brushes.Red,        // Critical
                _ => Brushes.Gray
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
