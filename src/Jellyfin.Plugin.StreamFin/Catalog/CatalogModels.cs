using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.StreamFin.Catalog;

/// <summary>
/// Everything one provider offers, normalized so that the rest of the plugin does not care whether
/// it came from an Xtream panel or an M3U playlist.
/// </summary>
public class ProviderCatalog
{
    /// <summary>
    /// Gets or sets the provider this catalog describes.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider's display name.
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the catalog was fetched.
    /// </summary>
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the live channels.
    /// </summary>
    public IReadOnlyList<CatalogChannel> Channels { get; set; } = Array.Empty<CatalogChannel>();

    /// <summary>
    /// Gets or sets the movies.
    /// </summary>
    public IReadOnlyList<CatalogMovie> Movies { get; set; } = Array.Empty<CatalogMovie>();

    /// <summary>
    /// Gets or sets the series.
    /// </summary>
    public IReadOnlyList<CatalogSeries> Series { get; set; } = Array.Empty<CatalogSeries>();

    /// <summary>
    /// Gets or sets the guide address discovered while fetching, which an M3U header can supply.
    /// </summary>
    public string? DiscoveredEpgUrl { get; set; }
}

/// <summary>
/// A live channel.
/// </summary>
public class CatalogChannel
{
    /// <summary>
    /// Gets or sets the identifier, unique within the provider.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel number shown in the guide.
    /// </summary>
    public string? Number { get; set; }

    /// <summary>
    /// Gets or sets the category the provider groups this channel under.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Gets or sets the logo address.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Gets or sets the XMLTV channel identifier this channel's guide data is published under.
    /// </summary>
    public string? EpgChannelId { get; set; }

    /// <summary>
    /// Gets or sets the playback address.
    /// </summary>
    public string StreamUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether catch-up is available.
    /// </summary>
    public bool HasArchive { get; set; }

    /// <summary>
    /// Gets or sets how many days of catch-up are retained.
    /// </summary>
    public int ArchiveDays { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the channel is advertised as high definition.
    /// </summary>
    public bool? IsHd { get; set; }
}

/// <summary>
/// Metadata shared by movies and series.
/// </summary>
public abstract class CatalogTitle
{
    /// <summary>
    /// Gets or sets the identifier, unique within the provider.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title with any year, language tag, or quality tag removed.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the original-language title.
    /// </summary>
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// Gets or sets the category the provider groups this title under.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Gets or sets the poster address.
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Gets or sets the backdrop addresses.
    /// </summary>
    public IReadOnlyList<string> BackdropUrls { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the synopsis.
    /// </summary>
    public string? Plot { get; set; }

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
    /// Gets or sets the TMDB identifier.
    /// </summary>
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the certification, such as <c>PG-13</c>.
    /// </summary>
    public string? Certification { get; set; }

    /// <summary>
    /// Gets or sets a trailer address.
    /// </summary>
    public string? TrailerUrl { get; set; }
}

/// <summary>
/// A movie.
/// </summary>
public class CatalogMovie : CatalogTitle
{
    /// <summary>
    /// Gets or sets the playback address.
    /// </summary>
    public string StreamUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the runtime.
    /// </summary>
    public TimeSpan? Runtime { get; set; }

    /// <summary>
    /// Gets or sets the container extension the provider serves.
    /// </summary>
    public string? Container { get; set; }
}

/// <summary>
/// A series and its episodes.
/// </summary>
public class CatalogSeries : CatalogTitle
{
    /// <summary>
    /// Gets or sets the seasons.
    /// </summary>
    public IReadOnlyList<CatalogSeason> Seasons { get; set; } = Array.Empty<CatalogSeason>();

    /// <summary>
    /// Gets or sets the nominal episode runtime.
    /// </summary>
    public TimeSpan? EpisodeRuntime { get; set; }
}

/// <summary>
/// A season within a series.
/// </summary>
public class CatalogSeason
{
    /// <summary>
    /// Gets or sets the season number. Zero is treated as specials.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets the season name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the season synopsis.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets the poster address.
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Gets or sets the first-air date.
    /// </summary>
    public DateTime? AirDate { get; set; }

    /// <summary>
    /// Gets or sets the episodes.
    /// </summary>
    public IReadOnlyList<CatalogEpisode> Episodes { get; set; } = Array.Empty<CatalogEpisode>();
}

/// <summary>
/// An episode.
/// </summary>
public class CatalogEpisode
{
    /// <summary>
    /// Gets or sets the identifier, unique within the provider.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number within its season.
    /// </summary>
    public int EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the playback address.
    /// </summary>
    public string StreamUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the synopsis.
    /// </summary>
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the still image address.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the runtime.
    /// </summary>
    public TimeSpan? Runtime { get; set; }

    /// <summary>
    /// Gets or sets the air date.
    /// </summary>
    public DateTime? AirDate { get; set; }

    /// <summary>
    /// Gets or sets the community rating out of ten.
    /// </summary>
    public double? Rating { get; set; }

    /// <summary>
    /// Gets or sets the TMDB identifier.
    /// </summary>
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the container extension the provider serves.
    /// </summary>
    public string? Container { get; set; }
}
