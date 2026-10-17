using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnClient.Core;

// Protocole entre l'interface et le service : une ligne JSON par requête, une ligne JSON par réponse.

public static class Ipc
{
    public const string PipeName = "VpnClient.Service";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

public enum RequestType
{
    Ping,
    ListProfiles,
    ImportProfile,
    DeleteProfile,
    SetProfileLocation,
    SetProfileExpiration,
    ForgetCredentials,
    Connect,
    Disconnect,
    GetStatus,
}

public sealed class IpcRequest
{
    public RequestType Type { get; set; }
    public string? ProfileId { get; set; }

    // ImportProfile / SetProfileLocation
    public string? LocationId { get; set; }

    // ImportProfile
    public string? Name { get; set; }
    public string? FileName { get; set; }
    public string? Content { get; set; }

    // ImportProfile / SetProfileExpiration (null = pas d'expiration)
    public DateTime? ExpiresAt { get; set; }

    // Connect (OpenVPN avec auth-user-pass). Sans identifiants, le service utilise ceux mémorisés s'il y en a.
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool RememberCredentials { get; set; }
}

public sealed class IpcResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public VpnErrorCode ErrorCode { get; set; }
    public List<VpnProfile>? Profiles { get; set; }
    public VpnProfile? Profile { get; set; }
    public TunnelStatus? Status { get; set; }

    public static IpcResponse Success() => new() { Ok = true };
    public static IpcResponse Fail(string error, VpnErrorCode code = VpnErrorCode.Unknown) =>
        new() { Ok = false, Error = error, ErrorCode = code };
}
