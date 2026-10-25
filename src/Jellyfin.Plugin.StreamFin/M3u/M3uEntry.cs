using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.StreamFin.M3u;

/// <summary>
/// What a playlist entry appears to be.
/// </summary>
public enum M3uEntryKind
{
    /// <summary>
    /// A live channel.
    /// </summary>
    Live = 0,

    /// <summary>
    /// An on-demand movie.
    /// </summary>
    Movie = 1,

    /// <summary>
    /// An episode of a series.
    /// </summary>
    Episode = 2
}

/// <summary>
/// One <c>#EXTINF</c> entry and its stream URL.
/// </summary>
public class M3uEntry
{
    /// <summary>
    /// Gets or sets the display name that followed the <c>#EXTINF</c> attributes.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stream address.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the declared duration in seconds, or -1 for a live stream.
    /// </summary>
    public double Duration { get; set; } = -1;

    /// <summary>
    /// Gets or sets the <c>#EXTINF</c> attributes, keyed case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets what the entry appears to be.
    /// </summary>
    public M3uEntryKind Kind { get; set; } = M3uEntryKind.Live;

    /// <summary>
    /// Gets or sets a per-entry User-Agent taken from <c>#EXTVLCOPT</c> or <c>#KODIPROP</c>.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Gets or sets a per-entry Referer taken from <c>#EXTVLCOPT</c> or <c>#KODIPROP</c>.
    /// </summary>
    public string? Referer { get; set; }

    /// <summary>
    /// Gets the XMLTV channel identifier, from whichever attribute the playlist used.
    /// </summary>
    public string? TvgId => FirstAttribute("tvg-id", "tvg-ID", "channel-id", "tvg_id");

    /// <summary>
    /// Gets the canonical channel name, from whichever attribute the playlist used.
    /// </summary>
    public string? TvgName => FirstAttribute("tvg-name", "tvg_name");

    /// <summary>
    /// Gets the logo address, from whichever attribute the playlist used.
    /// </summary>
    public string? TvgLogo => FirstAttribute("tvg-logo", "tvg_logo", "logo");

    /// <summary>
    /// Gets the channel number, from whichever attribute the playlist used.
    /// </summary>
    public string? TvgChannelNumber => FirstAttribute("tvg-chno", "tvg-channel-number", "channel-number", "tvg_chno");

    /// <summary>
    /// Gets the group name, from whichever attribute the playlist used.
    /// </summary>
    public string? GroupTitle => FirstAttribute("group-title", "group_title", "group");

    /// <summary>
    /// Gets the number of days of catch-up the playlist advertises.
    /// </summary>
    public string? CatchupDays => FirstAttribute("catchup-days", "timeshift", "tvg-rec", "catchup_days");

    /// <summary>
    /// Looks up the first of several attribute spellings that is present.
    /// </summary>
    /// <param name="names">The attribute names to try, in order.</param>
    /// <returns>The attribute value, or <c>null</c> when none of them is set.</returns>
    public string? FirstAttribute(params string[] names)
    {
        foreach (var name in names)
        {
            if (Attributes.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

/// <summary>
/// A parsed playlist.
/// </summary>
public class M3uPlaylist
{
    /// <summary>
    /// Gets or sets the guide address declared in the <c>#EXTM3U</c> header.
    /// </summary>
    public string? EpgUrl { get; set; }

    /// <summary>
    /// Gets or sets the entries, in playlist order.
    /// </summary>
    public IReadOnlyList<M3uEntry> Entries { get; set; } = Array.Empty<M3uEntry>();
}
