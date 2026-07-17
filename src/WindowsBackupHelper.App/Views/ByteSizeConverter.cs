using System.Globalization;
using System.Windows.Data;

namespace WindowsBackupHelper.App.Views;

/// <summary>Formats a byte count as a human-readable size (KB/MB/GB/TB, binary/1024-based --
/// matching what Windows Explorer's own "Size" column shows), since a raw byte count is hard to
/// read at multi-gigabyte scale. Uses the current culture's decimal separator (e.g. a comma for
/// fr-FR) via the culture WPF already passes into value converters.</summary>
public sealed class ByteSizeConverter : IValueConverter
{
    public static readonly ByteSizeConverter Instance = new();

    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes)
        {
            return "";
        }

        double size = bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < Units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "N0" : "N1";
        return $"{size.ToString(format, culture)} {Units[unitIndex]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
