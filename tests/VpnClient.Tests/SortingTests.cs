using VpnClient.App.Services;
using VpnClient.App.ViewModels;
using VpnClient.Core;

namespace VpnClient.Tests;

public class SortingTests
{
    // Mercredi 14 octobre 2026, 02:00 UTC : nuit en Asie (bonnes notes), soirée aux États-Unis (mauvaises notes).
    private static readonly DateTime NightInAsia = new(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc);

    private static ServerItem Server(string id, string? locationId, long? ping = 50)
    {
        var item = new ServerItem(new VpnProfile { Id = id, Name = id, Protocol = VpnProtocol.WireGuard, LocationId = locationId })
        {
            PingMs = ping,
        };
        item.Lobby = item.Location is null ? null : LobbyAdvisor.Score(item.Location, ping, NightInAsia);
        return item;
    }

    private static List<string> Order(IReadOnlyList<ServerItem> servers, SortMode mode)
    {
        MainViewModel.AssignFolders(servers);
        ServerDisplayComparer.ApplyKeys(servers, mode);
        return servers.Order(ServerDisplayComparer.Instance).Select(s => s.Profile.Id).ToList();
    }

    [Fact]
    public void Default_order_is_best_score_first_for_countries_and_servers()
    {
        var servers = new List<ServerItem>
        {
            Server("us-nyc", "us-nyc"),   // soirée, beaucoup de joueurs
            Server("tw-tpe", "tw-tpe"),   // nuit, peu de joueurs
            Server("fr-par", "fr-par"),
            Server("jp-tyo", "jp-tyo"),
            Server("unknown", null),
        };

        var order = Order(servers, SortMode.Score);
        var scores = order.Take(4).Select(id => servers.Single(s => s.Profile.Id == id).LobbyValue).ToList();

        Assert.Equal(scores.OrderByDescending(v => v), scores); // décroissant
        Assert.Equal("tw-tpe", order[0]);
        Assert.Equal("unknown", order[^1]);                     // région inconnue toujours en bas
    }

    [Fact]
    public void Country_folder_is_placed_by_its_best_server_and_keeps_servers_together()
    {
        var servers = new List<ServerItem>
        {
            Server("jp-tyo", "jp-tyo"),
            Server("us-nyc", "us-nyc"),
            Server("us-lax", "us-lax"),
            Server("tw-tpe", "tw-tpe"),
        };

        var order = Order(servers, SortMode.Score);

        // Les deux serveurs américains restent côte à côte (même dossier), même si l'un est moins bien noté.
        Assert.Equal(1, Math.Abs(order.IndexOf("us-nyc") - order.IndexOf("us-lax")));
        Assert.True(order.IndexOf("tw-tpe") < order.IndexOf("us-nyc"));
    }

    [Fact]
    public void Ping_mode_puts_lowest_ping_first_and_unknown_ping_last()
    {
        var servers = new List<ServerItem>
        {
            Server("a", "fr-par", 80),
            Server("b", "de-fra", 12),
            Server("c", "jp-tyo", null),
            Server("d", "gb-lon", 35),
        };

        Assert.Equal(["b", "d", "a", "c"], Order(servers, SortMode.Ping));
    }

    [Fact]
    public void Name_mode_sorts_countries_alphabetically()
    {
        var servers = new List<ServerItem>
        {
            Server("jp", "jp-tyo"),   // Japon
            Server("de", "de-fra"),   // Allemagne
            Server("fr", "fr-par"),   // France
            Server("us", "us-nyc"),   // États-Unis
        };

        // Ordre français : Allemagne, États-Unis (É ≈ E), France, Japon.
        Assert.Equal(["de", "us", "fr", "jp"], Order(servers, SortMode.Name));
    }
}
