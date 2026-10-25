using System.Text.Json.Serialization;
using Jellyfin.Plugin.StreamFin.Xtream.Json;

namespace Jellyfin.Plugin.StreamFin.Xtream.Models;

/// <summary>
/// A live channel as returned by <c>get_live_streams</c>.
/// </summary>
public class XtreamLiveStream
{
    /// <summary>
    /// Gets or sets the channel number assigned by the panel.
    /// </summary>
    [JsonPropertyName("num")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? Number { get; set; }

    /// <summary>
    /// Gets or sets the channel name.
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
    /// Gets or sets the channel logo address.
    /// </summary>
    [JsonPropertyName("stream_icon")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the XMLTV channel identifier this channel's guide data is published under.
    /// </summary>
    [JsonPropertyName("epg_channel_id")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? EpgChannelId { get; set; }

    /// <summary>
    /// Gets or sets the owning category identifier.
    /// </summary>
    [JsonPropertyName("category_id")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? CategoryId { get; set; }

    /// <summary>
    /// Gets or sets a provider-supplied absolute stream URL, used in preference to the
    /// conventional <c>/live/user/pass/id.ext</c> form when present.
    /// </summary>
    [JsonPropertyName("direct_source")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? DirectSource { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether catch-up recording is available.
    /// </summary>
    [JsonPropertyName("tv_archive")]
    [JsonConverter(typeof(TolerantBooleanConverter))]
    public bool HasArchive { get; set; }

    /// <summary>
    /// Gets or sets how many days of catch-up the panel retains.
    /// </summary>
    [JsonPropertyName("tv_archive_duration")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? ArchiveDays { get; set; }

    /// <summary>
    /// Gets or sets the stream kind, normally <c>live</c>.
    /// </summary>
    [JsonPropertyName("stream_type")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? StreamType { get; set; }
}
