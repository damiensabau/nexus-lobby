using System.IO;
using System.Text.RegularExpressions;

namespace VpnClient.App.Services;

public sealed record Location(
    string Id, string City, string Country, string CountryCode, string Region, double Latitude, double Longitude, string TimeZone)
{
    public string Label => $"{City}, {Country}";

    public DateTime? LocalTime => LocalTimeAt(DateTime.UtcNow);

    public DateTime? LocalTimeAt(DateTime utc)
    {
        try
        {
            return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
    }

    // Même projection que Resources/WorldMap.xaml (équirectangulaire 1000 x 394.4, latitudes 84 à -58).
    public const double MapWidth = 1000;
    public const double MapHeight = 394.444;

    public double MapX => (Longitude + 180) / 360 * MapWidth;
    public double MapY => (84 - Latitude) / 142 * MapHeight;
}

public static partial class Locations
{
    public static IReadOnlyList<Location> All { get; } =
    [
        // Europe
        new("fr-par", "Paris", "France", "FR", "Europe", 48.86, 2.35, "Europe/Paris"),
        new("fr-mrs", "Marseille", "France", "FR", "Europe", 43.30, 5.37, "Europe/Paris"),
        new("gb-lon", "Londres", "Royaume-Uni", "GB", "Europe", 51.51, -0.13, "Europe/London"),
        new("de-fra", "Francfort", "Allemagne", "DE", "Europe", 50.11, 8.68, "Europe/Berlin"),
        new("nl-ams", "Amsterdam", "Pays-Bas", "NL", "Europe", 52.37, 4.90, "Europe/Amsterdam"),
        new("be-bru", "Bruxelles", "Belgique", "BE", "Europe", 50.85, 4.35, "Europe/Brussels"),
        new("ch-zrh", "Zurich", "Suisse", "CH", "Europe", 47.38, 8.54, "Europe/Zurich"),
        new("es-mad", "Madrid", "Espagne", "ES", "Europe", 40.42, -3.70, "Europe/Madrid"),
        new("pt-lis", "Lisbonne", "Portugal", "PT", "Europe", 38.72, -9.14, "Europe/Lisbon"),
        new("it-mil", "Milan", "Italie", "IT", "Europe", 45.46, 9.19, "Europe/Rome"),
        new("ie-dub", "Dublin", "Irlande", "IE", "Europe", 53.35, -6.26, "Europe/Dublin"),
        new("se-sto", "Stockholm", "Suède", "SE", "Europe", 59.33, 18.07, "Europe/Stockholm"),
        new("no-osl", "Oslo", "Norvège", "NO", "Europe", 59.91, 10.75, "Europe/Oslo"),
        new("fi-hel", "Helsinki", "Finlande", "FI", "Europe", 60.17, 24.94, "Europe/Helsinki"),
        new("pl-waw", "Varsovie", "Pologne", "PL", "Europe", 52.23, 21.01, "Europe/Warsaw"),
        new("ro-buh", "Bucarest", "Roumanie", "RO", "Europe", 44.43, 26.10, "Europe/Bucharest"),
        new("tr-ist", "Istanbul", "Turquie", "TR", "Europe", 41.01, 28.98, "Europe/Istanbul"),
        new("ua-iev", "Kiev", "Ukraine", "UA", "Europe", 50.45, 30.52, "Europe/Kiev"),
        // Amérique du Nord
        new("us-nyc", "New York", "États-Unis", "US", "Amérique du Nord", 40.71, -74.01, "America/New_York"),
        new("us-ash", "Ashburn", "États-Unis", "US", "Amérique du Nord", 39.04, -77.49, "America/New_York"),
        new("us-mia", "Miami", "États-Unis", "US", "Amérique du Nord", 25.76, -80.19, "America/New_York"),
        new("us-chi", "Chicago", "États-Unis", "US", "Amérique du Nord", 41.88, -87.63, "America/Chicago"),
        new("us-dal", "Dallas", "États-Unis", "US", "Amérique du Nord", 32.78, -96.80, "America/Chicago"),
        new("us-den", "Denver", "États-Unis", "US", "Amérique du Nord", 39.74, -104.99, "America/Denver"),
        new("us-lax", "Los Angeles", "États-Unis", "US", "Amérique du Nord", 34.05, -118.24, "America/Los_Angeles"),
        new("us-sea", "Seattle", "États-Unis", "US", "Amérique du Nord", 47.61, -122.33, "America/Los_Angeles"),
        new("ca-mtl", "Montréal", "Canada", "CA", "Amérique du Nord", 45.50, -73.57, "America/Toronto"),
        new("ca-tor", "Toronto", "Canada", "CA", "Amérique du Nord", 43.65, -79.38, "America/Toronto"),
        new("ca-van", "Vancouver", "Canada", "CA", "Amérique du Nord", 49.28, -123.12, "America/Vancouver"),
        new("mx-mex", "Mexico", "Mexique", "MX", "Amérique du Nord", 19.43, -99.13, "America/Mexico_City"),
        // Amérique du Sud
        new("br-sao", "São Paulo", "Brésil", "BR", "Amérique du Sud", -23.55, -46.63, "America/Sao_Paulo"),
        new("ar-bue", "Buenos Aires", "Argentine", "AR", "Amérique du Sud", -34.60, -58.38, "America/Argentina/Buenos_Aires"),
        new("cl-scl", "Santiago", "Chili", "CL", "Amérique du Sud", -33.45, -70.67, "America/Santiago"),
        new("co-bog", "Bogotá", "Colombie", "CO", "Amérique du Sud", 4.71, -74.07, "America/Bogota"),
        new("pe-lim", "Lima", "Pérou", "PE", "Amérique du Sud", -12.05, -77.04, "America/Lima"),
        // Afrique / Moyen-Orient
        new("za-jnb", "Johannesburg", "Afrique du Sud", "ZA", "Afrique", -26.20, 28.05, "Africa/Johannesburg"),
        new("eg-cai", "Le Caire", "Égypte", "EG", "Afrique", 30.04, 31.24, "Africa/Cairo"),
        new("ma-cas", "Casablanca", "Maroc", "MA", "Afrique", 33.57, -7.59, "Africa/Casablanca"),
        new("ng-los", "Lagos", "Nigeria", "NG", "Afrique", 6.52, 3.38, "Africa/Lagos"),
        new("ke-nbo", "Nairobi", "Kenya", "KE", "Afrique", -1.29, 36.82, "Africa/Nairobi"),
        new("gh-acc", "Accra", "Ghana", "GH", "Afrique", 5.60, -0.19, "Africa/Accra"),
        new("ae-dxb", "Dubaï", "Émirats arabes unis", "AE", "Moyen-Orient", 25.20, 55.27, "Asia/Dubai"),
        new("sa-ruh", "Riyad", "Arabie saoudite", "SA", "Moyen-Orient", 24.71, 46.68, "Asia/Riyadh"),
        new("il-tlv", "Tel Aviv", "Israël", "IL", "Moyen-Orient", 32.09, 34.78, "Asia/Jerusalem"),
        // Asie / Océanie
        new("in-bom", "Mumbai", "Inde", "IN", "Asie", 19.08, 72.88, "Asia/Kolkata"),
        new("pk-khi", "Karachi", "Pakistan", "PK", "Asie", 24.86, 67.01, "Asia/Karachi"),
        new("bd-dac", "Dacca", "Bangladesh", "BD", "Asie", 23.81, 90.41, "Asia/Dhaka"),
        new("kg-fru", "Bichkek", "Kirghizistan", "KG", "Asie", 42.87, 74.59, "Asia/Bishkek"),
        new("vn-han", "Hanoï", "Viêt Nam", "VN", "Asie", 21.03, 105.85, "Asia/Ho_Chi_Minh"),
        new("kh-pnh", "Phnom Penh", "Cambodge", "KH", "Asie", 11.56, 104.92, "Asia/Phnom_Penh"),
        new("sg-sin", "Singapour", "Singapour", "SG", "Asie", 1.35, 103.82, "Asia/Singapore"),
        new("hk-hkg", "Hong Kong", "Hong Kong", "HK", "Asie", 22.32, 114.17, "Asia/Hong_Kong"),
        new("jp-tyo", "Tokyo", "Japon", "JP", "Asie", 35.68, 139.69, "Asia/Tokyo"),
        new("kr-sel", "Séoul", "Corée du Sud", "KR", "Asie", 37.57, 126.98, "Asia/Seoul"),
        new("tw-tpe", "Taipei", "Taïwan", "TW", "Asie", 25.03, 121.57, "Asia/Taipei"),
        new("th-bkk", "Bangkok", "Thaïlande", "TH", "Asie", 13.76, 100.50, "Asia/Bangkok"),
        new("id-jkt", "Jakarta", "Indonésie", "ID", "Asie", -6.21, 106.85, "Asia/Jakarta"),
        new("ph-mnl", "Manille", "Philippines", "PH", "Asie", 14.60, 120.98, "Asia/Manila"),
        new("au-syd", "Sydney", "Australie", "AU", "Océanie", -33.87, 151.21, "Australia/Sydney"),
        new("au-per", "Perth", "Australie", "AU", "Océanie", -31.95, 115.86, "Australia/Perth"),
        new("nz-akl", "Auckland", "Nouvelle-Zélande", "NZ", "Océanie", -36.85, 174.76, "Pacific/Auckland"),
    ];

    public static Location? Find(string? id) => id is null ? null : All.FirstOrDefault(l => l.Id == id);

    /// <summary>
    /// Devine l'emplacement depuis le nom du fichier : "fr-par-wg-001.conf" (Mullvad), "FR#12.conf" (Proton),
    /// "paris.ovpn", "us-chicago.conf"…
    /// </summary>
    public static Location? Guess(string fileName)
    {
        var name = RemoveAccents(Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant());
        var tokens = Tokens().Split(name).Where(t => t.Length > 0).ToArray();

        // 1. Identifiant exact ("fr-par") ou paire pays + code ville.
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            if (Find($"{tokens[i]}-{tokens[i + 1]}") is { } exact) return exact;
        }

        // 2. Nom de ville en clair.
        foreach (var location in All)
        {
            var city = RemoveAccents(location.City.ToLowerInvariant()).Replace(" ", "");
            if (name.Replace(" ", "").Replace("-", "").Replace("_", "").Contains(city)) return location;
        }

        // 3. Code pays seul : première ville de ce pays.
        foreach (var token in tokens.Where(t => t.Length == 2))
        {
            var byCountry = All.FirstOrDefault(l => l.CountryCode.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (byCountry is not null) return byCountry;
        }

        return null;
    }

    private static string RemoveAccents(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
            != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }

    [GeneratedRegex(@"[^a-z]+")]
    private static partial Regex Tokens();
}
