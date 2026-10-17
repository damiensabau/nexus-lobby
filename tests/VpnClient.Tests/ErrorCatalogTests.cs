using VpnClient.App.Services;
using VpnClient.Core;

namespace VpnClient.Tests;

public class ErrorCatalogTests
{
    public static TheoryData<VpnErrorCode> AllErrors()
    {
        var data = new TheoryData<VpnErrorCode>();
        foreach (var code in Enum.GetValues<VpnErrorCode>().Where(c => c != VpnErrorCode.None)) data.Add(code);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllErrors))]
    public void Every_error_has_a_clear_title_and_message(VpnErrorCode code)
    {
        var error = ErrorCatalog.Describe(code, "Taipei", "détail technique");

        Assert.False(string.IsNullOrWhiteSpace(error.Title));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.True(error.Title.Length <= 45, $"Titre trop long pour la carte : « {error.Title} »");
        if (error.Action != ErrorAction.None) Assert.False(string.IsNullOrWhiteSpace(error.ActionLabel));
        if (error.Action == ErrorAction.OpenUrl) Assert.StartsWith("https://", error.Url);
    }

    [Theory]
    [MemberData(nameof(AllErrors))]
    public void Known_errors_hide_technical_details(VpnErrorCode code)
    {
        if (code == VpnErrorCode.Unknown) return;
        Assert.DoesNotContain("détail technique", ErrorCatalog.Describe(code, null, "détail technique").Message);
    }

    [Fact]
    public void Unknown_error_shows_the_detail_to_help_debugging()
    {
        Assert.Contains("openvpn.exe a planté", ErrorCatalog.Describe(VpnErrorCode.Unknown, null, "openvpn.exe a planté").Message);
        Assert.DoesNotContain("Détail", ErrorCatalog.Describe(VpnErrorCode.Unknown).Message);
    }

    [Fact]
    public void Server_name_appears_when_relevant()
    {
        Assert.Equal("Impossible de joindre Taipei", ErrorCatalog.Describe(VpnErrorCode.ServerUnreachable, "Taipei").Title);
        Assert.Equal("Impossible de joindre le serveur", ErrorCatalog.Describe(VpnErrorCode.ServerUnreachable).Title);
        Assert.Contains("à Taipei", ErrorCatalog.Describe(VpnErrorCode.TunnelLost, "Taipei").Message);
    }

    [Theory]
    [InlineData(VpnErrorCode.WireGuardMissing, ErrorCatalog.WireGuardUrl)]
    [InlineData(VpnErrorCode.OpenVpnMissing, ErrorCatalog.OpenVpnUrl)]
    public void Missing_software_offers_a_download(VpnErrorCode code, string url)
    {
        var error = ErrorCatalog.Describe(code);
        Assert.Equal(ErrorAction.OpenUrl, error.Action);
        Assert.Equal(url, error.Url);
    }

    [Fact]
    public void Country_names_are_in_french()
    {
        Assert.Equal("Taïwan", NetworkProbe.CountryName("TW"));
        Assert.Equal("France", NetworkProbe.CountryName("fr"));
        Assert.Equal("ZZ", NetworkProbe.CountryName("zz"));
    }
}
