using System.Globalization;
using System.Windows.Data;

namespace WindowsBackupHelper.App.Views;

/// <summary>Used to show a checkbox reflecting whether an optional override object has been created yet.</summary>
public sealed class NullToBoolConverter : IValueConverter
{
    public static readonly NullToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
