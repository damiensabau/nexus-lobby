using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VpnClient.App.Services;
using VpnClient.Core;

namespace VpnClient.App.ViewModels;

/// <summary>Un serveur de la liste = un profil importé (.conf / .ovpn) + son emplacement.</summary>
public sealed partial class ServerItem : ObservableObject
{
    private static readonly Brush Good = Frozen(0x22, 0xC5, 0x5E);
    private static readonly Brush Fair = Frozen(0x84, 0xCC, 0x16);
    private static readonly Brush Medium = Frozen(0xF5, 0x9E, 0x0B);
    private static readonly Brush Bad = Frozen(0xEF, 0x44, 0x44);
    private static readonly Brush Unknown = Frozen(0x64, 0x74, 0x8B);

    public ServerItem(VpnProfile profile)
    {
        Profile = profile;
        Location = Locations.Find(profile.LocationId);
        RefreshClock();
    }

    public VpnProfile Profile { get; }
    public Location? Location { get; }

    public string Title => Location?.City ?? Profile.Name;

    // ── Rangement de la liste : dossier pays, puis sous-dossier ville si le pays a plusieurs serveurs ──

    public CountryGroupKey CountryGroup => Location is null
        ? CountryGroupKey.Unknown
        : new CountryGroupKey(Location.CountryCode, Location.Country);

    public string CountryName => CountryGroup.Name;

    /// <summary>Ville si le pays contient plusieurs serveurs, sinon vide (pas de sous-dossier).</summary>
    [ObservableProperty]
    private string _cityGroup = "";

    /// <summary>Vrai si plusieurs serveurs partagent la même ville : on affiche alors le nom du fichier.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListTitle), nameof(ListSubtitle))]
    private bool _sharesCity;

    public string ListTitle => SharesCity ? Profile.Name : Title;

    // Clés de tri calculées par ServerDisplayComparer.ApplyKeys (plus grand = plus haut).
    public int CountrySortKey { get; set; }
    public int CitySortKey { get; set; }
    public int ItemSortKey { get; set; }

    /// <summary>Le pays est donné par le dossier : on n'affiche que la ville (si besoin) et le protocole.</summary>
    public string ListSubtitle => Location is null
        ? $"Région inconnue · {ProtocolLabel}"
        : SharesCity ? $"{Location.City} · {ProtocolLabel}" : ProtocolLabel;
    public string Subtitle => $"{Location?.Country ?? "Région inconnue"} · {ProtocolLabel}";
    public string CountryCode => Location?.CountryCode ?? "??";
    public string ProtocolLabel => Profile.Protocol == VpnProtocol.WireGuard ? "WireGuard" : "OpenVPN";

    public string SearchText => $"{Profile.Name} {Location?.City} {Location?.Country} {Location?.CountryCode} {Location?.Region}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingText), nameof(PingBrush))]
    private long? _pingMs;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isConnected;

    // ── Heure locale de la région : la nuit, moins de joueurs connectés = lobbies souvent plus faciles. ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocalTimeText), nameof(LobbyLabel), nameof(LobbyBrush), nameof(IsOffPeak))]
    private DateTime? _localTime;

    public void RefreshClock()
    {
        LocalTime = Location?.LocalTime;
        Lobby = Location is null ? null : LobbyAdvisor.Score(Location, PingMs, DateTime.UtcNow);
    }

    partial void OnPingMsChanged(long? value) => RefreshClock();

    // ── Note « lobby facile » (voir LobbyAdvisor) ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LobbyValue), nameof(LobbyScoreText), nameof(LobbyGrade), nameof(LobbyReason), nameof(LobbyScoreBrush))]
    private LobbyScore? _lobby;

    public int LobbyValue => Lobby?.Value ?? -1;
    public string LobbyScoreText => Lobby is { } l ? l.Value.ToString() : "?";
    public string LobbyGrade => Lobby?.Grade ?? "Région inconnue";
    public string LobbyReason => Lobby?.Reason ?? "Choisis la région de ce serveur (clic droit)";

    public Brush LobbyScoreBrush => Lobby?.Value switch
    {
        null => Unknown,
        >= 75 => Good,
        >= 58 => Fair,
        >= 40 => Medium,
        _ => Bad,
    };

    public bool IsOffPeak => LocalTime is { Hour: >= 1 and < 10 };

    public string LocalTimeText => LocalTime is { } t ? t.ToString("HH:mm") : "--:--";

    public string LobbyLabel => LocalTime switch
    {
        null => "Heure inconnue",
        { Hour: >= 1 and < 10 } => "Heure creuse",
        { Hour: >= 17 or 0 } => "Heure de pointe",
        _ => "Journée",
    };

    public Brush LobbyBrush => LocalTime switch
    {
        null => Unknown,
        { Hour: >= 1 and < 10 } => Good,
        { Hour: >= 17 or 0 } => Bad,
        _ => Medium,
    };

    public string PingText => PingMs is { } ms ? $"{ms} ms" : "— ms";

    public Brush PingBrush => PingMs switch
    {
        null => Unknown,
        < 60 => Good,
        < 120 => Medium,
        _ => Bad,
    };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed record CountryGroupKey(string Code, string Name)
{
    public static readonly CountryGroupKey Unknown = new("??", "Région inconnue");
}

/// <summary>Pays surligné sur la carte (au moins un serveur importé dans ce pays).</summary>
public sealed partial class CountryHighlight(string code, Geometry geometry) : ObservableObject
{
    public string Code { get; } = code;
    public Geometry Geometry { get; } = geometry;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Repère sur la carte : un par emplacement utilisé par au moins un serveur.</summary>
public sealed partial class MapMarker(Location location, int count) : ObservableObject
{
    // Le repère fait 140 x 46 ; le point est centré en bas (70, 39).
    public const double Width = 140;

    public Location Location { get; } = location;
    public double Left => Location.MapX - Width / 2;
    public double Top => Location.MapY - 39;
    public string Label => count > 1 ? $"{Location.City} ×{count}" : Location.City;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isSelected;
}
