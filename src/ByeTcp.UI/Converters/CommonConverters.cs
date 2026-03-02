using Microsoft.UI.Xaml.Data;
using Microsoft.UI;
using Windows.UI;

namespace ByeTcp.UI.Converters;

/// <summary>
/// Конвертирует boolean в Color (True=Green, False=Red)
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Colors.Green : Colors.Red;
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертирует boolean в видимость (True=Visible, False=Collapsed)
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue 
                ? Microsoft.UI.Xaml.Visibility.Visible 
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        return Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертирует boolean в инвертированную видимость (True=Collapsed, False=Visible)
/// </summary>
public class BoolNegationToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue 
                ? Microsoft.UI.Xaml.Visibility.Collapsed 
                : Microsoft.UI.Xaml.Visibility.Visible;
        }
        return Microsoft.UI.Xaml.Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Инвертирует boolean значение
/// </summary>
public class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }
}

/// <summary>
/// Конвертирует null в boolean (null=true, не null=false)
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value == null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертирует процент потерь пакетов в Color
/// 0% = Green, <1% = Yellow, <5% = Orange, >=5% = Red
/// </summary>
public class LossToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double loss)
        {
            if (loss <= 0)
                return Colors.Green;
            if (loss < 1)
                return Colors.Yellow;
            if (loss < 5)
                return Colors.Orange;
            return Colors.Red;
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертирует уровень лога в Color
/// </summary>
public class LevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string level)
        {
            return level.ToLowerInvariant() switch
            {
                "information" => Colors.Blue,
                "debug" => Colors.Gray,
                "warning" => Colors.Orange,
                "error" => Colors.Red,
                "critical" => Colors.Purple,
                _ => Colors.Gray
            };
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертирует NetworkQuality в Color
/// </summary>
public class QualityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Используем строковое представление или enum
        var qualityStr = value?.ToString()?.ToLowerInvariant();
        
        return qualityStr switch
        {
            "excellent" or "1" => Colors.Green,
            "good" or "2" => Colors.LimeGreen,
            "fair" or "3" => Colors.Yellow,
            "poor" or "4" => Colors.Orange,
            "critical" or "5" => Colors.Red,
            _ => Colors.Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертирует значение в строку с форматированием
/// </summary>
public class FormatStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null)
            return string.Empty;
        
        var format = parameter as string ?? "{0}";
        return string.Format(format, value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертирует TimeSpan в строку формата HH:MM:SS
/// </summary>
public class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is TimeSpan timeSpan)
        {
            return timeSpan.ToString(@"hh\:mm\:ss");
        }
        return "00:00:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертирует DateTime в строку с форматом
/// </summary>
public class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime dateTime)
        {
            var format = parameter as string ?? "yyyy-MM-dd HH:mm:ss";
            return dateTime.ToString(format);
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертирует double в строку с указанным количеством знаков после запятой
/// </summary>
public class DoubleToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double doubleValue)
        {
            var format = parameter as string ?? "F2";
            return doubleValue.ToString(format);
        }
        return "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
