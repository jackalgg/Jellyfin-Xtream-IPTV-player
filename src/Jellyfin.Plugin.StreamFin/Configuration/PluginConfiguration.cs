using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.StreamFin.Configuration;

/// <summary>
/// StreamFin's persisted settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the configured IPTV subscriptions.
    /// </summary>
    /// <remarks>
    /// This has to be an array. Jellyfin persists plugin settings with <c>XmlSerializer</c>, which
    /// refuses interface types such as <c>IReadOnlyList&lt;T&gt;</c> and answers the settings page
    /// with a save error.
    /// </remarks>
    public ProviderOptions[] Providers { get; set; } = Array.Empty<ProviderOptions>();

    /// <summary>
    /// Gets or sets the directory the generated <c>.strm</c> and <c>.nfo</c> files are written to.
    /// Empty means "use <c>streamfin/media</c> inside the plugin's data folder".
    /// </summary>
    public string LibraryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether StreamFin creates and wires up the Jellyfin
    /// libraries, tuner, and guide provider itself.
    /// </summary>
    public bool AutoProvision { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the whole catalog is copied into Jellyfin libraries.
    /// </summary>
    /// <remarks>
    /// Off by default. Copying every film and episode into a library is what makes setup crawl and
    /// stall. The Watch page plays straight from the provider, the way a normal IPTV player does,
    /// and only asks the panel for the category you open.
    /// </remarks>
    public bool ImportLibraries { get; set; }

    /// <summary>
    /// Gets or sets the interval in hours between automatic catalog syncs.
    /// </summary>
    public int SyncIntervalHours { get; set; } = 12;

    /// <summary>
    /// Gets or sets a value indicating whether <c>.nfo</c> sidecars carrying the provider's own
    /// titles, plots, artwork, and TMDB identifiers are written next to each <c>.strm</c>.
    /// </summary>
    public bool WriteNfoMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the generated libraries may also fetch metadata from
    /// TMDB and friends. Provider NFO data wins where both exist.
    /// </summary>
    public bool EnableInternetMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether titles that disappear from the provider are removed
    /// from the generated library.
    /// </summary>
    public bool PruneRemovedItems { get; set; } = true;

    /// <summary>
    /// Gets or sets how long a fetched channel list or guide stays usable before it is refetched.
    /// </summary>
    public int CacheMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets the number of days of guide data requested from the provider.
    /// </summary>
    public int GuideDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets a value indicating whether movie and series <c>.strm</c> files point at
    /// StreamFin's own play endpoint instead of the panel URL.
    /// </summary>
    /// <remarks>
    /// On by default because that is the difference between a regular IPTV player and the plugins
    /// that fail to load films and shows: ffmpeg talks to Jellyfin, and StreamFin talks to the
    /// panel with the right User-Agent, Referer, and a walk of container extensions.
    /// </remarks>
    public bool ProxyPlayback { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether StreamFin advertises direct play for on-demand
    /// titles. Turn this off to force transcoding when a client cannot open the container.
    /// </summary>
    public bool PreferDirectPlay { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether live stream URLs are resolved through their
    /// redirects before being handed to the transcoder.
    /// </summary>
    /// <remarks>
    /// Most panels answer <c>/live/...</c> with a redirect to a load balanced edge node. Resolving
    /// it here means ffmpeg connects straight to the node that will actually serve the stream,
    /// which is the difference between a channel that starts in a second and one that times out.
    /// </remarks>
    public bool ResolveStreamRedirects { get; set; } = true;

    /// <summary>
    /// Gets or sets how long to wait on the provider's API before giving up, in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the number of series detail requests issued at once during a sync.
    /// </summary>
    public int SeriesConcurrency { get; set; } = 4;

    /// <summary>
    /// Gets or sets a value indicating whether per-movie detail (plot, cast, TMDB identifier) is
    /// fetched from the panel.
    /// </summary>
    public bool FetchMovieDetails { get; set; } = true;

    /// <summary>
    /// Gets or sets how many movie detail requests one sync may make.
    /// </summary>
    /// <remarks>
    /// The movie list already carries titles, posters, and ratings, so detail is an enrichment
    /// pass. A large catalog would need tens of thousands of requests to enrich in one go, which
    /// no panel tolerates, so each run spends a budget on the titles it has not enriched yet and
    /// caches the result. Repeated syncs fill the library in without ever hammering the provider.
    /// </remarks>
    public int MovieDetailBudget { get; set; } = 400;

    /// <summary>
    /// Gets or sets a value indicating whether an untrusted TLS certificate on the provider's
    /// server is accepted.
    /// </summary>
    /// <remarks>
    /// Off by default, because turning it on means an attacker who can intercept the connection can
    /// read the subscription credentials and substitute the video. Resellers do quite often front a
    /// panel with a self-signed or expired certificate though, and when they do, nothing loads at
    /// all, so the escape hatch exists.
    /// </remarks>
    public bool AllowUntrustedCertificates { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether verbose per-item sync logging is emitted.
    /// </summary>
    public bool VerboseLogging { get; set; }
}
