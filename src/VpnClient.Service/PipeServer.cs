using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using VpnClient.Core;

namespace VpnClient.Service;

/// <summary>Serveur named pipe : reçoit les commandes de l'interface (une requête JSON par ligne).</summary>
public sealed class PipeServer(ProfileStore store, CredentialStore credentials, TunnelManager tunnels, ILogger<PipeServer> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("En écoute sur \\\\.\\pipe\\{Pipe}", Ipc.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }

            _ = HandleClientAsync(pipe, stoppingToken);
        }

        await tunnels.DisconnectAsync();
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var _ = pipe;
        using var reader = new StreamReader(pipe);
        await using var writer = new StreamWriter(pipe) { AutoFlush = true, NewLine = "\n" };

        try
        {
            while (pipe.IsConnected && await reader.ReadLineAsync(ct) is { } line)
            {
                IpcResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<IpcRequest>(line, Ipc.Json)
                                  ?? throw new InvalidOperationException("Requête vide.");
                    response = await DispatchAsync(request);
                }
                catch (VpnException ex)
                {
                    response = IpcResponse.Fail(ex.Message, ex.Code);
                }
                catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException or TimeoutException)
                {
                    logger.LogError(ex, "Requête IPC en échec");
                    response = IpcResponse.Fail(ex.Message);
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, Ipc.Json));
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // Client déconnecté.
        }
    }

    private async Task<IpcResponse> DispatchAsync(IpcRequest request)
    {
        switch (request.Type)
        {
            case RequestType.Ping:
                return IpcResponse.Success();

            case RequestType.ListProfiles:
                var profiles = store.All().ToList();
                foreach (var p in profiles) p.HasSavedCredentials = p.RequiresCredentials && credentials.Has(p.Id);
                return new IpcResponse { Ok = true, Profiles = profiles };

            case RequestType.ImportProfile:
                if (string.IsNullOrEmpty(request.FileName) || string.IsNullOrEmpty(request.Content))
                    return IpcResponse.Fail("Fichier manquant.", VpnErrorCode.InvalidConfig);
                var profile = store.Import(request.Name ?? "", request.FileName, request.Content, request.LocationId, request.ExpiresAt);
                return new IpcResponse { Ok = true, Profile = profile };

            case RequestType.SetProfileLocation:
                if (request.ProfileId is null) return IpcResponse.Fail("Profil manquant.", VpnErrorCode.ProfileNotFound);
                return new IpcResponse { Ok = true, Profile = store.SetLocation(request.ProfileId, request.LocationId) };

            case RequestType.SetProfileExpiration:
                if (request.ProfileId is null) return IpcResponse.Fail("Profil manquant.", VpnErrorCode.ProfileNotFound);
                return new IpcResponse { Ok = true, Profile = store.SetExpiration(request.ProfileId, request.ExpiresAt) };

            case RequestType.ForgetCredentials:
                if (request.ProfileId is null) return IpcResponse.Fail("Profil manquant.", VpnErrorCode.ProfileNotFound);
                credentials.Forget(request.ProfileId);
                return IpcResponse.Success();

            case RequestType.DeleteProfile:
                if (request.ProfileId is null) return IpcResponse.Fail("Profil manquant.", VpnErrorCode.ProfileNotFound);
                if (tunnels.IsActive(request.ProfileId)) await tunnels.DisconnectAsync();
                credentials.Forget(request.ProfileId);
                return store.Delete(request.ProfileId) ? IpcResponse.Success() : IpcResponse.Fail("Profil introuvable.", VpnErrorCode.ProfileNotFound);

            case RequestType.Connect:
                if (request.ProfileId is null) return IpcResponse.Fail("Profil manquant.", VpnErrorCode.ProfileNotFound);
                var (username, password) = ResolveCredentials(request);
                await tunnels.ConnectAsync(request.ProfileId, username, password);
                return new IpcResponse { Ok = true, Status = tunnels.GetStatus() };

            case RequestType.Disconnect:
                await tunnels.DisconnectAsync();
                return new IpcResponse { Ok = true, Status = tunnels.GetStatus() };

            case RequestType.GetStatus:
                return new IpcResponse { Ok = true, Status = tunnels.GetStatus() };

            default:
                return IpcResponse.Fail($"Requête inconnue : {request.Type}");
        }
    }

    /// <summary>
    /// Identifiants saisis : mémorisés si demandé, sinon on oublie les anciens.
    /// Rien de saisi : on reprend ceux mémorisés (s'il y en a).
    /// </summary>
    private (string? Username, string? Password) ResolveCredentials(IpcRequest request)
    {
        var id = request.ProfileId!;
        if (!string.IsNullOrEmpty(request.Username) && !string.IsNullOrEmpty(request.Password))
        {
            if (request.RememberCredentials) credentials.Save(id, request.Username, request.Password);
            else credentials.Forget(id);
            return (request.Username, request.Password);
        }

        return credentials.Load(id) is { } saved ? (saved.Username, saved.Password) : (null, null);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        // Seules les sessions ouvertes sur ce PC (INTERACTIVE) peuvent piloter le VPN ; SYSTEM et les admins ont
        // le contrôle total. Les accès réseau (pipe via SMB depuis une autre machine) sont refusés explicitement.
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            Ipc.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }
}
