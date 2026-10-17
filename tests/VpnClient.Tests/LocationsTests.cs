using VpnClient.App.Services;

namespace VpnClient.Tests;

public class LocationsTests
{
    [Fact]
    public void Ids_are_unique()
    {
        var duplicates = Locations.All.GroupBy(l => l.Id).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_time_zone_resolves_on_this_machine()
    {
        var broken = Locations.All.Where(l => l.LocalTime is null).Select(l => $"{l.Id} ({l.TimeZone})");
        Assert.Empty(broken);
    }

    [Fact]
    public void Map_coordinates_stay_inside_the_map()
    {
        foreach (var l in Locations.All)
        {
            Assert.InRange(l.MapX, 0, Location.MapWidth);
            Assert.InRange(l.MapY, 0, Location.MapHeight);
        }
    }

    [Theory]
    [InlineData("fr-par-wg-001.conf", "fr-par")]   // Mullvad
    [InlineData("us-chi-wg-101.conf", "us-chi")]
    [InlineData("Paris.ovpn", "fr-par")]           // ville en clair
    [InlineData("São Paulo.ovpn", "br-sao")]       // accents
    [InlineData("sao_paulo-02.conf", "br-sao")]
    [InlineData("DE#12.conf", "de-fra")]           // Proton : code pays seul
    [InlineData("vpn-tokyo-3.conf", "jp-tyo")]
    public void Guess_finds_location_from_file_name(string fileName, string expectedId) =>
        Assert.Equal(expectedId, Locations.Guess(fileName)?.Id);

    [Fact]
    public void Guess_returns_null_when_nothing_matches() =>
        Assert.Null(Locations.Guess("mon-serveur.conf"));
}
