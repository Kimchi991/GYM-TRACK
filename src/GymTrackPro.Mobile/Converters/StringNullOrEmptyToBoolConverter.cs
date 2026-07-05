using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace GymTrackPro.Mobile.Converters;

public class StringNullOrEmptyToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasValue = false;
        
        if (value is string str)
        {
            hasValue = !string.IsNullOrWhiteSpace(str);
            if (hasValue && parameter is string paramVal && 
                !paramVal.Equals("invert", StringComparison.OrdinalIgnoreCase))
            {
                return str.Equals(paramVal, StringComparison.OrdinalIgnoreCase);
            }
        }
        else if (value is bool b)
        {
            hasValue = b;
        }
        else if (value != null)
        {
            hasValue = true;
        }

        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            return !hasValue;
        }

        return hasValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
