using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VpnClient.App.Converters;

/// <summary>
/// Code pays ISO (« FR ») → drapeau en pinceau, à mettre en fond d'un Border arrondi.
/// Les drapeaux sont dans Resources/Flags ; un code inconnu donne un fond neutre.
/// </summary>
public sealed class FlagBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, Brush> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Brush Unknown = CreateUnknown();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string code ? Get(code) : Unknown;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static bool Exists(string code) => Get(code) != Unknown;

    public static Brush Get(string code)
    {
        if (code.Length != 2 || !code.All(char.IsAsciiLetter)) return Unknown;
        lock (Cache)
        {
            if (Cache.TryGetValue(code, out var cached)) return cached;

            Brush brush;
            try
            {
                var uri = new Uri($"pack://application:,,,/VpnClient.App;component/Resources/Flags/{code.ToLowerInvariant()}.png");
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = uri;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                // UniformToFill : tous les drapeaux ont le même format de badge (la Suisse, carrée, est recadrée).
                brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
            }
            catch (IOException)
            {
                brush = Unknown;
            }

            Cache[code] = brush;
            return brush;
        }
    }

    private static Brush CreateUnknown()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x2A, 0x35, 0x50));
        brush.Freeze();
        return brush;
    }
}
