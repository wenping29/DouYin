using System;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Data;

namespace MemoryTool.Converters
{
    public class UsageToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double percentage)
            {
                if (percentage < 60)
                {
                    return Brushes.Green;
                }
                else if (percentage < 80)
                {
                    return Brushes.Orange;
                }
                else
                {
                    return Brushes.Red;
                }
            }
            return Brushes.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
