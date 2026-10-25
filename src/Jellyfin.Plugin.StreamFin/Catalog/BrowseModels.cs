using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.StreamFin.Catalog;

/// <summary>
/// A saved subscription the Watch page can open. Credentials are not included.
/// </summary>
public class BrowseProviderInfo
{
    /// <summary>
    /// Gets or sets the provider identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source kind, <c>Xtream</c> or <c>M3u</c>.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether live channels are offered.
    /// </summary>
    public bool Live { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether films are offered.
    /// </summary>
    public bool Movies { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether shows are offered.
    /// </summary>
    public bool Series { get; set; }
}

/// <summary>
/// One category or playlist group.
/// </summary>
public class BrowseCategory
{
    /// <summary>
    /// Gets or sets the category identifier passed back when the category is opened.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// One channel, film, or show in a category.
/// </summary>
public class BrowseItem
{
    /// <summary>
    /// Gets or sets the identifier within the provider.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an http or https poster address.
    /// </summary>
    public string? Poster { get; set; }

    /// <summary>
    /// Gets or sets a short synopsis, when the category listing included one.
    /// </summary>
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the community rating out of ten.
    /// </summary>
    public double? Rating { get; set; }

    /// <summary>
    /// Gets or sets the channel number, when the provider assigned one.
    /// </summary>
    public int? ChannelNumber { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a live channel.
    /// </summary>
    public bool Live { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether opening this item lists episodes instead of playing.
    /// </summary>
    public bool Series { get; set; }

    /// <summary>
    /// Gets or sets the signed play path. Empty for a show, which has episodes instead.
    /// </summary>
    public string? PlayUrl { get; set; }

    /// <summary>
    /// Gets or sets a second signed play path, used when the first container will not start.
    /// </summary>
    public string? FallbackPlayUrl { get; set; }

    /// <summary>
    /// Gets or sets the container the first play path asks for, such as <c>m3u8</c>, <c>ts</c>, or <c>mp4</c>.
    /// </summary>
    public string Container { get; set; } = string.Empty;
}

/// <summary>
/// One page of a category.
/// </summary>
public class BrowseItemPage
{
    /// <summary>
    /// Gets or sets how many items matched before paging.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Gets or sets how many matching items were skipped.
    /// </summary>
    public int Skip { get; set; }

    /// <summary>
    /// Gets or sets how many items were requested.
    /// </summary>
    public int Take { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the panel sent the whole catalog for one category.
    /// Later categories are then filtered locally.
    /// </summary>
    public bool FullCatalog { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether these items came from an M3U playlist.
    /// </summary>
    public bool Playlist { get; set; }

    /// <summary>
    /// Gets or sets the page of items.
    /// </summary>
    public IReadOnlyList<BrowseItem> Items { get; set; } = Array.Empty<BrowseItem>();
}

/// <summary>
/// One episode of a show.
/// </summary>
public class BrowseEpisode
{
    /// <summary>
    /// Gets or sets the episode's stream identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the episode title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    public int Season { get; set; }

    /// <summary>
    /// Gets or sets the episode number within the season.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets an http or https still image.
    /// </summary>
    public string? Poster { get; set; }

    /// <summary>
    /// Gets or sets the synopsis.
    /// </summary>
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the signed play path.
    /// </summary>
    public string PlayUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the container the play path asks for.
    /// </summary>
    public string Container { get; set; } = "mp4";
}

/// <summary>
/// The categories for one content type.
/// </summary>
public class BrowseCategoryList
{
    /// <summary>
    /// Gets or sets the source kind, <c>Xtream</c> or <c>M3u</c>.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the categories.
    /// </summary>
    public IReadOnlyList<BrowseCategory> Categories { get; set; } = Array.Empty<BrowseCategory>();
}
