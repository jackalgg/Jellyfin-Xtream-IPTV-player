using System;
using System.Globalization;

namespace Jellyfin.Plugin.StreamFin.Configuration;

/// <summary>
/// One configured IPTV subscription.
/// </summary>
public class ProviderOptions
{
    /// <summary>
    /// Gets or sets the stable identifier for this provider.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets or sets the display name shown in Jellyfin and used to name the generated libraries.
    /// </summary>
    public string Name { get; set; } = "IPTV";

    /// <summary>
    /// Gets or sets a value indicating whether this provider is used.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the source type.
    /// </summary>
    public ProviderKind Kind { get; set; } = ProviderKind.Xtream;

    /// <summary>
    /// Gets or sets the Xtream panel base address, for example <c>http://example.com:8080</c>.
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Xtream username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Xtream password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the playlist address for <see cref="ProviderKind.M3u"/> providers.
    /// </summary>
    public string PlaylistUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an XMLTV guide address. Optional for Xtream providers, which fall back to the
    /// panel's own <c>xmltv.php</c> endpoint.
    /// </summary>
    public string EpgUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the container used for live streams.
    /// </summary>
    public StreamFormat LiveStreamFormat { get; set; } = StreamFormat.MpegTs;

    /// <summary>
    /// Gets or sets the User-Agent sent to the provider. Many panels reject the default .NET agent.
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Referer sent to the provider. Empty means "use the panel base address".
    /// </summary>
    public string Referer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of simultaneous live streams allowed, or 0 to let the provider decide.
    /// </summary>
    public int MaxConcurrentStreams { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether live channels are imported into Live TV.
    /// </summary>
    public bool EnableLiveTv { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether video on demand is imported as a movie library.
    /// </summary>
    public bool EnableMovies { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether series are imported as a TV show library.
    /// </summary>
    public bool EnableSeries { get; set; } = true;

    /// <summary>
    /// Gets or sets the live channel filter.
    /// </summary>
    public CategoryFilter LiveFilter { get; set; } = new CategoryFilter();

    /// <summary>
    /// Gets or sets the movie filter.
    /// </summary>
    public CategoryFilter MovieFilter { get; set; } = new CategoryFilter();

    /// <summary>
    /// Gets or sets the series filter.
    /// </summary>
    public CategoryFilter SeriesFilter { get; set; } = new CategoryFilter();

    /// <summary>
    /// Gets or sets a value indicating whether the channel number from the provider is used.
    /// When disabled, channels are numbered sequentially in playlist order.
    /// </summary>
    public bool UseProviderChannelNumbers { get; set; } = true;

    /// <summary>
    /// Gets or sets a prefix prepended to generated channel numbers, which keeps several providers
    /// from colliding in the guide. Leave empty to disable.
    /// </summary>
    public string ChannelNumberPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets the server address with any trailing slash and <c>player_api.php</c> suffix removed.
    /// </summary>
    /// <returns>A normalized base address, or an empty string when none is configured.</returns>
    public string GetNormalizedServerUrl() => UrlNormalizer.NormalizeBaseUrl(ServerUrl);
}
