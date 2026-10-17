using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VpnClient.Core;

namespace VpnClient.Service;

public sealed partial class ProfileStore
{
    private readonly string _dir;
    private readonly string _indexPath;
    private readonly Lock _lock = new();
    private List<VpnProfile> _profiles;

    // Directives qui exécuteraient du code en tant que SYSTEM : refusées à l'import.
    private static readonly string[] ForbiddenOpenVpnDirectives =
    [
        "script-security", "up", "down", "route-up", "route-pre-down", "ipchange", "tls-verify",
        "auth-user-pass-verify", "client-connect", "client-disconnect", "learn-address", "plugin",
        "management", "config", "cd", "chroot", "log", "log-append", "writepid", "status", "daemon",
        // Chargent une DLL ou écrivent / détournent des fichiers avec les droits SYSTEM.
        "engine", "providers", "pkcs11-providers", "replay-persist", "tmp-dir", "win-sys", "capath",
        "ifconfig-pool-persist",
    ];

    // Lisent un fichier du disque (en tant que SYSTEM) : seule la forme intégrée au profil est acceptée,
    // c'est-à-dire sans argument ou avec « [inline] », le contenu étant dans un bloc <ca>…</ca>.
    private static readonly string[] FileReferenceDirectives =
    [
        "ca", "cert", "key", "pkcs12", "dh", "tls-auth", "tls-crypt", "tls-crypt-v2", "secret",
        "crl-verify", "extra-certs", "askpass", "auth-user-pass", "http-proxy-user-pass",
    ];

    private static readonly string[] ForbiddenWireGuardKeys = ["PreUp", "PostUp", "PreDown", "PostDown"];

    public ProfileStore(IOptions<VpnOptions> options)
    {
        _dir = Path.Combine(options.Value.DataDirectory, "profiles");
        _indexPath = Path.Combine(_dir, "profiles.json");
        CreateSecureDirectory(options.Value.DataDirectory);
        Directory.CreateDirectory(_dir);
        _profiles = Load();
    }

    public IReadOnlyList<VpnProfile> All()
    {
        lock (_lock) return _profiles.ToList();
    }

    public VpnProfile? Get(string id)
    {
        lock (_lock) return _profiles.FirstOrDefault(p => p.Id == id);
    }

    public string GetConfigPath(VpnProfile profile) =>
        Path.Combine(_dir, profile.Id + (profile.Protocol == VpnProtocol.WireGuard ? ".conf" : ".ovpn"));

    public VpnProfile Import(string name, string fileName, string content, string? locationId)
    {
        var protocol = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".conf" => VpnProtocol.WireGuard,
            ".ovpn" => VpnProtocol.OpenVpn,
            _ => throw new VpnException(VpnErrorCode.UnsupportedFile, "Format non supporté : utilisez un fichier .conf (WireGuard) ou .ovpn (OpenVPN)."),
        };

        if (protocol == VpnProtocol.WireGuard) ValidateWireGuard(content);
        else ValidateOpenVpn(content);

        lock (_lock)
        {
            var profile = new VpnProfile
            {
                Id = MakeUniqueId(name),
                Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fileName) : name.Trim(),
                Protocol = protocol,
                RequiresCredentials = protocol == VpnProtocol.OpenVpn && AuthUserPassPrompt().IsMatch(content),
                LocationId = locationId,
                ServerHost = ParseServerHost(protocol, content),
                ImportedAt = DateTime.UtcNow,
            };

            File.WriteAllText(GetConfigPath(profile), content);
            _profiles.Add(profile);
            Save();
            return profile;
        }
    }

    public VpnProfile SetLocation(string id, string? locationId)
    {
        lock (_lock)
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == id) ?? throw new VpnException(VpnErrorCode.ProfileNotFound, "Profil introuvable.");
            profile.LocationId = locationId;
            Save();
            return profile;
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == id);
            if (profile is null) return false;

            File.Delete(GetConfigPath(profile));
            _profiles.Remove(profile);
            Save();
            return true;
        }
    }

    private static string? ParseServerHost(VpnProtocol protocol, string content)
    {
        var match = protocol == VpnProtocol.WireGuard ? WireGuardEndpoint().Match(content) : OpenVpnRemote().Match(content);
        return match.Success ? match.Groups["host"].Value.Trim('[', ']') : null;
    }

    private static void ValidateWireGuard(string content)
    {
        if (!content.Contains("[Interface]", StringComparison.OrdinalIgnoreCase) ||
            !content.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase))
            throw new VpnException(VpnErrorCode.InvalidConfig, "Configuration WireGuard invalide (section [Interface] ou PrivateKey manquante).");

        foreach (var key in ForbiddenWireGuardKeys)
        {
            if (Regex.IsMatch(content, $@"^\s*{key}\s*=", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                throw new VpnException(VpnErrorCode.ForbiddenDirective, $"La directive « {key} » n'est pas autorisée (exécution de commandes).");
        }
    }

    private static void ValidateOpenVpn(string content)
    {
        if (!Regex.IsMatch(content, @"^\s*remote\s+", RegexOptions.Multiline))
            throw new VpnException(VpnErrorCode.InvalidConfig, "Configuration OpenVPN invalide (aucune directive « remote »).");

        string? inlineBlock = null;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();

            // Contenu d'un bloc intégré (<ca> … </ca>) : certificats, clés… jamais des directives.
            if (inlineBlock is not null)
            {
                if (line.Equals($"</{inlineBlock}>", StringComparison.OrdinalIgnoreCase)) inlineBlock = null;
                continue;
            }
            if (line.StartsWith('<') && line.EndsWith('>') && !line.StartsWith("</", StringComparison.Ordinal))
            {
                inlineBlock = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }

            var parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var directive = parts[0].TrimStart('-').ToLowerInvariant();

            if (ForbiddenOpenVpnDirectives.Contains(directive))
                throw new VpnException(VpnErrorCode.ForbiddenDirective, $"La directive « {directive} » n'est pas autorisée dans un profil importé.");

            if (FileReferenceDirectives.Contains(directive) && parts.Length > 1 && !IsInlineMarker(parts[1]))
                throw new VpnException(VpnErrorCode.ForbiddenDirective,
                    $"La directive « {directive} » pointe vers le fichier « {parts[1].Trim('"')} » : intègre son contenu dans le profil (<{directive}>…</{directive}>).");

            // http-proxy hôte port [fichier-identifiants|auto|auto-nct|stdin] : pas de fichier externe.
            if (directive == "http-proxy" && parts.Length > 3 && !parts[3].StartsWith("auto", StringComparison.OrdinalIgnoreCase)
                && !parts[3].Equals("stdin", StringComparison.OrdinalIgnoreCase))
                throw new VpnException(VpnErrorCode.ForbiddenDirective, "Le fichier d'identifiants du proxy doit être intégré au profil.");
        }
    }

    private static bool IsInlineMarker(string arg) =>
        arg.Equals("[inline]", StringComparison.OrdinalIgnoreCase) || arg.Equals("[[inline]]", StringComparison.OrdinalIgnoreCase);

    private string MakeUniqueId(string name)
    {
        // Nom de tunnel WireGuard valide : [a-zA-Z0-9_=+.-]{1,32}
        var slug = InvalidIdChars().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length == 0) slug = "vpn";
        if (slug.Length > 28) slug = slug[..28];

        var id = slug;
        for (var i = 2; _profiles.Any(p => p.Id == id); i++) id = $"{slug}-{i}";
        return id;
    }

    private List<VpnProfile> Load()
    {
        if (!File.Exists(_indexPath)) return [];
        return JsonSerializer.Deserialize<List<VpnProfile>>(File.ReadAllText(_indexPath), Ipc.Json) ?? [];
    }

    private void Save() =>
        File.WriteAllText(_indexPath, JsonSerializer.Serialize(_profiles, Ipc.Json));

    private static void CreateSecureDirectory(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        var dir = new DirectoryInfo(path);
        if (!dir.Exists) dir.Create(security);
        else dir.SetAccessControl(security);
    }

    [GeneratedRegex(@"^\s*auth-user-pass\s*$", RegexOptions.Multiline)]
    private static partial Regex AuthUserPassPrompt();

    // "Endpoint = host:port" ou "Endpoint = [ipv6]:port"
    [GeneratedRegex(@"^\s*Endpoint\s*=\s*(?<host>\[[^\]]+\]|[^:\s]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex WireGuardEndpoint();

    [GeneratedRegex(@"^\s*remote\s+(?<host>\S+)", RegexOptions.Multiline)]
    private static partial Regex OpenVpnRemote();

    [GeneratedRegex(@"[^a-z0-9_=+.-]+")]
    private static partial Regex InvalidIdChars();
}
