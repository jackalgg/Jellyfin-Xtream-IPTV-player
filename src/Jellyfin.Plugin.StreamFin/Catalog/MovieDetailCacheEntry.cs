using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.StreamFin.Catalog;

/// <summary>
/// A cached <c>get_vod_info</c> result.
/// </summary>
/// <remarks>
/// Cached indefinitely because a movie's metadata does not change once the panel has it, and
/// refetching it is the single most expensive part of a sync.
/// </remarks>
public class MovieDetailCacheEntry
{
    /// <summary>
    /// Gets or sets the title recorded in the panel's metadata.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the original-language title.
    /// </summary>
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// Gets or sets the synopsis.
    /// </summary>
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the poster address.
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Gets or sets the backdrop addresses.
    /// </summary>
    public IReadOnlyList<string> BackdropUrls { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the TMDB identifier.
    /// </summary>
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the genres.
    /// </summary>
    public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the cast.
    /// </summary>
    public IReadOnlyList<string> Cast { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the directors.
    /// </summary>
    public IReadOnlyList<string> Directors { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the community rating out of ten.
    /// </summary>
    public double? Rating { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the runtime in seconds.
    /// </summary>
    public long? RuntimeSeconds { get; set; }

    /// <summary>
    /// Gets or sets the certification.
    /// </summary>
    public string? Certification { get; set; }

    /// <summary>
    /// Gets or sets the trailer identifier or URL the panel supplied.
    /// </summary>
    public string? TrailerId { get; set; }
}
