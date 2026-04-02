namespace MemoryWatchDogApp.Converters
{
    using System;
    using System.Globalization;
    using System.Windows.Data;
    using MemoryWatchDog;

    public class FormatBytesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long l)
                return CommonUtil.FormatBytes(l);
            if (value is ulong ul)
                return CommonUtil.FormatBytes((long)ul);
            if (value is int i)
                return CommonUtil.FormatBytes(i);

            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
