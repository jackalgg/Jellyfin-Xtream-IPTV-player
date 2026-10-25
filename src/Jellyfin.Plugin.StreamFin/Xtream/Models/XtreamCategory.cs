using System.Text.Json.Serialization;
using Jellyfin.Plugin.StreamFin.Xtream.Json;

namespace Jellyfin.Plugin.StreamFin.Xtream.Models;

/// <summary>
/// A content category as returned by <c>get_live_categories</c>, <c>get_vod_categories</c>, and
/// <c>get_series_categories</c>.
/// </summary>
public class XtreamCategory
{
    /// <summary>
    /// Gets or sets the category identifier.
    /// </summary>
    [JsonPropertyName("category_id")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the category display name.
    /// </summary>
    [JsonPropertyName("category_name")]
    [JsonConverter(typeof(TolerantStringConverter))]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the parent category identifier, or 0 for a top-level category.
    /// </summary>
    [JsonPropertyName("parent_id")]
    [JsonConverter(typeof(TolerantInt32Converter))]
    public int? ParentId { get; set; }
}
