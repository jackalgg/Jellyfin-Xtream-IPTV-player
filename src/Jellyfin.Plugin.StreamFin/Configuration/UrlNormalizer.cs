using System;

namespace Jellyfin.Plugin.StreamFin.Configuration;

/// <summary>
/// Cleans up the server addresses people paste out of reseller emails.
/// </summary>
public static class UrlNormalizer
{
    private static readonly string[] _strippableSuffixes =
    {
        "/player_api.php",
        "/panel_api.php",
        "/get.php",
        "/xmltv.php",
        "/c",
        "/portal.php"
    };

    /// <summary>
    /// Turns a pasted address into a bare scheme, host, and port.
    /// </summary>
    /// <param name="value">The raw address.</param>
    /// <returns>A normalized base address, or an empty string when <paramref name="value"/> is unusable.</returns>
    /// <remarks>
    /// Accepts <c>example.com</c>, <c>example.com:8080</c>, <c>http://example.com:8080/</c>, and full
    /// <c>player_api.php?username=...</c> URLs, all of which people routinely paste in.
    /// </remarks>
    public static string NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        // A query string means the user pasted a full API or playlist URL.
        var queryIndex = text.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            text = text[..queryIndex];
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var suffix in _strippableSuffixes)
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length];
                break;
            }
        }

        var builder = new UriBuilder(uri)
        {
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty
        };

        // UriBuilder re-adds the default port for the scheme; drop it so URLs stay recognizable.
        if ((builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80)
            || (builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443))
        {
            builder.Port = -1;
        }

        return builder.Uri.ToString().TrimEnd('/');
    }

    /// <summary>
    /// Resolves a possibly relative address against a base address.
    /// </summary>
    /// <param name="baseUrl">The provider base address.</param>
    /// <param name="value">The address to resolve.</param>
    /// <returns>An absolute address, or <c>null</c> when one cannot be formed.</returns>
    public static string? ResolveAbsolute(string? baseUrl, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (!string.IsNullOrWhiteSpace(baseUrl)
            && Uri.TryCreate(baseUrl.TrimEnd('/') + "/" + text.TrimStart('/'), UriKind.Absolute, out var combined))
        {
            return combined.ToString();
        }

        return null;
    }
}
