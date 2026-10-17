using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using VpnClient.Core;

namespace VpnClient.App.Services;

/// <summary>Client named pipe vers VpnClientService. Se reconnecte automatiquement.</summary>
public sealed class ServiceClient : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public async Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await EnsureConnectedAsync(ct);
                    await _writer!.WriteLineAsync(JsonSerializer.Serialize(request, Ipc.Json).AsMemory(), ct);
                    var line = await _reader!.ReadLineAsync(ct) ?? throw new IOException("Connexion fermée par le service.");
                    return JsonSerializer.Deserialize<IpcResponse>(line, Ipc.Json) ?? IpcResponse.Fail("Réponse vide.");
                }
                catch (IOException) when (attempt == 0)
                {
                    // Le service a peut-être redémarré : on retente une fois avec une nouvelle connexion.
                    Reset();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Reset();
            return IpcResponse.Fail("Service VPN injoignable.", VpnErrorCode.ServiceUnavailable);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_pipe is { IsConnected: true }) return;
        Reset();

        _pipe = new NamedPipeClientStream(".", Ipc.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(1500, ct);
        _reader = new StreamReader(_pipe);
        _writer = new StreamWriter(_pipe) { AutoFlush = true, NewLine = "\n" };
    }

    private void Reset()
    {
        _reader?.Dispose();
        _writer = null;
        _reader = null;
        _pipe?.Dispose();
        _pipe = null;
    }

    public void Dispose() => Reset();
}
