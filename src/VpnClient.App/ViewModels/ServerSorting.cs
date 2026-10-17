using System.Collections;

namespace VpnClient.App.ViewModels;

public enum SortMode
{
    Score,  // meilleur score « lobby facile » d'abord (par défaut)
    Ping,   // ping le plus bas d'abord
    Name,   // pays et villes de A à Z
}

/// <summary>
/// Ordre d'affichage de la liste. Les dossiers suivent l'ordre de leurs serveurs : un pays est placé selon son
/// meilleur serveur, une ville aussi. Les clés sont précalculées par <see cref="ApplyKeys"/>.
/// </summary>
public sealed class ServerDisplayComparer : IComparer, IComparer<ServerItem>
{
    public static readonly ServerDisplayComparer Instance = new();

    public int Compare(object? x, object? y) => Compare(x as ServerItem, y as ServerItem);

    public int Compare(ServerItem? a, ServerItem? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return 1;
        if (b is null) return -1;

        int c;
        if ((c = b.CountrySortKey.CompareTo(a.CountrySortKey)) != 0) return c;          // pays : meilleur d'abord
        if ((c = string.Compare(a.CountryName, b.CountryName, StringComparison.CurrentCulture)) != 0) return c;
        if ((c = b.CitySortKey.CompareTo(a.CitySortKey)) != 0) return c;                // ville : meilleure d'abord
        if ((c = string.Compare(a.CityGroup, b.CityGroup, StringComparison.CurrentCulture)) != 0) return c;
        if ((c = b.ItemSortKey.CompareTo(a.ItemSortKey)) != 0) return c;                // serveur : meilleur d'abord
        return string.Compare(a.ListTitle, b.ListTitle, StringComparison.CurrentCulture);
    }

    /// <summary>Calcule les clés de tri (plus grand = plus haut dans la liste) selon le mode choisi.</summary>
    public static void ApplyKeys(IReadOnlyCollection<ServerItem> servers, SortMode mode)
    {
        foreach (var s in servers) s.ItemSortKey = ItemKey(s, mode);

        foreach (var country in servers.GroupBy(s => s.CountryGroup))
        {
            // La région inconnue reste toujours en bas.
            var countryKey = country.Key == CountryGroupKey.Unknown ? int.MinValue : country.Max(s => s.ItemSortKey);
            foreach (var s in country) s.CountrySortKey = countryKey;

            foreach (var city in country.GroupBy(s => s.CityGroup))
            {
                var cityKey = city.Max(s => s.ItemSortKey);
                foreach (var s in city) s.CitySortKey = cityKey;
            }
        }
    }

    private static int ItemKey(ServerItem s, SortMode mode) => mode switch
    {
        SortMode.Score => s.LobbyValue,                                   // -1 si région inconnue
        SortMode.Ping => s.PingMs is { } ms ? -(int)Math.Min(ms, 99_999) : -100_000, // sans ping = en bas
        _ => 0,                                                           // A→Z : départage par les noms
    };
}
