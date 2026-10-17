using System.IO;
using Microsoft.Extensions.Options;
using VpnClient.Core;
using VpnClient.Service;

namespace VpnClient.Tests;

public sealed class ProfileStoreTests : IDisposable
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

    private const string OpenVpn = """
        client
        dev tun
        remote 203.0.113.10 1194 udp
        auth-user-pass
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vpnclient-tests-" + Guid.NewGuid().ToString("N"));

    private ProfileStore NewStore() => new(Options.Create(new VpnOptions { DataDirectory = _dir }));

    [Fact]
    public void Imports_wireguard_and_reads_endpoint_host()
    {
        var profile = NewStore().Import("Paris 1", "fr-par-wg-001.conf", WireGuard, "fr-par");

        Assert.Equal(VpnProtocol.WireGuard, profile.Protocol);
        Assert.Equal("vpn.example.com", profile.ServerHost);
        Assert.Equal("fr-par", profile.LocationId);
        Assert.Equal("paris-1", profile.Id);
        Assert.False(profile.RequiresCredentials);
    }

    [Fact]
    public void Reads_ipv6_endpoint()
    {
        var conf = WireGuard.Replace("vpn.example.com:51820", "[2001:db8::1]:51820");
        Assert.Equal("2001:db8::1", NewStore().Import("v6", "v6.conf", conf, null).ServerHost);
    }

    [Fact]
    public void Imports_openvpn_and_detects_credentials()
    {
        var profile = NewStore().Import("Tokyo", "jp.ovpn", OpenVpn, "jp-tyo");

        Assert.Equal(VpnProtocol.OpenVpn, profile.Protocol);
        Assert.Equal("203.0.113.10", profile.ServerHost);
        Assert.True(profile.RequiresCredentials);
    }

    [Theory]
    [InlineData("script-security 2\nup \"cmd.exe /c whoami\"")]
    [InlineData("plugin evil.dll")]
    [InlineData("  --up run.bat")]
    public void Rejects_openvpn_directives_that_run_code(string extra)
    {
        var ex = Assert.Throws<VpnException>(() =>
            NewStore().Import("x", "x.ovpn", OpenVpn + "\n" + extra, null));
        Assert.Equal(VpnErrorCode.ForbiddenDirective, ex.Code);
        Assert.Contains("n'est pas autorisée", ex.Message);
    }

    [Fact]
    public void Accepts_a_typical_provider_profile_with_inline_blocks()
    {
        // Le contenu des blocs ne doit jamais être lu comme des directives (« up », « log »… en clair dedans).
        const string profile = """
            client
            dev tun
            proto udp
            remote vpn.example.com 1194
            auth-user-pass
            key-direction 1
            http-proxy proxy.example.com 8080 auto
            <ca>
            -----BEGIN CERTIFICATE-----
            up
            log C:\Windows\evil.txt
            -----END CERTIFICATE-----
            </ca>
            <tls-auth>
            -----BEGIN OpenVPN Static key V1-----
            0123456789abcdef
            -----END OpenVPN Static key V1-----
            </tls-auth>
            tls-crypt [inline]
            """;

        var imported = NewStore().Import("Provider", "provider.ovpn", profile, null);
        Assert.True(imported.RequiresCredentials);
    }

    [Theory]
    [InlineData("ca C:\\Windows\\System32\\config\\SAM")]
    [InlineData("auth-user-pass identifiants.txt")]
    [InlineData("tls-auth ta.key 1")]
    [InlineData("pkcs12 \"C:\\certs\\moi.p12\"")]
    public void Rejects_references_to_files_on_disk(string line)
    {
        var ex = Assert.Throws<VpnException>(() => NewStore().Import("x", "x.ovpn", OpenVpn + "\n" + line, null));
        Assert.Equal(VpnErrorCode.ForbiddenDirective, ex.Code);
        Assert.Contains("intègre son contenu", ex.Message);
    }

    [Theory]
    [InlineData("engine dynamic")]
    [InlineData("providers legacy default")]
    [InlineData("pkcs11-providers C:\\evil.dll")]
    [InlineData("win-sys C:\\Users\\Public\\fake")]
    [InlineData("replay-persist C:\\Windows\\out.txt")]
    [InlineData("http-proxy proxy.example.com 8080 C:\\creds.txt")]
    public void Rejects_directives_that_load_code_or_write_files(string line) =>
        Assert.Equal(VpnErrorCode.ForbiddenDirective,
            Assert.Throws<VpnException>(() => NewStore().Import("x", "x.ovpn", OpenVpn + "\n" + line, null)).Code);

    [Fact]
    public void Rejects_wireguard_scripts()
    {
        var conf = WireGuard.Replace("Address = 10.66.0.2/32", "Address = 10.66.0.2/32\nPostUp = calc.exe");
        var ex = Assert.Throws<VpnException>(() => NewStore().Import("x", "x.conf", conf, null));
        Assert.Equal(VpnErrorCode.ForbiddenDirective, ex.Code);
    }

    [Fact]
    public void Rejects_unknown_extension() =>
        Assert.Equal(VpnErrorCode.UnsupportedFile,
            Assert.Throws<VpnException>(() => NewStore().Import("x", "x.txt", WireGuard, null)).Code);

    [Theory]
    [InlineData("x.conf", "[Peer]\nEndpoint = vpn.example.com:51820")] // pas de [Interface] / PrivateKey
    [InlineData("x.ovpn", "client\ndev tun")]                          // pas de remote
    public void Rejects_incomplete_profiles(string fileName, string content) =>
        Assert.Equal(VpnErrorCode.InvalidConfig,
            Assert.Throws<VpnException>(() => NewStore().Import("x", fileName, content, null)).Code);

    [Fact]
    public void Unknown_profile_has_its_own_code() =>
        Assert.Equal(VpnErrorCode.ProfileNotFound,
            Assert.Throws<VpnException>(() => NewStore().SetLocation("nope", "fr-par")).Code);

    [Fact]
    public void Ids_are_unique_and_valid_tunnel_names()
    {
        var store = NewStore();
        var a = store.Import("Mon Serveur !!", "a.conf", WireGuard, null);
        var b = store.Import("Mon Serveur !!", "b.conf", WireGuard, null);

        Assert.Equal("mon-serveur", a.Id);
        Assert.Equal("mon-serveur-2", b.Id);
        Assert.Matches("^[a-zA-Z0-9_=+.-]{1,32}$", b.Id);
    }

    [Fact]
    public void Location_changes_and_deletes_are_persisted()
    {
        var store = NewStore();
        var p = store.Import("Paris", "paris.conf", WireGuard, null);
        store.SetLocation(p.Id, "fr-par");

        var reloaded = NewStore();
        Assert.Equal("fr-par", reloaded.Get(p.Id)?.LocationId);

        Assert.True(reloaded.Delete(p.Id));
        Assert.Empty(NewStore().All());
        Assert.False(File.Exists(reloaded.GetConfigPath(p)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
