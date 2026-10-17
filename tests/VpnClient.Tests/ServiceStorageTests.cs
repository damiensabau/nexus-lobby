using System.IO;
using System.Text;
using Microsoft.Extensions.Options;
using VpnClient.Core;
using VpnClient.Service;

namespace VpnClient.Tests;

public sealed class ServiceStorageTests : IDisposable
{
    // Clé d'exemple publique (documentation de wg(8)) : ne protège aucun vrai serveur.
    private const string WireGuard = """
        [Interface]
        PrivateKey = yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk=
        Address = 10.66.0.2/32

        [Peer]
        PublicKey = xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=
        AllowedIPs = 0.0.0.0/0
        Endpoint = vpn.example.com:51820
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vpnclient-tests-" + Guid.NewGuid().ToString("N"));

    private IOptions<VpnOptions> Options() => Microsoft.Extensions.Options.Options.Create(new VpnOptions { DataDirectory = _dir });
    private ProfileStore NewStore() => new(Options());
    private CredentialStore NewCredentials() => new(Options(), NewStore());

    // ───────── Port du serveur (ping de secours) ─────────

    [Theory]
    [InlineData(VpnProtocol.WireGuard, "Endpoint = vpn.example.com:51820", 51820)]
    [InlineData(VpnProtocol.WireGuard, "Endpoint = [2001:db8::1]:443", 443)]
    [InlineData(VpnProtocol.WireGuard, "Endpoint = vpn.example.com", null)]
    [InlineData(VpnProtocol.OpenVpn, "remote vpn.example.com 443 tcp", 443)]
    [InlineData(VpnProtocol.OpenVpn, "remote vpn.example.com", 1194)]      // port OpenVPN par défaut
    [InlineData(VpnProtocol.OpenVpn, "remote vpn.example.com 99999", 1194)] // port invalide
    public void Reads_the_server_port(VpnProtocol protocol, string line, int? expected) =>
        Assert.Equal(expected, ProfileStore.ParseServerPort(protocol, line));

    [Fact]
    public void Import_keeps_port_and_expiration_and_they_persist()
    {
        var expires = new DateTime(2026, 11, 3, 18, 45, 0);
        var p = NewStore().Import("Free", "free.conf", WireGuard, "jp-tyo", expires);

        Assert.Equal(51820, p.ServerPort);
        Assert.Equal(new DateTime(2026, 11, 3), p.ExpiresAt); // la date seule, sans l'heure

        var reloaded = NewStore().Get(p.Id)!;
        Assert.Equal(51820, reloaded.ServerPort);
        Assert.Equal(new DateTime(2026, 11, 3), reloaded.ExpiresAt);
    }

    [Fact]
    public void Expiration_can_be_changed_and_removed()
    {
        var store = NewStore();
        var p = store.Import("Free", "free.conf", WireGuard, null);
        Assert.Null(p.ExpiresAt);

        store.SetExpiration(p.Id, new DateTime(2026, 12, 1));
        Assert.Equal(new DateTime(2026, 12, 1), NewStore().Get(p.Id)!.ExpiresAt);

        store.SetExpiration(p.Id, null);
        Assert.Null(NewStore().Get(p.Id)!.ExpiresAt);
    }

    [Fact]
    public void Older_profiles_get_their_port_back_on_load()
    {
        var store = NewStore();
        var p = store.Import("Ancien", "ancien.conf", WireGuard, null);

        // Simule un profils.json d'avant la version qui enregistre le port.
        var index = Path.Combine(_dir, "profiles", "profiles.json");
        File.WriteAllText(index, File.ReadAllText(index).Replace("\"serverPort\":51820", "\"serverPort\":null"));

        Assert.Equal(51820, NewStore().Get(p.Id)!.ServerPort);
    }

    // ───────── Identifiants mémorisés (DPAPI) ─────────

    [Fact]
    public void Credentials_round_trip_and_are_not_stored_in_clear()
    {
        var creds = NewCredentials();
        creds.Save("tokyo-1", "joueur42", "MotDePasse-Tres-Secret!");

        Assert.True(creds.Has("tokyo-1"));
        Assert.Equal(("joueur42", "MotDePasse-Tres-Secret!"), NewCredentials().Load("tokyo-1"));

        var raw = File.ReadAllBytes(Path.Combine(_dir, "credentials", "tokyo-1.bin"));
        var asText = Encoding.UTF8.GetString(raw) + Encoding.Unicode.GetString(raw);
        Assert.DoesNotContain("MotDePasse-Tres-Secret!", asText);
        Assert.DoesNotContain("joueur42", asText);
    }

    [Fact]
    public void Forget_removes_credentials()
    {
        var creds = NewCredentials();
        creds.Save("tokyo-1", "u", "p");
        creds.Forget("tokyo-1");

        Assert.False(creds.Has("tokyo-1"));
        Assert.Null(creds.Load("tokyo-1"));
        creds.Forget("tokyo-1"); // sans effet, pas d'erreur
    }

    [Fact]
    public void Corrupted_credentials_are_ignored_and_deleted()
    {
        var creds = NewCredentials();
        File.WriteAllBytes(Path.Combine(_dir, "credentials", "abime.bin"), [1, 2, 3, 4]);

        Assert.Null(creds.Load("abime"));
        Assert.False(creds.Has("abime"));
    }

    [Theory]
    [InlineData("..\\..\\Windows\\evil")]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("")]
    public void Credential_paths_cannot_escape_their_folder(string id) =>
        Assert.Throws<ArgumentException>(() => NewCredentials().Save(id, "u", "p"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
