using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Xtream;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// Builds the list of playback URLs a regular IPTV player would try for one title.
/// </summary>
/// <remarks>
/// Panels routinely advertise the wrong <c>container_extension</c>. TiviMate and VLC just walk
/// <c>mp4</c>, <c>mkv</c>, <c>avi</c>, and <c>ts</c> until one answers. Plugins that trust the
/// advertised extension and stop there are the ones that "fail to load movies or series".
/// </remarks>
public static class StreamUrlCandidates
{
    /// <summary>
    /// The on-demand containers tried after the advertised one.
    /// </summary>
    public static readonly string[] VodExtensions =
    {
        "mp4", "mkv", "avi", "ts", "m3u8", "mpg", "mpeg", "flv", "mov", "wmv", "m4v"
    };

    /// <summary>
    /// The live containers tried after the configured format.
    /// </summary>
    public static readonly string[] LiveExtensions = { "ts", "m3u8" };

    /// <summary>
    /// Builds the candidate URLs for one playback request, most likely first.
    /// </summary>
    /// <param name="provider">The owning provider.</param>
    /// <param name="kind">The stream kind.</param>
    /// <param name="streamId">The stream identifier within the provider.</param>
    /// <param name="preferredContainer">The container the catalog advertised, if any.</param>
    /// <param name="upstreamUrl">The original upstream address, if any.</param>
    /// <returns>The addresses to try, with duplicates removed.</returns>
    public static IReadOnlyList<string> Build(
        ProviderOptions provider,
        PlaybackKind kind,
        string streamId,
        string? preferredContainer,
        string? upstreamUrl)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        void Add(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
            {
                return;
            }

            candidates.Add(url);
        }

        Add(upstreamUrl);

        if (!string.IsNullOrWhiteSpace(upstreamUrl))
        {
            foreach (var extension in EnumerateExtensions(kind, preferredContainer, provider.LiveStreamFormat))
            {
                Add(ReplaceExtension(upstreamUrl, extension));
            }
        }

        if (provider.Kind == ProviderKind.Xtream)
        {
            var urls = new XtreamUrlBuilder(provider);
            if (urls.IsConfigured)
            {
                foreach (var extension in EnumerateExtensions(kind, preferredContainer, provider.LiveStreamFormat))
                {
                    Add(BuildXtreamUrl(urls, kind, streamId, extension, provider.LiveStreamFormat));
                }
            }
        }

        return candidates;
    }

    /// <summary>
    /// Replaces the file extension of an HTTP URL, leaving the query string intact.
    /// </summary>
    /// <param name="url">The original address.</param>
    /// <param name="extension">The extension to use, without a leading dot.</param>
    /// <returns>The rewritten address, or the original when it has no replaceable path.</returns>
    public static string ReplaceExtension(string url, string extension)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(extension);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var path = uri.AbsolutePath;
        var current = Path.GetExtension(path);
        if (string.IsNullOrEmpty(current))
        {
            path += "." + extension.TrimStart('.');
        }
        else if (current.Equals("." + extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
        else
        {
            path = path[..^current.Length] + "." + extension.TrimStart('.');
        }

        var builder = new UriBuilder(uri) { Path = path };
        return builder.Uri.ToString();
    }

    /// <summary>
    /// Reads a file extension off an HTTP URL.
    /// </summary>
    /// <param name="url">The address.</param>
    /// <returns>The extension without a leading dot, or <c>null</c> when there is none.</returns>
    public static string? GetExtension(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.');
        return string.IsNullOrWhiteSpace(extension) ? null : extension;
    }

    /// <summary>
    /// Normalizes a container extension to a lowercase token without a leading dot.
    /// </summary>
    /// <param name="value">The raw extension.</param>
    /// <returns>The normalized extension, or <c>null</c> when <paramref name="value"/> is empty.</returns>
    public static string? NormalizeExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimStart('.').ToLowerInvariant();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static IEnumerable<string> EnumerateExtensions(PlaybackKind kind, string? preferred, StreamFormat liveFormat)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preferredNormalized = NormalizeExtension(preferred);

        if (preferredNormalized is not null && yielded.Add(preferredNormalized))
        {
            yield return preferredNormalized;
        }

        var extras = kind == PlaybackKind.Live
            ? LiveOrder(liveFormat)
            : VodExtensions;

        foreach (var extension in extras)
        {
            if (yielded.Add(extension))
            {
                yield return extension;
            }
        }
    }

    private static IReadOnlyList<string> LiveOrder(StreamFormat format)
        => format == StreamFormat.Hls
            ? new[] { "m3u8", "ts" }
            : new[] { "ts", "m3u8" };

    private static string? BuildXtreamUrl(
        XtreamUrlBuilder urls,
        PlaybackKind kind,
        string streamId,
        string extension,
        StreamFormat liveFormat)
    {
        switch (kind)
        {
            case PlaybackKind.Movie:
                if (long.TryParse(streamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var movieId))
                {
                    return urls.MovieStream(movieId, extension);
                }

                return null;

            case PlaybackKind.Series:
                return string.IsNullOrWhiteSpace(streamId) ? null : urls.EpisodeStream(streamId, extension);

            case PlaybackKind.Live:
                if (long.TryParse(streamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var liveId))
                {
                    var format = extension.Equals("m3u8", StringComparison.OrdinalIgnoreCase)
                        ? StreamFormat.Hls
                        : StreamFormat.MpegTs;
                    return urls.LiveStream(liveId, format);
                }

                return null;

            default:
                return null;
        }
    }
}
