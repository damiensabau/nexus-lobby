namespace VpnClient.Core;

public enum VpnProtocol
{
    WireGuard,
    OpenVpn,
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Error,
}

public sealed class VpnProfile
{
    /// <summary>Identifiant court, aussi utilisé comme nom de tunnel WireGuard (a-z, 0-9, -, max 32).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public VpnProtocol Protocol { get; set; }

    /// <summary>Vrai si le .ovpn contient "auth-user-pass" sans fichier : il faut un identifiant / mot de passe.</summary>
    public bool RequiresCredentials { get; set; }

    /// <summary>Emplacement choisi par l'utilisateur (voir le catalogue Locations côté interface).</summary>
    public string? LocationId { get; set; }

    /// <summary>Hôte du serveur (Endpoint WireGuard / remote OpenVPN), utilisé pour mesurer le ping.</summary>
    public string? ServerHost { get; set; }

    /// <summary>Port du serveur (Endpoint WireGuard / remote OpenVPN), pour le ping de secours en TCP.</summary>
    public int? ServerPort { get; set; }

    /// <summary>Date d'expiration du compte (profils gratuits), saisie par l'utilisateur. Null = pas d'expiration connue.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Identifiants OpenVPN mémorisés (chiffrés) côté service. Recalculé à chaque liste, jamais le secret lui-même.</summary>
    public bool HasSavedCredentials { get; set; }

    public DateTime ImportedAt { get; set; }
}

public sealed class TunnelStatus
{
    public string? ProfileId { get; set; }
    public ConnectionState State { get; set; } = ConnectionState.Disconnected;
    public string? Message { get; set; }
    public VpnErrorCode ErrorCode { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public DateTime? ConnectedSince { get; set; }
    public string? LocalAddress { get; set; }
}
