using VpnClient.Core;

namespace VpnClient.Service.Tunnels;

public abstract class TunnelBase(VpnProfile profile) : ITunnel
{
    private readonly Lock _lock = new();
    private readonly TunnelStatus _status = new()
    {
        ProfileId = profile.Id,
        State = ConnectionState.Connecting,
        Message = "Préparation du tunnel…",
    };

    public VpnProfile Profile { get; } = profile;

    public TunnelStatus GetStatus()
    {
        lock (_lock)
        {
            return new TunnelStatus
            {
                ProfileId = _status.ProfileId,
                State = _status.State,
                Message = _status.Message,
                ErrorCode = _status.ErrorCode,
                BytesReceived = _status.BytesReceived,
                BytesSent = _status.BytesSent,
                ConnectedSince = _status.ConnectedSince,
                LocalAddress = _status.LocalAddress,
            };
        }
    }

    protected void Update(Action<TunnelStatus> change)
    {
        lock (_lock)
        {
            var wasConnected = _status.State == ConnectionState.Connected;
            change(_status);
            if (_status.State != ConnectionState.Error) _status.ErrorCode = VpnErrorCode.None;
            if (_status.State == ConnectionState.Connected && !wasConnected) _status.ConnectedSince = DateTime.UtcNow;
            if (_status.State != ConnectionState.Connected) _status.ConnectedSince = null;
        }
    }

    /// <summary>Passe le tunnel en erreur avec un code que l'interface traduit en message clair.</summary>
    protected void Fail(VpnErrorCode code, string message) =>
        Update(s => { s.State = ConnectionState.Error; s.ErrorCode = code; s.Message = message; });

    protected ConnectionState State
    {
        get { lock (_lock) return _status.State; }
    }

    public abstract Task StartAsync(CancellationToken ct);

    public abstract Task StopAsync();

    public virtual async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }
}
