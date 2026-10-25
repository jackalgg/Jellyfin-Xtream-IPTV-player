using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.StreamFin.M3u;

/// <summary>
/// Parses M3U and M3U-plus playlists.
/// </summary>
/// <remarks>
/// Written to survive the playlists resellers actually publish: byte order marks, CR line endings,
/// attributes with unquoted or single-quoted values, <c>#EXTGRP</c> instead of <c>group-title</c>,
/// Kodi and VLC option lines between the <c>#EXTINF</c> and its URL, and commas inside the display
/// name. A parser that gives up on any of those drops entries silently, which reads to the user as
/// "the plugin only loaded some of my channels".
/// </remarks>
public static class M3uParser
{
    private static readonly char[] _attributeTerminators = { ' ', ',' };
    private static readonly char[] UrlSeparators = { ',', ';', ' ' };

    /// <summary>
    /// Parses a playlist from a stream.
    /// </summary>
    /// <param name="stream">The playlist content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed playlist.</returns>
    public static async Task<M3uPlaylist> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return Parse(text);
    }

    /// <summary>
    /// Parses a playlist from text.
    /// </summary>
    /// <param name="content">The playlist content.</param>
    /// <returns>The parsed playlist.</returns>
    public static M3uPlaylist Parse(string content)
    {
        var playlist = new M3uPlaylist();
        if (string.IsNullOrWhiteSpace(content))
        {
            return playlist;
        }

        var entries = new List<M3uEntry>();

        // Classic Mac line endings still turn up in hand-edited playlists.
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        M3uEntry? pending = null;
        string? pendingGroup = null;
        string? pendingUserAgent = null;
        string? pendingReferer = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                var headerAttributes = ParseAttributes(line);
                if (headerAttributes.TryGetValue("url-tvg", out var urlTvg) && !string.IsNullOrWhiteSpace(urlTvg))
                {
                    playlist.EpgUrl = FirstUrl(urlTvg);
                }
                else if (headerAttributes.TryGetValue("x-tvg-url", out var xTvgUrl) && !string.IsNullOrWhiteSpace(xTvgUrl))
                {
                    playlist.EpgUrl = FirstUrl(xTvgUrl);
                }

                continue;
            }

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                pending = ParseExtInf(line);
                pendingGroup = null;
                pendingUserAgent = null;
                pendingReferer = null;
                continue;
            }

            if (line.StartsWith("#EXTGRP", StringComparison.OrdinalIgnoreCase))
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon >= 0)
                {
                    pendingGroup = line[(colon + 1)..].Trim();
                }

                continue;
            }

            if (line.StartsWith("#EXTVLCOPT", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("#KODIPROP", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("#EXTHTTP", StringComparison.OrdinalIgnoreCase))
            {
                ApplyOption(line, ref pendingUserAgent, ref pendingReferer);
                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            // Anything that is not a directive is this entry's URL.
            if (pending is null)
            {
                // A bare URL with no #EXTINF still deserves to show up.
                pending = new M3uEntry { Name = DeriveNameFromUrl(line) };
            }

            pending.Url = line;
            pending.UserAgent = pendingUserAgent;
            pending.Referer = pendingReferer;

            if (!string.IsNullOrWhiteSpace(pendingGroup) && string.IsNullOrWhiteSpace(pending.GroupTitle))
            {
                var merged = new Dictionary<string, string>(pending.Attributes, StringComparer.OrdinalIgnoreCase)
                {
                    ["group-title"] = pendingGroup
                };
                pending.Attributes = merged;
            }

            pending.Kind = Classify(pending);
            entries.Add(pending);

            pending = null;
            pendingGroup = null;
            pendingUserAgent = null;
            pendingReferer = null;
        }

        playlist.Entries = entries;
        return playlist;
    }

    private static M3uEntry ParseExtInf(string line)
    {
        var entry = new M3uEntry();

        var colon = line.IndexOf(':', StringComparison.Ordinal);
        var payload = colon >= 0 ? line[(colon + 1)..] : string.Empty;

        // The display name follows the last comma that is not inside a quoted attribute value.
        var nameSeparator = FindNameSeparator(payload);
        var attributePart = nameSeparator >= 0 ? payload[..nameSeparator] : payload;
        entry.Name = nameSeparator >= 0 ? payload[(nameSeparator + 1)..].Trim() : string.Empty;

        var durationText = attributePart.TrimStart();
        var durationEnd = durationText.IndexOfAny(_attributeTerminators);
        if (durationEnd > 0
            && double.TryParse(durationText[..durationEnd], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
        {
            entry.Duration = duration;
        }
        else if (double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var onlyDuration))
        {
            entry.Duration = onlyDuration;
        }

        entry.Attributes = ParseAttributes(attributePart);

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            entry.Name = entry.TvgName ?? string.Empty;
        }

        return entry;
    }

    /// <summary>
    /// Finds the comma that separates the attributes from the display name. Commas inside quoted
    /// attribute values do not count, and the display name itself may contain commas, so the first
    /// unquoted comma is the separator.
    /// </summary>
    private static int FindNameSeparator(string payload)
    {
        var inQuotes = false;
        var quoteChar = '"';

        for (var i = 0; i < payload.Length; i++)
        {
            var c = payload[i];

            if (inQuotes)
            {
                if (c == quoteChar)
                {
                    inQuotes = false;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                inQuotes = true;
                quoteChar = c;
                continue;
            }

            if (c == ',')
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyDictionary<string, string> ParseAttributes(string text)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var i = 0;
        while (i < text.Length)
        {
            var equals = text.IndexOf('=', i);
            if (equals < 0)
            {
                break;
            }

            // Walk back from the '=' to the start of the key.
            var keyStart = equals - 1;
            while (keyStart >= 0 && !char.IsWhiteSpace(text[keyStart]) && text[keyStart] != ',' && text[keyStart] != ':')
            {
                keyStart--;
            }

            keyStart++;
            var key = text[keyStart..equals].Trim();

            var valueStart = equals + 1;
            if (valueStart >= text.Length)
            {
                break;
            }

            string value;
            var quote = text[valueStart];
            if (quote is '"' or '\'')
            {
                var closing = text.IndexOf(quote, valueStart + 1);
                if (closing < 0)
                {
                    value = text[(valueStart + 1)..];
                    i = text.Length;
                }
                else
                {
                    value = text[(valueStart + 1)..closing];
                    i = closing + 1;
                }
            }
            else
            {
                var end = text.IndexOfAny(_attributeTerminators, valueStart);
                if (end < 0)
                {
                    value = text[valueStart..];
                    i = text.Length;
                }
                else
                {
                    value = text[valueStart..end];
                    i = end + 1;
                }
            }

            if (key.Length > 0)
            {
                attributes[key] = value.Trim();
            }
        }

        return attributes;
    }

    private static void ApplyOption(string line, ref string? userAgent, ref string? referer)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return;
        }

        var option = line[(colon + 1)..].Trim();
        var equals = option.IndexOf('=', StringComparison.Ordinal);
        if (equals < 0)
        {
            return;
        }

        var key = option[..equals].Trim();
        var value = option[(equals + 1)..].Trim().Trim('"', '\'');

        if (key.EndsWith("user-agent", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("user_agent", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("User-Agent", StringComparison.Ordinal))
        {
            userAgent = value;
        }
        else if (key.EndsWith("referer", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("referrer", StringComparison.OrdinalIgnoreCase))
        {
            referer = value;
        }
    }

    private static M3uEntryKind Classify(M3uEntry entry)
    {
        // Xtream's m3u_plus output encodes the content type in the URL path, which is far more
        // reliable than guessing from the group name.
        if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath;
            if (path.Contains("/series/", StringComparison.OrdinalIgnoreCase))
            {
                return M3uEntryKind.Episode;
            }

            if (path.Contains("/movie/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/movies/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/vod/", StringComparison.OrdinalIgnoreCase))
            {
                return M3uEntryKind.Movie;
            }

            if (path.Contains("/live/", StringComparison.OrdinalIgnoreCase))
            {
                return M3uEntryKind.Live;
            }
        }

        // No usable path, so fall back to the remaining signals: an episode marker in the name, a
        // finite duration (live streams always declare -1), and the group name.
        if (Catalog.TitleParser.ParseEpisode(entry.Name) is not null)
        {
            return M3uEntryKind.Episode;
        }

        if (entry.Duration > 0)
        {
            return M3uEntryKind.Movie;
        }

        var group = entry.GroupTitle ?? string.Empty;
        if (group.Contains("vod", StringComparison.OrdinalIgnoreCase)
            || group.Contains("movie", StringComparison.OrdinalIgnoreCase)
            || group.Contains("film", StringComparison.OrdinalIgnoreCase))
        {
            return M3uEntryKind.Movie;
        }

        return M3uEntryKind.Live;
    }

    private static string FirstUrl(string value)
    {
        var first = value.Split(UrlSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        return first ?? value.Trim();
    }

    private static string DeriveNameFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        return string.IsNullOrWhiteSpace(name) ? uri.Host : Uri.UnescapeDataString(name);
    }
}
