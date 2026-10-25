using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.StreamFin.Xtream.Json;

namespace Jellyfin.Plugin.StreamFin.Xtream.Models;

/// <summary>
/// A series as returned by <c>get_series</c>.
/// </summary>
public class XtreamSeries
{
    /// <summary>
    /// Gets or sets the series identifier, used to request episode listings.
    /// </summary>
    [JsonPropertyName("series_id")]
    [JsonConverter(typeof(TolerantInt64Converter))]
    public long? SeriesId { get; set; }

    /// <summary>
    /// Gets or sets the series title.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the poster address.
    /// </summary>
    [JsonPropertyName("cover")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Cover { get; set; }

    /// <summary>
    /// Gets or sets the synopsis.
    /// </summary>
    [JsonPropertyName("plot")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets a comma separated cast list.
    /// </summary>
    [JsonPropertyName("cast")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Cast { get; set; }

    /// <summary>
    /// Gets or sets a comma separated director list.
    /// </summary>
    [JsonPropertyName("director")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Director { get; set; }

    /// <summary>
    /// Gets or sets a comma or slash separated genre list.
    /// </summary>
    [JsonPropertyName("genre")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Genre { get; set; }

    /// <summary>
    /// Gets or sets the first-air date.
    /// </summary>
    [JsonPropertyName("releaseDate")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the first-air date under the snake_case key some panels use.
    /// </summary>
    [JsonPropertyName("release_date")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? ReleaseDateAlternate { get; set; }

    /// <summary>
    /// Gets or sets the community rating out of ten.
    /// </summary>
    [JsonPropertyName("rating")]
    [JsonConverter(typeof(TolerantDoubleConverter))]
    public double? Rating { get; set; }

    /// <summary>
    /// Gets or sets the owning category identifier.
    /// </summary>
    [JsonPropertyName("category_id")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? CategoryId { get; set; }

    /// <summary>
    /// Gets or sets the backdrop addresses.
    /// </summary>
    [JsonPropertyName("backdrop_path")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public IReadOnlyList<string> BackdropPaths { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the nominal episode runtime, in minutes.
    /// </summary>
    [JsonPropertyName("episode_run_time")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? EpisodeRuntimeMinutes { get; set; }

    /// <summary>
    /// Gets or sets the last time the panel touched this series, as a Unix timestamp.
    /// </summary>
    [JsonPropertyName("last_modified")]
    [JsonConverter(typeof(TolerantInt64Converter))]
    public long? LastModifiedUnix { get; set; }

    /// <summary>
    /// Gets or sets the TMDB identifier.
    /// </summary>
    [JsonPropertyName("tmdb")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets the first-air date from whichever key the panel populated.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveReleaseDate => string.IsNullOrWhiteSpace(ReleaseDate) ? ReleaseDateAlternate : ReleaseDate;
}

/// <summary>
/// The <c>get_series_info</c> response.
/// </summary>
public class XtreamSeriesDetail
{
    /// <summary>
    /// Gets or sets the series level metadata.
    /// </summary>
    [JsonPropertyName("info")]
    public XtreamSeries? Info { get; set; }

    /// <summary>
    /// Gets or sets the season level metadata.
    /// </summary>
    [JsonPropertyName("seasons")]
    [JsonConverter(typeof(LooseListConverter<XtreamSeason>))]
    public IReadOnlyList<XtreamSeason> Seasons { get; set; } = Array.Empty<XtreamSeason>();

    /// <summary>
    /// Gets or sets the episodes, keyed by season number.
    /// </summary>
    [JsonPropertyName("episodes")]
    [JsonConverter(typeof(LooseDictionaryConverter<IReadOnlyList<XtreamEpisode>>))]
    public IReadOnlyDictionary<string, IReadOnlyList<XtreamEpisode>> Episodes { get; set; }
        = new Dictionary<string, IReadOnlyList<XtreamEpisode>>();
}

/// <summary>
/// Season level metadata.
/// </summary>
public class XtreamSeason
{
    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("season_number")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the season name.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the season synopsis.
    /// </summary>
    [JsonPropertyName("overview")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets the first-air date.
    /// </summary>
    [JsonPropertyName("air_date")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? AirDate { get; set; }

    /// <summary>
    /// Gets or sets the season poster address.
    /// </summary>
    [JsonPropertyName("cover")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Cover { get; set; }

    /// <summary>
    /// Gets or sets the number of episodes the panel reports for this season.
    /// </summary>
    [JsonPropertyName("episode_count")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? EpisodeCount { get; set; }
}

/// <summary>
/// One episode within a series.
/// </summary>
public class XtreamEpisode
{
    /// <summary>
    /// Gets or sets the episode's stream identifier, used to build the playback URL.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the episode number within its season.
    /// </summary>
    [JsonPropertyName("episode_num")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode title.
    /// </summary>
    [JsonPropertyName("title")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the file extension the panel serves this episode as.
    /// </summary>
    [JsonPropertyName("container_extension")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? ContainerExtension { get; set; }

    /// <summary>
    /// Gets or sets the season number. Panels disagree on whether this lives here or only in the
    /// episode map key.
    /// </summary>
    [JsonPropertyName("season")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the date the episode was added, as a Unix timestamp.
    /// </summary>
    [JsonPropertyName("added")]
    [JsonConverter(typeof(TolerantInt64Converter))]
    public long? AddedUnix { get; set; }

    /// <summary>
    /// Gets or sets a provider-supplied absolute stream URL.
    /// </summary>
    [JsonPropertyName("direct_source")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? DirectSource { get; set; }

    /// <summary>
    /// Gets or sets the episode's descriptive metadata.
    /// </summary>
    [JsonPropertyName("info")]
    public XtreamEpisodeInfo? Info { get; set; }
}

/// <summary>
/// Descriptive metadata for an episode.
/// </summary>
public class XtreamEpisodeInfo
{
    /// <summary>
    /// Gets or sets the still image address.
    /// </summary>
    [JsonPropertyName("movie_image")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Image { get; set; }

    /// <summary>
    /// Gets or sets the synopsis.
    /// </summary>
    [JsonPropertyName("plot")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the synopsis under its alternate key.
    /// </summary>
    [JsonPropertyName("overview")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets the runtime in seconds.
    /// </summary>
    [JsonPropertyName("duration_secs")]
    [JsonConverter(typeof(TolerantInt64Converter))]
    public long? DurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets the community rating out of ten.
    /// </summary>
    [JsonPropertyName("rating")]
    [JsonConverter(typeof(TolerantDoubleConverter))]
    public double? Rating { get; set; }

    /// <summary>
    /// Gets or sets the air date.
    /// </summary>
    [JsonPropertyName("release_date")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the air date under its alternate key.
    /// </summary>
    [JsonPropertyName("releasedate")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? ReleaseDateAlternate { get; set; }

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("season")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the TMDB identifier for this episode.
    /// </summary>
    [JsonPropertyName("tmdb_id")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets the synopsis from whichever key the panel populated.
    /// </summary>
    [JsonIgnore]
    public string? EffectivePlot => string.IsNullOrWhiteSpace(Plot) ? Overview : Plot;

    /// <summary>
    /// Gets the air date from whichever key the panel populated.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveReleaseDate => string.IsNullOrWhiteSpace(ReleaseDate) ? ReleaseDateAlternate : ReleaseDate;
}
