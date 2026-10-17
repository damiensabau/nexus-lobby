using System.IO;
using System.Net;
using System.Net.Sockets;
using VpnClient.App;
using VpnClient.App.Services;
using VpnClient.App.ViewModels;
using VpnClient.Core;

namespace VpnClient.Tests;

public sealed class AppFeaturesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nexus-settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    // ───────── Préférences (#26) ─────────

    [Fact]
    public void Settings_have_sensible_defaults_when_missing()
    {
        var s = UserSettings.Load(SettingsPath);
        Assert.Equal(SortMode.Score, s.SortMode);
        Assert.Empty(s.CollapsedFolders);
        Assert.True(s.MinimizeToTray);
        Assert.True(s.AutoReconnect);
        Assert.True(s.Notifications);
        Assert.Null(s.LastConnectedProfileId);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var s = UserSettings.Load(SettingsPath);
        s.SortMode = SortMode.Ping;
        s.CollapsedFolders.Add("pays:FR");
        s.CollapsedFolders.Add("ville:Paris");
        s.SuggestionsExpanded = false;
        s.AutoReconnect = false;
        s.LastConnectedProfileId = "demo-tw-tpe-1";
        s.Save();

        var back = UserSettings.Load(SettingsPath);
        Assert.Equal(SortMode.Ping, back.SortMode);
        Assert.Equal(["pays:FR", "ville:Paris"], back.CollapsedFolders.Order());
        Assert.False(back.SuggestionsExpanded);
        Assert.False(back.AutoReconnect);
        Assert.Equal("demo-tw-tpe-1", back.LastConnectedProfileId);
        Assert.Contains("\"Ping\"", File.ReadAllText(SettingsPath)); // énumération lisible dans le fichier
    }

    [Fact]
    public void Corrupted_settings_fall_back_to_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ ceci n'est pas du JSON");
        var s = UserSettings.Load(SettingsPath);
        Assert.Equal(SortMode.Score, s.SortMode);
        s.Save(); // réécrit un fichier valide
        Assert.Equal(SortMode.Score, UserSettings.Load(SettingsPath).SortMode);
    }

    [Fact]
    public void Folder_keys_are_stable()
    {
        Assert.Equal("pays:FR", MainWindow.FolderKey(new CountryGroupKey("FR", "France")));
        Assert.Equal("ville:Paris", MainWindow.FolderKey("Paris"));
        Assert.Null(MainWindow.FolderKey(""));      // pas de sous-dossier ville
        Assert.Null(MainWindow.FolderKey(null));
    }

    // ───────── Ping de secours (#14) ─────────

    [Fact]
    public async Task Tcp_probe_measures_an_open_port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var ms = await NetworkProbe.TcpProbeAsync("127.0.0.1", port);
            Assert.NotNull(ms);
            Assert.InRange(ms!.Value, 0, 1500);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Tcp_probe_counts_a_refused_connection_as_an_answer()
    {
        // Port libéré juste avant : la machine répond « refusé », c'est bien un aller-retour.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var ms = await NetworkProbe.TcpProbeAsync("127.0.0.1", port);
        Assert.NotNull(ms);
        // Sans désactiver les réessais SYN, Windows met ~1 s à signaler le refus : ce ne serait pas un aller-retour.
        Assert.InRange(ms!.Value, 0, 300);
    }

    [Fact]
    public async Task Probe_reports_its_method()
    {
        var result = await NetworkProbe.ProbeAsync("127.0.0.1", null);
        Assert.NotNull(result);
        Assert.Equal("ICMP", result!.Method); // la boucle locale répond au ping
    }

    // ───────── Expiration (#13) ─────────

    private static ServerItem Server(DateTime? expires) =>
        new(new VpnProfile { Id = "x", Name = "x", LocationId = "jp-tyo", ExpiresAt = expires });

    [Fact]
    public void Expiry_labels_follow_the_days_left()
    {
        var today = DateTime.Today;
        Assert.Equal("", Server(null).ExpiryText);
        Assert.False(Server(null).HasExpiry);
        Assert.Equal("Expiré", Server(today.AddDays(-1)).ExpiryText);
        Assert.Equal("Expire aujourd'hui", Server(today).ExpiryText);
        Assert.Equal("Expire demain", Server(today.AddDays(1)).ExpiryText);
        Assert.Equal("Expire dans 5 j", Server(today.AddDays(5)).ExpiryText);
        Assert.StartsWith("Expire le ", Server(today.AddDays(30)).ExpiryText);
    }

    [Fact]
    public void Expired_profiles_get_no_score_and_say_why()
    {
        var expired = Server(DateTime.Today.AddDays(-2));
        Assert.True(expired.IsExpired);
        Assert.Null(expired.Lobby);           // donc absent des suggestions
        Assert.Equal("Expiré", expired.LobbyGrade);

        var valid = Server(DateTime.Today.AddDays(3));
        Assert.False(valid.IsExpired);
        Assert.NotNull(valid.Lobby);
    }

    // ───────── Import multiple / glisser-déposer (#11) ─────────

    [Theory]
    [InlineData("fr-par-wg-001.conf", true)]
    [InlineData("C:\\dl\\Tokyo.OVPN", true)]
    [InlineData("notes.txt", false)]
    [InlineData("profil.conf.bak", false)]
    public void Recognizes_profile_files(string path, bool expected) =>
        Assert.Equal(expected, MainViewModel.IsProfileFile(path));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
