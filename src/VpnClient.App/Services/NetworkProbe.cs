using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;

namespace VpnClient.App.Services;

/// <summary>Latence mesurée vers un serveur, et comment (ICMP, ou TCP sur un port quand l'ICMP est bloqué).</summary>
public sealed record PingResult(long Ms, string Method);

public static class NetworkProbe
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly TimeSpan TcpTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Ping ICMP, puis en secours l'ouverture d'une connexion TCP (port du serveur, 443, 80).
    /// Une connexion refusée compte aussi : le serveur a répondu, le temps mesuré est un aller-retour.
    /// </summary>
    public static async Task<PingResult?> ProbeAsync(string host, int? serverPort)
    {
        if (await PingAsync(host) is { } icmp) return new PingResult(icmp, "ICMP");

        foreach (var port in new[] { serverPort, 443, 80 }.OfType<int>().Distinct())
        {
            if (await TcpProbeAsync(host, port) is { } tcp) return new PingResult(tcp, $"TCP {port}");
        }
        return null;
    }

    /// <summary>Temps (ms) pour qu'une connexion TCP aboutisse ou soit refusée ; null si rien ne répond à temps.</summary>
    public static async Task<long?> TcpProbeAsync(string host, int port)
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        using var timeout = new CancellationTokenSource(TcpTimeout);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await socket.ConnectAsync(host, port, timeout.Token);
            return clock.ElapsedMilliseconds;
        }
        catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused)
        {
            return clock.ElapsedMilliseconds;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Ping ICMP vers le serveur VPN, en ms. Null si l'hôte ne répond pas (certains serveurs bloquent l'ICMP).</summary>
    public static async Task<long?> PingAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(1500), null, null);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException)
        {
            return null;
        }
    }

    /// <summary>
    /// Code pays (ISO) sous lequel Internet te voit : le tien, ou celui du serveur quand le tunnel est actif.
    /// Ni l'IP ni la ville ne sont gardées ou affichées.
    /// </summary>
    public static async Task<string?> GetVisibleCountryAsync()
    {
        try
        {
            var info = await Http.GetFromJsonAsync<IpInfoResponse>("https://ipinfo.io/json");
            return string.IsNullOrWhiteSpace(info?.Country) ? null : info.Country;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Nom du pays en français : catalogue de l'app, sinon nom Windows du pays.</summary>
    public static string CountryName(string code)
    {
        var known = Locations.All.FirstOrDefault(l => l.CountryCode.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (known is not null) return known.Country;
        try
        {
            return new System.Globalization.RegionInfo(code).DisplayName;
        }
        catch (ArgumentException)
        {
            return code.ToUpperInvariant();
        }
    }

    private sealed record IpInfoResponse(string? Country);
}
