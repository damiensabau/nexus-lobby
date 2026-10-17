using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnClient.App.ViewModels;

namespace VpnClient.App.Services;

/// <summary>
/// Préférences de l'utilisateur (pas du service) : %AppData%\NexusLobby\settings.json.
/// Lecture tolérante : un fichier absent ou abîmé redonne les réglages par défaut.
/// </summary>
public sealed class UserSettings
{
    public SortMode SortMode { get; set; } = SortMode.Score;

    /// <summary>Dossiers (pays « pays:XX » ou ville « ville:Nom ») refermés par l'utilisateur.</summary>
    public HashSet<string> CollapsedFolders { get; set; } = [];

    public bool SuggestionsExpanded { get; set; } = true;

    /// <summary>Fermer la fenêtre la garde dans la zone de notification au lieu de quitter.</summary>
    public bool MinimizeToTray { get; set; } = true;

    public bool Notifications { get; set; } = true;

    /// <summary>Se reconnecter si le tunnel se coupe, et au lancement si on était connecté en quittant.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>Région active au moment de quitter (effacée par « Revenir à ma région »).</summary>
    public string? LastConnectedProfileId { get; set; }

    public bool RememberCredentials { get; set; } = true;

    /// <summary>Le message « NEXUS LOBBY reste actif ici » n'est montré qu'une fois.</summary>
    public bool TrayHintShown { get; set; }

    // ───────── Lecture / écriture ─────────

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [JsonIgnore]
    public string? FilePath { get; private set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusLobby", "settings.json");

    public static UserSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        UserSettings settings;
        try
        {
            settings = File.Exists(path) ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            settings = new();
        }
        settings.CollapsedFolders ??= [];
        settings.FilePath = path;
        return settings;
    }

    public void Save()
    {
        if (FilePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Écriture atomique : un plantage en plein milieu ne laisse pas un fichier tronqué.
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Préférences non enregistrées : pas grave, l'app continue avec celles en mémoire.
        }
    }
}
