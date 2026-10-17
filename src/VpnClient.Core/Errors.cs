namespace VpnClient.Core;

/// <summary>Cause d'une erreur, transmise du service à l'interface qui l'affiche en clair (voir ErrorCatalog).</summary>
public enum VpnErrorCode
{
    None,
    Unknown,

    // Service / environnement
    ServiceUnavailable,
    WireGuardMissing,
    OpenVpnMissing,

    // Connexion
    CredentialsRequired,
    AuthFailed,
    PrivateKeyProtected,
    ServerUnreachable,
    TunnelLost,
    Timeout,

    // Profils
    ProfileNotFound,
    NoServers,
    UnsupportedFile,
    FileTooLarge,
    InvalidConfig,
    ForbiddenDirective,
}

/// <summary>Erreur métier levée par le service : le code part dans la réponse IPC, le message sert aux logs.</summary>
public sealed class VpnException(VpnErrorCode code, string message) : InvalidOperationException(message)
{
    public VpnErrorCode Code { get; } = code;
}
