using VpnClient.Core;

namespace VpnClient.App.Services;

public enum ErrorAction
{
    None,
    Retry,          // relancer la dernière connexion
    PickAnother,    // se connecter à la meilleure autre région
    OpenUrl,        // ouvrir une page (téléchargement…)
    Import,         // ajouter une région
    Credentials,    // remplir identifiant / mot de passe
}

/// <summary>Message d'erreur prêt à afficher : titre court, explication, et action proposée.</summary>
public sealed record FriendlyError(string Title, string Message, ErrorAction Action = ErrorAction.None, string? ActionLabel = null, string? Url = null);

/// <summary>Traduit les codes d'erreur du service en messages compréhensibles (tutoiement, comme le reste de l'app).</summary>
public static class ErrorCatalog
{
    public const string WireGuardUrl = "https://www.wireguard.com/install/";
    public const string OpenVpnUrl = "https://openvpn.net/community-downloads/";

    /// <param name="code">Cause renvoyée par le service.</param>
    /// <param name="server">Nom de la région concernée, s'il y en a une.</param>
    /// <param name="detail">Message technique (affiché en petit pour les erreurs inconnues).</param>
    public static FriendlyError Describe(VpnErrorCode code, string? server = null, string? detail = null)
    {
        var at = server is null ? "" : $" à {server}";
        return code switch
        {
            VpnErrorCode.ServiceUnavailable => new(
                "Le moteur VPN ne répond pas",
                "Le service « VpnClientService » est arrêté. Relance-le (ou réinstalle l'app), puis réessaie.",
                ErrorAction.Retry, "Réessayer"),

            VpnErrorCode.WireGuardMissing => new(
                "WireGuard n'est pas installé",
                "Cette région utilise WireGuard. Installe-le (gratuit, 2 minutes), puis relance la connexion.",
                ErrorAction.OpenUrl, "Télécharger WireGuard", WireGuardUrl),

            VpnErrorCode.OpenVpnMissing => new(
                "OpenVPN n'est pas installé",
                "Cette région utilise OpenVPN. Installe OpenVPN Community, puis relance la connexion.",
                ErrorAction.OpenUrl, "Télécharger OpenVPN", OpenVpnUrl),

            VpnErrorCode.CredentialsRequired => new(
                "Identifiants requis",
                $"Le serveur{at} demande un identifiant et un mot de passe : remplis les champs sous le bouton.",
                ErrorAction.Credentials, "Compris"),

            VpnErrorCode.AuthFailed => new(
                "Identifiants refusés",
                $"Le serveur{at} a refusé ton identifiant ou ton mot de passe. Vérifie-les (ou ton abonnement) et réessaie.",
                ErrorAction.Credentials, "Corriger"),

            VpnErrorCode.PrivateKeyProtected => new(
                "Profil protégé par mot de passe",
                "La clé de ce profil OpenVPN est chiffrée, ce n'est pas encore géré. Exporte un profil sans mot de passe sur la clé.",
                ErrorAction.Import, "Importer un autre fichier"),

            VpnErrorCode.ServerUnreachable => new(
                $"Impossible de joindre{(server is null ? " le serveur" : " " + server)}",
                "Le serveur ne répond pas : il est peut-être hors ligne, saturé, ou le compte a expiré. Essaie une autre région.",
                ErrorAction.PickAnother, "Essayer une autre région"),

            VpnErrorCode.TunnelLost => new(
                "Connexion perdue",
                $"Le tunnel{at} s'est coupé. Tu es revenu sur ta connexion normale. Relance-le ou passe sur une autre région.",
                ErrorAction.Retry, "Se reconnecter"),

            VpnErrorCode.Timeout => new(
                "Ça prend trop de temps",
                "La connexion n'a pas abouti à temps. Réessaie dans un instant.",
                ErrorAction.Retry, "Réessayer"),

            VpnErrorCode.ProfileNotFound => new(
                "Région introuvable",
                "Ce serveur a été supprimé entre-temps. La liste vient d'être mise à jour.",
                ErrorAction.None),

            VpnErrorCode.NoServers => new(
                "Aucune région disponible",
                "Ajoute d'abord un serveur : un fichier WireGuard (.conf) ou OpenVPN (.ovpn) par pays.",
                ErrorAction.Import, "Ajouter une région"),

            VpnErrorCode.UnsupportedFile => new(
                "Fichier non reconnu",
                "Seuls les fichiers WireGuard (.conf) et OpenVPN (.ovpn) sont acceptés.",
                ErrorAction.Import, "Choisir un autre fichier"),

            VpnErrorCode.FileTooLarge => new(
                "Fichier trop gros",
                "Un profil VPN fait quelques Ko. Ce fichier n'en est sûrement pas un.",
                ErrorAction.Import, "Choisir un autre fichier"),

            VpnErrorCode.InvalidConfig => new(
                "Profil invalide",
                "Le fichier est incomplet ou abîmé (clé ou adresse du serveur manquante). Re-télécharge-le chez ton fournisseur.",
                ErrorAction.Import, "Choisir un autre fichier"),

            VpnErrorCode.ForbiddenDirective => new(
                "Profil refusé pour ta sécurité",
                "Ce fichier contient des commandes qui s'exécuteraient sur ton PC (scripts). Utilise un profil standard.",
                ErrorAction.Import, "Choisir un autre fichier"),

            _ => new(
                "Oups, quelque chose a coincé",
                string.IsNullOrWhiteSpace(detail) ? "Une erreur inattendue s'est produite. Réessaie." : $"Détail : {detail}",
                ErrorAction.Retry, "Réessayer"),
        };
    }
}
