using System.IO;
using System.Text.RegularExpressions;
using VpnClient.App.Services;
using VpnClient.App.ViewModels;
using VpnClient.Core;

namespace VpnClient.Tests;

public class FoldersAndMapTests
{
    private static ServerItem Server(string name, string? locationId) =>
        new(new VpnProfile { Id = name, Name = name, Protocol = VpnProtocol.WireGuard, LocationId = locationId });

    [Fact]
    public void Single_server_country_has_no_city_folder()
    {
        var tokyo = Server("jp-1", "jp-tyo");
        MainViewModel.AssignFolders([tokyo]);

        Assert.Equal("", tokyo.CityGroup);
        Assert.Equal(new CountryGroupKey("JP", "Japon"), tokyo.CountryGroup);
        Assert.Equal("Tokyo", tokyo.ListTitle);
        Assert.Equal("WireGuard", tokyo.ListSubtitle);
    }

    [Fact]
    public void Several_servers_in_a_country_get_city_folders()
    {
        var paris1 = Server("fr-par-wg-001", "fr-par");
        var paris2 = Server("fr-par-wg-002", "fr-par");
        var marseille = Server("fr-mrs-wg-001", "fr-mrs");
        var tokyo = Server("jp-1", "jp-tyo");
        MainViewModel.AssignFolders([paris1, paris2, marseille, tokyo]);

        Assert.Equal("Paris", paris1.CityGroup);
        Assert.Equal("Paris", paris2.CityGroup);
        Assert.Equal("Marseille", marseille.CityGroup);
        Assert.Equal("", tokyo.CityGroup);

        // Deux serveurs dans la même ville : on les distingue par le nom du fichier.
        Assert.True(paris1.SharesCity);
        Assert.Equal("fr-par-wg-001", paris1.ListTitle);
        Assert.Equal("Paris · WireGuard", paris1.ListSubtitle);
        Assert.False(marseille.SharesCity);
        Assert.Equal("Marseille", marseille.ListTitle);
    }

    [Fact]
    public void Servers_without_location_go_to_unknown_folder_without_subfolders()
    {
        var a = Server("a", null);
        var b = Server("b", null);
        MainViewModel.AssignFolders([a, b]);

        Assert.Equal(CountryGroupKey.Unknown, a.CountryGroup);
        Assert.Equal("", a.CityGroup);
        Assert.False(a.SharesCity);
        Assert.Equal("a", a.ListTitle);
    }

    [Fact]
    public void Every_catalog_country_can_be_highlighted_except_city_states()
    {
        var map = File.ReadAllText(Path.Combine(RepoRoot(), "src", "VpnClient.App", "Resources", "WorldMap.xaml"));
        var keys = Regex.Matches(map, @"x:Key=""Country\.([A-Z]{2})""").Select(m => m.Groups[1].Value).ToHashSet();

        // Singapour et Hong Kong n'existent pas à l'échelle 1:110m : repère seulement.
        var missing = Locations.All.Select(l => l.CountryCode).Distinct().Where(c => !keys.Contains(c)).Order();
        Assert.Equal(["HK", "SG"], missing);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VpnClient.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("VpnClient.sln introuvable");
    }
}
