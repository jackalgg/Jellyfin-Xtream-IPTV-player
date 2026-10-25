using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.StreamFin.Configuration;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// Builds the local playback URLs written into <c>.strm</c> files.
/// </summary>
/// <remarks>
/// Jellyfin's own strm reader hands ffmpeg whatever URL is in the file, and ffmpeg does not send
/// the User-Agent or try another container when the first one 404s. Pointing the file at a
/// StreamFin endpoint means the request goes through the same HTTP stack a regular IPTV player
/// uses: the right headers, redirect following, and a walk of <c>mp4</c>/<c>mkv</c>/<c>ts</c>.
/// The URL is signed so the anonymous endpoint cannot be used as an open proxy.
/// </remarks>
public class PlaybackLinkFactory
{
    /// <summary>
    /// The path prefix StreamFin's play endpoint lives under.
    /// </summary>
    public const string PlayPathPrefix = "/StreamFin/Play/";

    private readonly IServerApplicationHost _applicationHost;
    private readonly ILogger<PlaybackLinkFactory> _logger;
    private readonly object _keyLock = new();
    private byte[]? _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackLinkFactory"/> class.
    /// </summary>
    /// <param name="applicationHost">The server host, used for the listen port.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackLinkFactory(IServerApplicationHost applicationHost, ILogger<PlaybackLinkFactory> logger)
    {
        _applicationHost = applicationHost;
        _logger = logger;
    }

    /// <summary>
    /// Gets the HMAC key used to sign playback URLs, creating one on first use.
    /// </summary>
    /// <returns>The key bytes.</returns>
    public byte[] GetKey()
    {
        if (_key is not null)
        {
            return _key;
        }

        lock (_keyLock)
        {
            if (_key is not null)
            {
                return _key;
            }

            _key = LoadOrCreateKey();
            return _key;
        }
    }

    /// <summary>
    /// Builds the contents of a <c>.strm</c> file for one stream.
    /// </summary>
    /// <param name="options">The plugin settings.</param>
    /// <param name="provider">The owning provider.</param>
    /// <param name="kind">The stream kind.</param>
    /// <param name="streamId">The stream identifier within the provider.</param>
    /// <param name="upstreamUrl">The original upstream address.</param>
    /// <param name="container">The advertised container extension.</param>
    /// <returns>The text to write into the strm file.</returns>
    public string BuildStrm(
        PluginConfiguration options,
        ProviderOptions provider,
        PlaybackKind kind,
        string streamId,
        string upstreamUrl,
        string? container)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);

        if (!options.ProxyPlayback || string.IsNullOrWhiteSpace(upstreamUrl))
        {
            return upstreamUrl ?? string.Empty;
        }

        return BuildPlayUrl(provider, kind, streamId, upstreamUrl, container);
    }

    /// <summary>
    /// Builds a signed local play URL for one stream.
    /// </summary>
    /// <param name="provider">The owning provider.</param>
    /// <param name="kind">The stream kind.</param>
    /// <param name="streamId">The stream identifier within the provider.</param>
    /// <param name="upstreamUrl">The original upstream address.</param>
    /// <param name="container">The advertised container extension.</param>
    /// <returns>An absolute URL pointing at StreamFin's play endpoint.</returns>
    public string BuildPlayUrl(
        ProviderOptions provider,
        PlaybackKind kind,
        string streamId,
        string? upstreamUrl,
        string? container)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var normalizedContainer = StreamUrlCandidates.NormalizeExtension(container);
        var payload = PlaybackSigner.BuildPayload(kind, provider.Id, streamId, normalizedContainer, upstreamUrl);
        var signature = PlaybackSigner.Sign(GetKey(), payload);

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(normalizedContainer))
        {
            query.Add("c=" + Uri.EscapeDataString(normalizedContainer));
        }

        if (!string.IsNullOrWhiteSpace(upstreamUrl))
        {
            query.Add("u=" + Uri.EscapeDataString(Encode(upstreamUrl)));
        }

        query.Add("s=" + Uri.EscapeDataString(signature));

        var idSegment = string.IsNullOrWhiteSpace(streamId)
            ? "x"
            : Uri.EscapeDataString(streamId);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"http://127.0.0.1:{GetLocalHttpPort()}{PlayPathPrefix}{kind.ToString().ToLowerInvariant()}/{Uri.EscapeDataString(provider.Id)}/{idSegment}?{string.Join('&', query)}");
    }

    /// <summary>
    /// Parses a StreamFin play URL back into its pieces.
    /// </summary>
    /// <param name="url">The play URL.</param>
    /// <returns>The pieces, or <c>null</c> when <paramref name="url"/> is not a StreamFin play URL.</returns>
    public static ParsedPlayUrl? TryParse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.AbsolutePath.StartsWith(PlayPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var remainder = uri.AbsolutePath[PlayPathPrefix.Length..];
        var parts = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return null;
        }

        if (!Enum.TryParse(parts[0], ignoreCase: true, out PlaybackKind kind))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        query.TryGetValue("c", out var container);
        query.TryGetValue("u", out var upstream);
        query.TryGetValue("s", out var signature);

        return new ParsedPlayUrl
        {
            Kind = kind,
            ProviderId = Uri.UnescapeDataString(parts[1]),
            StreamId = Uri.UnescapeDataString(parts[2]),
            Container = container,
            UpstreamUrl = Decode(upstream),
            Signature = signature
        };
    }

    /// <summary>
    /// Returns whether <paramref name="url"/> points at StreamFin's play endpoint.
    /// </summary>
    /// <param name="url">The URL to inspect.</param>
    /// <returns><c>true</c> when it is a StreamFin play URL.</returns>
    public static bool IsPlayUrl(string? url) => TryParse(url) is not null;

    /// <summary>
    /// Verifies the signature on a parsed play URL.
    /// </summary>
    /// <param name="parsed">The parsed URL.</param>
    /// <returns><c>true</c> when the signature is valid.</returns>
    public bool Verify(ParsedPlayUrl parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var payload = PlaybackSigner.BuildPayload(
            parsed.Kind,
            parsed.ProviderId,
            parsed.StreamId,
            parsed.Container,
            parsed.UpstreamUrl);

        return PlaybackSigner.Verify(GetKey(), payload, parsed.Signature);
    }

    /// <summary>
    /// Encodes an arbitrary string for a query parameter.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>A URL-safe Base64 payload.</returns>
    public static string Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Decodes a value produced by <see cref="Encode"/>.
    /// </summary>
    /// <param name="value">The encoded value.</param>
    /// <returns>The original string, or <c>null</c> when <paramref name="value"/> is empty or malformed.</returns>
    public static string? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = query.StartsWith('?') ? query[1..] : query;
        if (text.Length == 0)
        {
            return result;
        }

        foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            result[Uri.UnescapeDataString(pair[..equals])] = Uri.UnescapeDataString(pair[(equals + 1)..]);
        }

        return result;
    }

    private int GetLocalHttpPort()
    {
        try
        {
            var port = _applicationHost.HttpPort;
            if (port > 0)
            {
                return port;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the server HTTP port; defaulting to 8096");
        }

        return 8096;
    }

    private byte[] LoadOrCreateKey()
    {
        var path = Path.Combine(
            StreamFinPlugin.Instance?.GetStateDirectory()
            ?? Path.Combine(Path.GetTempPath(), "streamfin"),
            "playback.key");

        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length >= 32)
                {
                    return existing;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not read the playback signing key: {Reason}", ex.Message);
        }

        var key = RandomNumberGenerator.GetBytes(32);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not persist the playback signing key: {Reason}", ex.Message);
        }

        return key;
    }
}

/// <summary>
/// The pieces of a StreamFin play URL.
/// </summary>
public class ParsedPlayUrl
{
    /// <summary>
    /// Gets or sets the stream kind.
    /// </summary>
    public PlaybackKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the provider identifier.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stream identifier within the provider.
    /// </summary>
    public string StreamId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the preferred container extension.
    /// </summary>
    public string? Container { get; set; }

    /// <summary>
    /// Gets or sets the original upstream address.
    /// </summary>
    public string? UpstreamUrl { get; set; }

    /// <summary>
    /// Gets or sets the HMAC signature.
    /// </summary>
    public string? Signature { get; set; }
}
