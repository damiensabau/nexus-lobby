namespace VpnClient.Service;

public sealed class VpnOptions
{
    public string WireGuardExe { get; set; } = @"C:\Program Files\WireGuard\wireguard.exe";
    public string WgExe { get; set; } = @"C:\Program Files\WireGuard\wg.exe";
    public string OpenVpnExe { get; set; } = @"C:\Program Files\OpenVPN\bin\openvpn.exe";

    /// <summary>
    /// Simule la connexion des profils dont l'identifiant commence par « demo- » (tests de l'interface).
    /// Activé seulement en développement (appsettings.Development.json).
    /// </summary>
    public bool DemoMode { get; set; }

    /// <summary>Dossier des profils, protégé (SYSTEM + Administrateurs uniquement).</summary>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VpnClient");
}
