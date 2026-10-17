using VpnClient.Core;

namespace VpnClient.Service.Tunnels;

public interface ITunnel : IAsyncDisposable
{
    VpnProfile Profile { get; }

    /// <summary>Copie de l'état courant (lue par le serveur IPC).</summary>
    TunnelStatus GetStatus();

    Task StartAsync(CancellationToken ct);

    Task StopAsync();
}
