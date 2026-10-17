using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VpnClient.Core;

namespace VpnClient.App.Converters;

public sealed class StateToBrushConverter : IValueConverter
{
    // Région d'origine = neutre, en cours = orange, région changée = vert, erreur = rouge.
    private static readonly Brush Disconnected = Freeze(new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)));
    private static readonly Brush Busy = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)));
    private static readonly Brush Connected = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)));
    private static readonly Brush Error = Freeze(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ConnectionState.Connected => Connected,
        ConnectionState.Connecting or ConnectionState.Disconnecting => Busy,
        ConnectionState.Error => Error,
        _ => Disconnected,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
