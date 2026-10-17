using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using VpnClient.Core;

namespace VpnClient.Service.Tunnels;

/// <summary>
/// Lance openvpn.exe (OpenVPN Community 2.6+) et le pilote via son interface de management
/// (socket TCP local protégé par mot de passe) : état, octets transférés, identifiants.
/// </summary>
public sealed class OpenVpnTunnel(
    VpnProfile profile, string configPath, string? username, string? password, VpnOptions options, ILogger logger)
    : TunnelBase(profile)
{
    private Process? _process;
    private TcpClient? _management;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _reader;
    private string? _lastLog;
    private bool _stopping;

    public override async Task StartAsync(CancellationToken ct)
    {
        if (!File.Exists(options.OpenVpnExe))
            throw new VpnException(VpnErrorCode.OpenVpnMissing, "openvpn.exe introuvable : " + options.OpenVpnExe);
        if (Profile.RequiresCredentials && (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)))
            throw new VpnException(VpnErrorCode.CredentialsRequired, "Ce profil demande un identifiant et un mot de passe.");

        Update(s => { s.State = ConnectionState.Connecting; s.Message = "Lancement d'OpenVPN…"; });

        var port = GetFreePort();
        var managementPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var pwFile = Path.Combine(Path.GetDirectoryName(configPath)!, $".mgmt-{Profile.Id}.txt");
        await File.WriteAllTextAsync(pwFile, managementPassword + "\n", ct);

        try
        {
            var psi = new ProcessStartInfo(options.OpenVpnExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(configPath)!,
            };
            foreach (var arg in new[]
            {
                "--config", configPath,
                // Après --config : écrase toute valeur du profil.
                "--script-security", "1",
                "--management", "127.0.0.1", port.ToString(), pwFile,
                "--management-hold",
                "--management-query-passwords",
                "--auth-retry", "nointeract",
                "--verb", "3",
            })
            {
                psi.ArgumentList.Add(arg);
            }

            _process = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de lancer openvpn.exe");
            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _lastLog = e.Data; };
            _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _lastLog = e.Data; };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _process.Exited += (_, _) => OnProcessExited();

            _management = await ConnectManagementAsync(port, ct);
            var stream = _management.GetStream();
            _writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };

            // OpenVPN affiche "ENTER PASSWORD:" sans retour à la ligne : on répond directement.
            await _writer.WriteLineAsync(managementPassword);

            _cts = new CancellationTokenSource();
            _reader = ReadLoopAsync(new StreamReader(stream), _cts.Token);

            await _writer.WriteLineAsync("state on");
            await _writer.WriteLineAsync("bytecount 1");
            await _writer.WriteLineAsync("hold release");

            // OpenVPN réessaie indéfiniment si le serveur ne répond pas : on abandonne au bout d'un moment.
            _ = AbandonIfStillConnectingAsync(_cts.Token);
        }
        finally
        {
            // OpenVPN lit le fichier au démarrage du socket de management.
            _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => TryDelete(pwFile));
        }
    }

    public override async Task StopAsync()
    {
        if (_process is null || State == ConnectionState.Disconnected) return;
        _stopping = true;
        Update(s => { s.State = ConnectionState.Disconnecting; s.Message = "Déconnexion…"; });

        try
        {
            if (_writer is not null) await _writer.WriteLineAsync("signal SIGTERM");
        }
        catch (IOException) { }

        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        {
            try { await _process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { _process.Kill(entireProcessTree: true); }
        }

        if (_cts is not null) await _cts.CancelAsync();
        _management?.Dispose();
        _process.Dispose();
        _process = null;
        Update(s => { s.State = ConnectionState.Disconnected; s.Message = null; s.BytesReceived = s.BytesSent = 0; });
    }

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private async Task AbandonIfStillConnectingAsync(CancellationToken ct)
    {
        try { await Task.Delay(ConnectTimeout, ct); }
        catch (OperationCanceledException) { return; }

        if (State != ConnectionState.Connecting) return;
        logger.LogWarning("OpenVPN : pas de connexion à {Profile} après {Delay}", Profile.Id, ConnectTimeout);
        _stopping = true;
        Fail(VpnErrorCode.ServerUnreachable, _lastLog ?? "Le serveur OpenVPN ne répond pas.");
        try { _process?.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
            {
                await HandleManagementLineAsync(line);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task HandleManagementLineAsync(string line)
    {
        if (line.StartsWith(">STATE:", StringComparison.Ordinal))
        {
            // >STATE:<time>,<état>,<détail>,<ip locale>,<ip distante>,...
            var parts = line[7..].Split(',');
            if (parts.Length < 2) return;
            var name = parts[1];
            Update(s =>
            {
                switch (name)
                {
                    case "CONNECTED":
                        s.State = ConnectionState.Connected;
                        s.Message = "Tunnel actif";
                        if (parts.Length > 3 && parts[3].Length > 0) s.LocalAddress = parts[3];
                        break;
                    case "RECONNECTING":
                        s.State = ConnectionState.Connecting;
                        s.Message = "Reconnexion…";
                        break;
                    case "EXITING":
                        break;
                    default:
                        s.State = ConnectionState.Connecting;
                        s.Message = StateLabel(name);
                        break;
                }
            });
        }
        else if (line.StartsWith(">BYTECOUNT:", StringComparison.Ordinal))
        {
            var parts = line[11..].Split(',');
            if (parts.Length == 2 && long.TryParse(parts[0], out var rx) && long.TryParse(parts[1], out var tx))
                Update(s => { s.BytesReceived = rx; s.BytesSent = tx; });
        }
        else if (line.StartsWith(">PASSWORD:Need 'Auth'", StringComparison.Ordinal))
        {
            await _writer!.WriteLineAsync($"username \"Auth\" {Quote(username ?? "")}");
            await _writer.WriteLineAsync($"password \"Auth\" {Quote(password ?? "")}");
        }
        else if (line.StartsWith(">PASSWORD:Need 'Private Key'", StringComparison.Ordinal))
        {
            Fail(VpnErrorCode.PrivateKeyProtected, "Clé privée protégée par mot de passe.");
        }
        else if (line.StartsWith(">PASSWORD:Verification Failed", StringComparison.Ordinal))
        {
            _stopping = true;
            Fail(VpnErrorCode.AuthFailed, "Identifiant ou mot de passe refusé par le serveur.");
        }
        else if (line.StartsWith(">FATAL:", StringComparison.Ordinal))
        {
            Fail(VpnErrorCode.Unknown, line[7..]);
        }
    }

    private void OnProcessExited()
    {
        if (_stopping && State != ConnectionState.Error) return;
        var code = _process?.ExitCode;
        logger.LogWarning("openvpn.exe s'est arrêté (code {Code}) : {Log}", code, _lastLog);
        if (State == ConnectionState.Error) return;
        Fail(State == ConnectionState.Connected ? VpnErrorCode.TunnelLost : VpnErrorCode.ServerUnreachable,
            _lastLog ?? $"OpenVPN s'est arrêté (code {code}).");
    }

    private async Task<TcpClient> ConnectManagementAsync(int port, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (_process!.HasExited) throw new VpnException(VpnErrorCode.InvalidConfig, _lastLog ?? "openvpn.exe s'est arrêté au démarrage.");
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, ct);
                return client;
            }
            catch (SocketException)
            {
                client.Dispose();
                await Task.Delay(200, ct);
            }
        }
        throw new VpnException(VpnErrorCode.Timeout, "L'interface de management d'OpenVPN ne répond pas.");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string StateLabel(string state) => state switch
    {
        "CONNECTING" => "Connexion au serveur…",
        "WAIT" => "Attente du serveur…",
        "AUTH" => "Authentification…",
        "GET_CONFIG" => "Récupération de la configuration…",
        "ASSIGN_IP" => "Attribution de l'adresse IP…",
        "ADD_ROUTES" => "Ajout des routes…",
        "RESOLVE" => "Résolution DNS…",
        "TCP_CONNECT" => "Connexion TCP…",
        _ => state,
    };

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
