using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.StreamFin.Xtream.Json;

namespace Jellyfin.Plugin.StreamFin.Xtream.Models;

/// <summary>
/// A movie as returned by <c>get_vod_streams</c>.
/// </summary>
public class XtreamVodStream
{
    /// <summary>
    /// Gets or sets the listing position.
    /// </summary>
    [JsonPropertyName("num")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? Number { get; set; }

    /// <summary>
    /// Gets or sets the movie title.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the stream identifier used to build the playback URL.
    /// </summary>
    [JsonPropertyName("stream_id")]
    [JsonConverter(typeof(TolerantInt64Converter))]
    public long? StreamId { get; set; }

    /// <summary>
    /// Gets or sets the poster address.
    /// </summary>
    [JsonPropertyName("stream_icon")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the file extension the panel serves this title as, such as <c>mkv</c> or <c>mp4</c>.
    /// </summary>
    [JsonPropertyName("container_extension")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? ContainerExtension { get; set; }

    /// <summary>
    /// Gets or sets the owning category identifier.
    /// </summary>
    [JsonPropertyName("category_id")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? CategoryId { get; set; }

    /// <summary>
    /// Gets or sets the community rating out of ten.
    /// </summary>
    [JsonPropertyName("rating")]
    [JsonConverter(typeof(TolerantDoubleConverter))]
    public double? Rating { get; set; }

    /// <summary>
    /// Gets or sets the TMDB identifier, when the panel has one.
    /// </summary>
    [JsonPropertyName("tmdb")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the date the title was added, as a Unix timestamp.
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
}

/// <summary>
/// The <c>get_vod_info</c> response.
/// </summary>
public class XtreamVodDetail
{
    /// <summary>
    /// Gets or sets the descriptive metadata.
    /// </summary>
    [JsonPropertyName("info")]
    public XtreamVodInfo? Info { get; set; }

    /// <summary>
    /// Gets or sets the playback metadata.
    /// </summary>
    [JsonPropertyName("movie_data")]
    public XtreamVodStream? MovieData { get; set; }
}

/// <summary>
/// Descriptive metadata for a movie.
/// </summary>
public class XtreamVodInfo
{
    /// <summary>
    /// Gets or sets the title as recorded in the panel's metadata, which is often cleaner than the
    /// stream name.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the original-language title.
    /// </summary>
    [JsonPropertyName("o_name")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? OriginalName { get; set; }

    /// <summary>
    /// Gets or sets the poster address.
    /// </summary>
    [JsonPropertyName("movie_image")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Gets or sets the cover address, an alternate spelling used by some panels.
    /// </summary>
    [JsonPropertyName("cover_big")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? CoverUrl { get; set; }

    /// <summary>
    /// Gets or sets the backdrop addresses.
    /// </summary>
    [JsonPropertyName("backdrop_path")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public IReadOnlyList<string> BackdropPaths { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the TMDB identifier.
    /// </summary>
    [JsonPropertyName("tmdb_id")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the synopsis.
    /// </summary>
    [JsonPropertyName("plot")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the synopsis under its alternate key.
    /// </summary>
    [JsonPropertyName("description")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a comma separated cast list.
    /// </summary>
    [JsonPropertyName("cast")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Cast { get; set; }

    /// <summary>
    /// Gets or sets a comma separated cast list under its alternate key.
    /// </summary>
    [JsonPropertyName("actors")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Actors { get; set; }

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
    /// Gets or sets the release date.
    /// </summary>
    [JsonPropertyName("releasedate")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the release date under its alternate key.
    /// </summary>
    [JsonPropertyName("release_date")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? ReleaseDateAlternate { get; set; }

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
    /// Gets or sets a YouTube trailer identifier or URL.
    /// </summary>
    [JsonPropertyName("youtube_trailer")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? YoutubeTrailer { get; set; }

    /// <summary>
    /// Gets or sets the MPAA or equivalent certification.
    /// </summary>
    [JsonPropertyName("mpaa_rating")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? MpaaRating { get; set; }

    /// <summary>
    /// Gets the synopsis from whichever key the panel populated.
    /// </summary>
    [JsonIgnore]
    public string? EffectivePlot => Coalesce(Plot, Description);

    /// <summary>
    /// Gets the cast list from whichever key the panel populated.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveCast => Coalesce(Cast, Actors);

    /// <summary>
    /// Gets the release date from whichever key the panel populated.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveReleaseDate => Coalesce(ReleaseDate, ReleaseDateAlternate);

    /// <summary>
    /// Gets the poster from whichever key the panel populated.
    /// </summary>
    [JsonIgnore]
    public string? EffectivePoster => Coalesce(PosterUrl, CoverUrl);

    private static string? Coalesce(string? first, string? second)
        => string.IsNullOrWhiteSpace(first) ? (string.IsNullOrWhiteSpace(second) ? null : second) : first;
}
