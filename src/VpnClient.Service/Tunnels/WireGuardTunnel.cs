using System.ServiceProcess;
using System.Text.RegularExpressions;
using VpnClient.Core;

namespace VpnClient.Service.Tunnels;

/// <summary>
/// Pilote le service de tunnel officiel de WireGuard for Windows
/// (wireguard.exe /installtunnelservice), qui crée le service "WireGuardTunnel$&lt;nom&gt;".
/// </summary>
public sealed partial class WireGuardTunnel(VpnProfile profile, string configPath, VpnOptions options, ILogger logger)
    : TunnelBase(profile)
{
    // Sans premier échange avec le serveur dans ce délai, on coupe : sinon tout le trafic part dans un tunnel mort.
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(20);

    private readonly string _serviceName = "WireGuardTunnel$" + profile.Id;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitor;

    public override async Task StartAsync(CancellationToken ct)
    {
        if (!File.Exists(options.WireGuardExe))
            throw new VpnException(VpnErrorCode.WireGuardMissing, "wireguard.exe introuvable : " + options.WireGuardExe);

        Update(s => { s.State = ConnectionState.Connecting; s.Message = "Démarrage du tunnel…"; });

        // Un service resté d'une session précédente bloquerait l'installation.
        if (ServiceExists()) await UninstallAsync();

        var (code, output) = await ProcessRunner.RunAsync(options.WireGuardExe, ["/installtunnelservice", configPath], ct);
        if (code != 0) throw new VpnException(VpnErrorCode.Unknown, $"wireguard.exe a échoué ({code}) : {output}");

        try
        {
            using var sc = new ServiceController(_serviceName);
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            await UninstallAsync();
            throw new VpnException(VpnErrorCode.Timeout, "Le service de tunnel WireGuard n'a pas démarré à temps.");
        }

        var address = AddressLine().Match(await File.ReadAllTextAsync(configPath, ct));
        Update(s =>
        {
            s.State = ConnectionState.Connecting;
            s.Message = "Attente de réponse du serveur…";
            s.LocalAddress = address.Success ? address.Groups[1].Value.Trim() : null;
        });

        _monitorCts = new CancellationTokenSource();
        _monitor = MonitorAsync(_monitorCts.Token);
    }

    public override async Task StopAsync()
    {
        if (State == ConnectionState.Disconnected) return;
        var failed = State == ConnectionState.Error;
        if (!failed) Update(s => { s.State = ConnectionState.Disconnecting; s.Message = "Arrêt du tunnel…"; });

        if (_monitorCts is not null)
        {
            await _monitorCts.CancelAsync();
            try { await _monitor!; } catch (OperationCanceledException) { }
            _monitorCts.Dispose();
            _monitorCts = null;
        }

        if (ServiceExists()) await UninstallAsync();
        if (!failed) Update(s => { s.State = ConnectionState.Disconnected; s.Message = null; s.BytesReceived = s.BytesSent = 0; });
    }

    private async Task MonitorAsync(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (!ServiceRunning())
            {
                Fail(State == ConnectionState.Connected ? VpnErrorCode.TunnelLost : VpnErrorCode.ServerUnreachable,
                    "Le service de tunnel WireGuard s'est arrêté.");
                return;
            }

            if (!File.Exists(options.WgExe))
            {
                // Sans wg.exe, impossible de vérifier le handshake : on considère le tunnel actif.
                if (State == ConnectionState.Connecting) Update(s => { s.State = ConnectionState.Connected; s.Message = "Tunnel actif"; });
                continue;
            }

            try
            {
                // « wg show <tunnel> dump » : ligne 1 = interface, puis une ligne par pair :
                // clé publique, clé partagée, endpoint, IPs autorisées, dernier handshake (s), reçus, envoyés, keepalive.
                var (code, output) = await ProcessRunner.RunAsync(options.WgExe, ["show", Profile.Id, "dump"], ct);
                if (code != 0) continue;

                long rx = 0, tx = 0, lastHandshake = 0;
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 7) continue;
                    lastHandshake = Math.Max(lastHandshake, long.TryParse(parts[4], out var h) ? h : 0);
                    rx += long.TryParse(parts[5], out var r) ? r : 0;
                    tx += long.TryParse(parts[6], out var t) ? t : 0;
                }

                if (lastHandshake > 0 && State == ConnectionState.Connecting)
                {
                    Update(s => { s.State = ConnectionState.Connected; s.Message = "Tunnel actif"; });
                }
                else if (lastHandshake == 0 && DateTime.UtcNow - started > HandshakeTimeout)
                {
                    logger.LogWarning("Aucun handshake WireGuard avec {Tunnel} après {Delay}", Profile.Id, HandshakeTimeout);
                    await UninstallAsync();
                    Fail(VpnErrorCode.ServerUnreachable, "Aucune réponse du serveur WireGuard (handshake).");
                    return;
                }

                Update(s => { s.BytesReceived = rx; s.BytesSent = tx; });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "wg show a échoué");
            }
        }
    }

    private async Task UninstallAsync()
    {
        var (code, output) = await ProcessRunner.RunAsync(options.WireGuardExe, ["/uninstalltunnelservice", Profile.Id]);
        if (code != 0) logger.LogWarning("Désinstallation du tunnel {Tunnel} : {Output}", Profile.Id, output);
    }

    private bool ServiceExists() =>
        ServiceController.GetServices().Any(s => s.ServiceName.Equals(_serviceName, StringComparison.OrdinalIgnoreCase));

    private bool ServiceRunning()
    {
        try
        {
            using var sc = new ServiceController(_serviceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^\s*Address\s*=\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex AddressLine();
}
