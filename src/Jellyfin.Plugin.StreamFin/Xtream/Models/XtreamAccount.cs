using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.StreamFin.Xtream.Json;

namespace Jellyfin.Plugin.StreamFin.Xtream.Models;

/// <summary>
/// The <c>player_api.php</c> handshake response.
/// </summary>
public class XtreamAccount
{
    /// <summary>
    /// Gets or sets the subscription details.
    /// </summary>
    [JsonPropertyName("user_info")]
    public XtreamUserInfo? UserInfo { get; set; }

    /// <summary>
    /// Gets or sets the panel details.
    /// </summary>
    [JsonPropertyName("server_info")]
    public XtreamServerInfo? ServerInfo { get; set; }
}

/// <summary>
/// Subscription details for the configured credentials.
/// </summary>
public class XtreamUserInfo
{
    /// <summary>
    /// Gets or sets the account username.
    /// </summary>
    [JsonPropertyName("username")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the credentials were accepted.
    /// </summary>
    [JsonPropertyName("auth")]
    [JsonConverter(typeof(TolerantBooleanConverter))]
    public bool Authenticated { get; set; }

    /// <summary>
    /// Gets or sets the account status, typically <c>Active</c>, <c>Expired</c>, or <c>Banned</c>.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the expiry as a Unix timestamp. Null means the line never expires.
    /// </summary>
    [JsonPropertyName("exp_date")]
    [JsonConverter(typeof(TolerantInt64Converter))]
    public long? ExpiryUnix { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a trial line.
    /// </summary>
    [JsonPropertyName("is_trial")]
    [JsonConverter(typeof(TolerantBooleanConverter))]
    public bool IsTrial { get; set; }

    /// <summary>
    /// Gets or sets the number of streams currently open on this account.
    /// </summary>
    [JsonPropertyName("active_cons")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? ActiveConnections { get; set; }

    /// <summary>
    /// Gets or sets the number of simultaneous streams the account allows.
    /// </summary>
    [JsonPropertyName("max_connections")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? MaxConnections { get; set; }

    /// <summary>
    /// Gets or sets the output formats the panel advertises, such as <c>m3u8</c>, <c>ts</c>, or <c>rtmp</c>.
    /// </summary>
    [JsonPropertyName("allowed_output_formats")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public IReadOnlyList<string> AllowedOutputFormats { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets the expiry as a UTC timestamp.
    /// </summary>
    [JsonIgnore]
    public DateTime? ExpiresAtUtc => ExpiryUnix is null or <= 0
        ? null
        : DateTimeOffset.FromUnixTimeSeconds(ExpiryUnix.Value).UtcDateTime;

    /// <summary>
    /// Gets a value indicating whether the line is usable right now.
    /// </summary>
    [JsonIgnore]
    public bool IsActive => Authenticated
        && (Status is null || string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase))
        && (ExpiresAtUtc is null || ExpiresAtUtc > DateTime.UtcNow);
}

/// <summary>
/// Panel details reported alongside the subscription.
/// </summary>
public class XtreamServerInfo
{
    /// <summary>
    /// Gets or sets the panel hostname.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the plain HTTP port.
    /// </summary>
    [JsonPropertyName("port")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Port { get; set; }

    /// <summary>
    /// Gets or sets the TLS port.
    /// </summary>
    [JsonPropertyName("https_port")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? HttpsPort { get; set; }

    /// <summary>
    /// Gets or sets the protocol the panel prefers.
    /// </summary>
    [JsonPropertyName("server_protocol")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? ServerProtocol { get; set; }

    /// <summary>
    /// Gets or sets the panel's timezone name.
    /// </summary>
    [JsonPropertyName("timezone")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Timezone { get; set; }
}
