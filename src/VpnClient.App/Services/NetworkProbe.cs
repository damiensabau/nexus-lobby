using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;

namespace VpnClient.App.Services;

public static class NetworkProbe
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

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
