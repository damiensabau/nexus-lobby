using Microsoft.Win32;

namespace VpnClient.App.Services;

/// <summary>
/// Lancement au démarrage de Windows : valeur dans HKCU\…\Run (compte de l'utilisateur, pas besoin d'être admin).
/// L'app démarre alors réduite dans la zone de notification (argument --minimized).
/// </summary>
public static class StartupRegistration
{
    public const string MinimizedArgument = "--minimized";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NEXUS LOBBY";

    public static string Command => $"\"{Environment.ProcessPath}\" {MinimizedArgument}";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Si l'app a été déplacée (mise à jour, autre dossier), on remet le bon chemin.</summary>
    public static void RefreshPathIfEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(ValueName) is string value && value.Length > 0 && value != Command) Set(true);
    }
}
