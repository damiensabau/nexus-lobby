using Microsoft.Extensions.Options;
using VpnClient.Core;
using VpnClient.Service.Tunnels;

namespace VpnClient.Service;

/// <summary>Un seul tunnel actif à la fois : se connecter à un profil coupe le précédent.</summary>
public sealed class TunnelManager(ProfileStore store, IOptions<VpnOptions> options, ILogger<TunnelManager> logger)
    : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITunnel? _current;
    private TunnelStatus? _lastError;

    public TunnelStatus GetStatus() => _current?.GetStatus() ?? _lastError ?? new TunnelStatus();

    public async Task ConnectAsync(string profileId, string? username, string? password)
    {
        var profile = store.Get(profileId) ?? throw new VpnException(VpnErrorCode.ProfileNotFound, "Profil introuvable.");

        ITunnel tunnel;
        await _gate.WaitAsync();
        try
        {
            await StopCurrentAsync();

            var path = store.GetConfigPath(profile);
            _current = tunnel = (options.Value.DemoMode && profile.Id.StartsWith("demo-", StringComparison.Ordinal), profile.Protocol) switch
            {
                (true, _) => new DemoTunnel(profile),
                (_, VpnProtocol.WireGuard) => new WireGuardTunnel(profile, path, options.Value, logger),
                _ => new OpenVpnTunnel(profile, path, username, password, options.Value, logger),
            };
        }
        finally
        {
            _gate.Release();
        }

        // La connexion se poursuit en arrière-plan : l'interface suit l'état via GetStatus.
        _ = Task.Run(async () =>
        {
            try
            {
                await tunnel.StartAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Connexion annulée par l'utilisateur (Annuler / déconnexion pendant la connexion).
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Échec de connexion à {Profile}", profile.Id);
                await _gate.WaitAsync();
                try
                {
                    if (_current != tunnel) return;
                    await tunnel.DisposeAsync();
                    _current = null;
                    _lastError = new TunnelStatus
                    {
                        ProfileId = profile.Id,
                        State = ConnectionState.Error,
                        Message = ex.Message,
                        ErrorCode = ex is VpnException vpn ? vpn.Code : VpnErrorCode.Unknown,
                    };
                }
                finally
                {
                    _gate.Release();
                }
            }
        });
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync();
        try { await StopCurrentAsync(); }
        finally { _gate.Release(); }
    }

    public bool IsActive(string profileId) => _current?.Profile.Id == profileId;

    private async Task StopCurrentAsync()
    {
        _lastError = null;
        if (_current is null) return;
        try
        {
            await _current.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur à l'arrêt du tunnel {Profile}", _current.Profile.Id);
        }
        _current = null;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
