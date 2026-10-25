using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.StreamFin.Epg;

/// <summary>
/// A channel declared in an XMLTV guide.
/// </summary>
public class XmltvChannel
{
    /// <summary>
    /// Gets or sets the channel identifier that programmes reference.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the channel number, when the guide declares one.
    /// </summary>
    [JsonPropertyName("number")]
    public string? Number { get; set; }

    /// <summary>
    /// Gets or sets the logo address.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? IconUrl { get; set; }
}

/// <summary>
/// A programme from an XMLTV guide, stored in a compact shape because a week of guide data for a
/// large lineup runs to hundreds of thousands of these.
/// </summary>
public class XmltvProgramme
{
    /// <summary>
    /// Gets or sets the channel identifier.
    /// </summary>
    [JsonPropertyName("c")]
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the start time, as Unix seconds.
    /// </summary>
    [JsonPropertyName("s")]
    public long StartUnix { get; set; }

    /// <summary>
    /// Gets or sets the end time, as Unix seconds.
    /// </summary>
    [JsonPropertyName("e")]
    public long EndUnix { get; set; }

    /// <summary>
    /// Gets or sets the programme title.
    /// </summary>
    [JsonPropertyName("t")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the episode title.
    /// </summary>
    [JsonPropertyName("st")]
    public string? SubTitle { get; set; }

    /// <summary>
    /// Gets or sets the synopsis.
    /// </summary>
    [JsonPropertyName("d")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the categories.
    /// </summary>
    [JsonPropertyName("g")]
    public IReadOnlyList<string>? Categories { get; set; }

    /// <summary>
    /// Gets or sets the image address.
    /// </summary>
    [JsonPropertyName("i")]
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("sn")]
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number.
    /// </summary>
    [JsonPropertyName("en")]
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    [JsonPropertyName("y")]
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the original air date.
    /// </summary>
    [JsonPropertyName("oa")]
    public DateTime? OriginalAirDate { get; set; }

    /// <summary>
    /// Gets or sets the certification.
    /// </summary>
    [JsonPropertyName("r")]
    public string? Rating { get; set; }

    /// <summary>
    /// Gets or sets the community rating out of ten.
    /// </summary>
    [JsonPropertyName("sr")]
    public double? StarRating { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the guide flagged this as new.
    /// </summary>
    [JsonPropertyName("n")]
    public bool IsNew { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the guide flagged this as live.
    /// </summary>
    [JsonPropertyName("l")]
    public bool IsLive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the guide flagged this as a premiere.
    /// </summary>
    [JsonPropertyName("p")]
    public bool IsPremiere { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the guide flagged this as previously shown.
    /// </summary>
    [JsonPropertyName("rp")]
    public bool IsRepeat { get; set; }

    /// <summary>
    /// Gets the start time in UTC.
    /// </summary>
    [JsonIgnore]
    public DateTime StartUtc => DateTimeOffset.FromUnixTimeSeconds(StartUnix).UtcDateTime;

    /// <summary>
    /// Gets the end time in UTC.
    /// </summary>
    [JsonIgnore]
    public DateTime EndUtc => DateTimeOffset.FromUnixTimeSeconds(EndUnix).UtcDateTime;
}
