using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MemoryTool.Converters
{
    public class PercentageToPieConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || !(values[0] is double percentage) || percentage <= 0)
            {
                return Geometry.Empty;
            }

            if (percentage >= 100)
            {
                return new EllipseGeometry(new Point(50, 50), 50, 50);
            }

            double angle = percentage / 100 * 360;
            double radians = angle * Math.PI / 180;

            double centerX = 50;
            double centerY = 50;
            double radius = 50;

            double endX = centerX + radius * Math.Sin(radians);
            double endY = centerY - radius * Math.Cos(radians);

            bool isLargeArc = angle > 180;

            PathFigure figure = new PathFigure
            {
                StartPoint = new Point(centerX, centerY),
                Segments =
                {
                    new LineSegment(new Point(centerX, centerY - radius), true),
                    new ArcSegment
                    {
                        Point = new Point(endX, endY),
                        Size = new Size(radius, radius),
                        IsLargeArc = isLargeArc,
                        SweepDirection = SweepDirection.Clockwise
                    },
                    new LineSegment(new Point(centerX, centerY), true)
                }
            };

            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return geometry;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
