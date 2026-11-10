using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WorkLogTool;

/// <summary>
/// 值转换器：将数值加1，用于序号显示（从0开始转换为从1开始）
/// </summary>
public class IncrementConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue + 1;
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 状态到背景色转换器
/// </summary>
public class StatusToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string status = value as string;
        if (string.IsNullOrEmpty(status)) return Brushes.Transparent;

        switch (status)
        {
            case "已完成":
                return new SolidColorBrush(Color.FromRgb(200, 230, 201)); // 绿色
            case "进行中":
                return new SolidColorBrush(Color.FromRgb(255, 249, 196)); // 黄色
            case "待办":
                return new SolidColorBrush(Color.FromRgb(187, 222, 251)); // 蓝色
            default:
                return Brushes.Transparent;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 状态和结束时间到背景色转换器（支持超期检测）
/// </summary>
public class StatusToBackgroundMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] == null) return Brushes.Transparent;

        string status = values[0] as string;
        DateTime? endTime = values[1] as DateTime?;

        if (string.IsNullOrEmpty(status)) return Brushes.Transparent;

        // 超期检测：如果结束时间已过且状态不是已完成
        if (endTime.HasValue && endTime.Value < DateTime.Now && status != "已完成")
        {
            return new SolidColorBrush(Color.FromRgb(255, 205, 210)); // 红色（超期）
        }

        switch (status)
        {
            case "已完成":
                return new SolidColorBrush(Color.FromRgb(200, 230, 201)); // 绿色
            case "进行中":
                return new SolidColorBrush(Color.FromRgb(255, 249, 196)); // 黄色
            case "待办":
                return new SolidColorBrush(Color.FromRgb(187, 222, 251)); // 蓝色
            default:
                return Brushes.Transparent;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
