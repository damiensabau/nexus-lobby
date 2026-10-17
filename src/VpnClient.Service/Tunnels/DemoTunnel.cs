using VpnClient.Core;

namespace VpnClient.Service.Tunnels;

/// <summary>
/// Tunnel simulé pour tester l'interface sans WireGuard ni OpenVPN (profils « demo-… », option Vpn:DemoMode).
/// N'ouvre aucune connexion : il fait défiler les étapes puis invente un trafic.
/// Pannes simulées selon l'identifiant : « -fail-unreachable », « -fail-auth », « -fail-lost » (coupure après 8 s).
/// </summary>
public sealed class DemoTunnel(VpnProfile profile) : TunnelBase(profile)
{
    private CancellationTokenSource? _cts;
    private Task? _traffic;

    public override async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        foreach (var step in new[] { "Contact du serveur…", "Échange des clés…", "Création de la route…", "Vérification de l'IP…" })
        {
            Update(s => { s.State = ConnectionState.Connecting; s.Message = step; });
            await Task.Delay(800, _cts.Token);
        }

        if (Profile.Id.Contains("-fail-unreachable", StringComparison.Ordinal))
        {
            Fail(VpnErrorCode.ServerUnreachable, "Démo : le serveur ne répond pas.");
            return;
        }
        if (Profile.Id.Contains("-fail-auth", StringComparison.Ordinal))
        {
            Fail(VpnErrorCode.AuthFailed, "Démo : identifiants refusés.");
            return;
        }

        Update(s =>
        {
            s.State = ConnectionState.Connected;
            s.Message = "Tunnel actif (démo)";
            s.LocalAddress = "10.66.0.2";
        });
        _traffic = SimulateTrafficAsync(_cts.Token);

        if (Profile.Id.Contains("-fail-lost", StringComparison.Ordinal))
        {
            _ = Task.Delay(TimeSpan.FromSeconds(8), _cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                Fail(VpnErrorCode.TunnelLost, "Démo : connexion coupée par le serveur.");
                Update(s => { s.BytesReceived = s.BytesSent = 0; });
                _cts?.Cancel();
            }, TaskScheduler.Default);
        }
    }

    public override async Task StopAsync()
    {
        if (State == ConnectionState.Disconnected) return;
        var failed = State == ConnectionState.Error;
        if (!failed) Update(s => { s.State = ConnectionState.Disconnecting; s.Message = "Fermeture du tunnel…"; });

        if (_cts is not null)
        {
            await _cts.CancelAsync();
            try { if (_traffic is not null) await _traffic; } catch (OperationCanceledException) { }
            _cts.Dispose();
            _cts = null;
        }

        if (failed) return;
        await Task.Delay(600);
        Update(s => { s.State = ConnectionState.Disconnected; s.Message = null; s.BytesReceived = s.BytesSent = 0; });
    }

    private async Task SimulateTrafficAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(ct))
        {
            // Trafic de jeu typique : quelques dizaines de Ko/s, avec des pics.
            var down = Random.Shared.Next(15_000, Random.Shared.Next(10) == 0 ? 400_000 : 60_000);
            var up = Random.Shared.Next(5_000, 25_000);
            Update(s => { s.BytesReceived += down; s.BytesSent += up; });
        }
    }
}
