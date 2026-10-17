using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace VpnClient.Service;

/// <summary>
/// Identifiants OpenVPN mémorisés, un fichier par profil dans %ProgramData%\VpnClient\credentials
/// (dossier SYSTEM + Administrateurs uniquement), chiffré avec DPAPI portée machine.
/// Les identifiants ne quittent jamais le service : l'interface sait seulement s'il y en a.
/// </summary>
public sealed class CredentialStore
{
    // Entropie propre à l'app : un autre programme utilisant DPAPI machine ne peut pas relire ces fichiers par hasard.
    private static readonly byte[] Entropy = "NEXUS LOBBY · identifiants OpenVPN · v1"u8.ToArray();

    private readonly string _dir;

    // ProfileStore en dépendance : il crée et protège DataDirectory AVANT nous, le sous-dossier hérite de ses droits.
    public CredentialStore(IOptions<VpnOptions> options, ProfileStore profilesFirst)
    {
        _ = profilesFirst;
        _dir = Path.Combine(options.Value.DataDirectory, "credentials");
        Directory.CreateDirectory(_dir);
    }

    public bool Has(string profileId) => File.Exists(PathFor(profileId));

    public void Save(string profileId, string username, string password)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new Saved(username, password));
        try
        {
            File.WriteAllBytes(PathFor(profileId), ProtectedData.Protect(json, Entropy, DataProtectionScope.LocalMachine));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    public (string Username, string Password)? Load(string profileId)
    {
        var path = PathFor(profileId);
        if (!File.Exists(path)) return null;
        try
        {
            var json = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.LocalMachine);
            try
            {
                var saved = JsonSerializer.Deserialize<Saved>(json);
                return saved is null ? null : (saved.U, saved.P);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }
        catch (CryptographicException)
        {
            // Fichier copié depuis un autre PC ou abîmé : on l'ignore et on le supprime.
            Forget(profileId);
            return null;
        }
    }

    public void Forget(string profileId)
    {
        var path = PathFor(profileId);
        if (File.Exists(path)) File.Delete(path);
    }

    // L'identifiant de profil est déjà restreint à [a-z0-9_=+.-] (ProfileStore.MakeUniqueId) ; on revérifie quand même.
    private string PathFor(string profileId)
    {
        if (profileId.Length == 0 || profileId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '=' or '+' or '.' or '-'))
            || profileId.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Identifiant de profil invalide.", nameof(profileId));
        return Path.Combine(_dir, profileId + ".bin");
    }

    private sealed record Saved(string U, string P);
}
