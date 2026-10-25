using System;
using System.Collections.Generic;
using Jellyfin.Plugin.StreamFin.Configuration;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// Builds the HTTP headers a regular IPTV player would send to a panel.
/// </summary>
public static class PlaybackHeaders
{
    /// <summary>
    /// Builds the headers StreamFin attaches to provider requests and to ffmpeg.
    /// </summary>
    /// <param name="provider">The provider being contacted.</param>
    /// <returns>The headers to send.</returns>
    public static Dictionary<string, string> Build(ProviderOptions provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = UserAgents.Resolve(provider.UserAgent),
            ["Accept"] = "*/*"
        };

        var referer = ResolveReferer(provider);
        if (!string.IsNullOrWhiteSpace(referer))
        {
            headers["Referer"] = referer;
            headers["Origin"] = referer.TrimEnd('/');
        }

        return headers;
    }

    /// <summary>
    /// Picks the Referer a provider should be contacted with.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The Referer, or <c>null</c> when none is appropriate.</returns>
    public static string? ResolveReferer(ProviderOptions provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!string.IsNullOrWhiteSpace(provider.Referer))
        {
            return provider.Referer.Trim();
        }

        var server = provider.GetNormalizedServerUrl();
        return string.IsNullOrWhiteSpace(server) ? null : server + "/";
    }
}
