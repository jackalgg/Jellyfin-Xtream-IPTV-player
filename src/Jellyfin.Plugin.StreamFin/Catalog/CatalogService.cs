using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.State;
using Jellyfin.Plugin.StreamFin.Xtream;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Catalog;

/// <summary>
/// Fetches provider catalogs, caching the channel list so that Live TV does not re-query the
/// provider on every guide refresh.
/// </summary>
public class CatalogService
{
    /// <summary>
    /// The name of the HTTP client StreamFin issues provider requests through.
    /// </summary>
    public const string HttpClientName = "StreamFin";

    /// <summary>
    /// The name of the HTTP client used to walk a stream's redirect chain.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="HttpClientName"/> because this one must not follow redirects on its
    /// own: the caller needs to see each hop. The catalog client, by contrast, has to follow them,
    /// since playlist addresses routinely bounce through a load balancer before serving the file.
    /// </remarks>
    public const string RedirectProbeHttpClientName = "StreamFinRedirectProbe";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SyncStateStore _stateStore;
    private readonly ILogger<CatalogService> _logger;
    private readonly ConcurrentDictionary<string, CachedChannels> _channelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetchLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="stateStore">The sync state store.</param>
    /// <param name="logger">The logger.</param>
    public CatalogService(IHttpClientFactory httpClientFactory, SyncStateStore stateStore, ILogger<CatalogService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _stateStore = stateStore;
        _logger = logger;
    }

    private static PluginConfiguration Options =>
        StreamFinPlugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Gets the enabled providers.
    /// </summary>
    /// <returns>The providers StreamFin should act on.</returns>
    public static IReadOnlyList<ProviderOptions> GetEnabledProviders()
        => (StreamFinPlugin.Instance?.Configuration.Providers ?? Array.Empty<ProviderOptions>())
            .Where(p => p.Enabled)
            .ToArray();

    /// <summary>
    /// Gets a provider's live channels, reusing a cached list when one is still fresh.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="useCache">Whether a cached list may be returned.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The channels.</returns>
    public async Task<IReadOnlyList<CatalogChannel>> GetChannelsAsync(
        ProviderOptions provider,
        bool useCache,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var ttl = TimeSpan.FromMinutes(Math.Clamp(Options.CacheMinutes, 1, 24 * 60));

        if (useCache
            && _channelCache.TryGetValue(provider.Id, out var cached)
            && DateTime.UtcNow - cached.FetchedAtUtc < ttl)
        {
            return cached.Channels;
        }

        // One fetch at a time per provider: a guide refresh and a library sync starting together
        // would otherwise both pull the full channel list.
        var gate = _fetchLocks.GetOrAdd(provider.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (useCache
                && _channelCache.TryGetValue(provider.Id, out cached)
                && DateTime.UtcNow - cached.FetchedAtUtc < ttl)
            {
                return cached.Channels;
            }

            IReadOnlyList<CatalogChannel> channels;
            try
            {
                if (provider.Kind == ProviderKind.Xtream)
                {
                    channels = await CreateXtreamSource().GetChannelsAsync(provider, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var source = CreateM3uSource();
                    var playlist = await source.FetchPlaylistAsync(provider, cancellationToken).ConfigureAwait(false);
                    channels = source.BuildCatalog(provider, playlist).Channels;
                }
            }
            catch (Exception ex) when (ex is XtreamApiException or HttpRequestException)
            {
                // Serving a stale list beats emptying the user's guide because the panel blipped.
                if (_channelCache.TryGetValue(provider.Id, out var stale))
                {
                    _logger.LogWarning(
                        "{Provider}: channel refresh failed ({Reason}); keeping the {Count} channels from {Age} ago",
                        provider.Name,
                        ex.Message,
                        stale.Channels.Count,
                        DateTime.UtcNow - stale.FetchedAtUtc);
                    return stale.Channels;
                }

                throw;
            }

            _channelCache[provider.Id] = new CachedChannels(channels, DateTime.UtcNow);
            return channels;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Fetches everything a provider offers.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="progress">Receives a 0-1 completion fraction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The catalog.</returns>
    public async Task<ProviderCatalog> GetFullCatalogAsync(
        ProviderOptions provider,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (provider.Kind == ProviderKind.M3u)
        {
            var source = CreateM3uSource();
            var playlist = await source.FetchPlaylistAsync(provider, cancellationToken).ConfigureAwait(false);
            progress?.Report(1d);

            var catalog = source.BuildCatalog(provider, playlist);
            _channelCache[provider.Id] = new CachedChannels(catalog.Channels, DateTime.UtcNow);
            return catalog;
        }

        return await GetXtreamCatalogAsync(provider, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies a provider's settings and summarizes what it offers.
    /// </summary>
    /// <param name="provider">The provider to test.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A description of what was found.</returns>
    public async Task<ProviderProbeResult> ProbeAsync(ProviderOptions provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (provider.Kind == ProviderKind.M3u)
        {
            var source = CreateM3uSource();
            var playlist = await source.FetchPlaylistAsync(provider, cancellationToken).ConfigureAwait(false);
            var catalog = source.BuildCatalog(provider, playlist);

            return new ProviderProbeResult
            {
                Succeeded = true,
                ChannelCount = catalog.Channels.Count,
                MovieCount = catalog.Movies.Count,
                SeriesCount = catalog.Series.Count,
                EpisodeCount = catalog.Series.Sum(s => s.Seasons.Sum(season => season.Episodes.Count)),
                DiscoveredEpgUrl = playlist.EpgUrl,
                Message = $"Loaded {playlist.Entries.Count:N0} playlist entries."
            };
        }

        var client = CreateXtreamClient();
        var account = await client.GetAccountAsync(provider, cancellationToken).ConfigureAwait(false);

        var live = await client.GetLiveStreamsAsync(provider, null, cancellationToken).ConfigureAwait(false);
        var vod = await client.GetVodStreamsAsync(provider, null, cancellationToken).ConfigureAwait(false);
        var series = await client.GetSeriesAsync(provider, null, cancellationToken).ConfigureAwait(false);

        var info = account.UserInfo!;
        var expiry = info.ExpiresAtUtc is null
            ? "never expires"
            : $"expires {info.ExpiresAtUtc:yyyy-MM-dd}";

        return new ProviderProbeResult
        {
            Succeeded = true,
            ChannelCount = live.Count,
            MovieCount = vod.Count,
            SeriesCount = series.Count,
            AccountStatus = info.Status,
            ExpiresAtUtc = info.ExpiresAtUtc,
            MaxConnections = info.MaxConnections,
            ActiveConnections = info.ActiveConnections,
            IsTrial = info.IsTrial,
            Message = $"Connected as {info.Username} ({expiry}), {info.ActiveConnections ?? 0} of {info.MaxConnections ?? 0} connections in use."
        };
    }

    /// <summary>
    /// Lists a provider's categories for one content type.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="contentType">One of <c>live</c>, <c>movies</c>, or <c>series</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The categories, with the number of titles in each.</returns>
    public async Task<IReadOnlyList<CategorySummary>> GetCategoriesAsync(
        ProviderOptions provider,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (provider.Kind == ProviderKind.M3u)
        {
            var source = CreateM3uSource();
            var playlist = await source.FetchPlaylistAsync(provider, cancellationToken).ConfigureAwait(false);

            var wanted = contentType switch
            {
                "movies" => M3u.M3uEntryKind.Movie,
                "series" => M3u.M3uEntryKind.Episode,
                _ => M3u.M3uEntryKind.Live
            };

            return playlist.Entries
                .Where(e => e.Kind == wanted)
                .GroupBy(e => e.GroupTitle ?? "Ungrouped", StringComparer.OrdinalIgnoreCase)
                .Select(g => new CategorySummary { Id = g.Key, Name = g.Key, ItemCount = g.Count() })
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var client = CreateXtreamClient();

        // Category names only. Counting titles means downloading the entire movie or series
        // catalog, which is the stall an IPTV player never pays.
        var categories = contentType switch
        {
            "movies" => await client.GetVodCategoriesAsync(provider, cancellationToken).ConfigureAwait(false),
            "series" => await client.GetSeriesCategoriesAsync(provider, cancellationToken).ConfigureAwait(false),
            _ => await client.GetLiveCategoriesAsync(provider, cancellationToken).ConfigureAwait(false)
        };

        return categories
            .Where(c => c.Id is not null)
            .Select(c => new CategorySummary
            {
                Id = c.Id!,
                Name = c.Name ?? c.Id!,
                ItemCount = 0
            })
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Works out where a provider's XMLTV guide lives.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The guide address, or <c>null</c> when the provider has none.</returns>
    public static string? ResolveEpgUrl(ProviderOptions provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!string.IsNullOrWhiteSpace(provider.EpgUrl))
        {
            return provider.EpgUrl.Trim();
        }

        if (provider.Kind == ProviderKind.Xtream)
        {
            var urls = new XtreamUrlBuilder(provider);
            return urls.IsConfigured ? urls.Xmltv() : null;
        }

        return null;
    }

    /// <summary>
    /// Drops the cached channel list for a provider, or for all of them.
    /// </summary>
    /// <param name="providerId">The provider to forget, or <c>null</c> for all of them.</param>
    public void InvalidateChannelCache(string? providerId = null)
    {
        if (providerId is null)
        {
            _channelCache.Clear();
        }
        else
        {
            _channelCache.TryRemove(providerId, out _);
        }
    }

    /// <summary>
    /// Creates an HTTP client configured for provider requests.
    /// </summary>
    /// <returns>The client.</returns>
    public HttpClient CreateHttpClient() => _httpClientFactory.CreateClient(HttpClientName);

    /// <summary>
    /// Creates an HTTP client that reports redirects instead of following them.
    /// </summary>
    /// <returns>The client.</returns>
    public HttpClient CreateRedirectProbeClient() => _httpClientFactory.CreateClient(RedirectProbeHttpClientName);

    private async Task<ProviderCatalog> GetXtreamCatalogAsync(
        ProviderOptions provider,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var options = Options;
        var source = CreateXtreamSource();

        var catalog = new ProviderCatalog
        {
            ProviderId = provider.Id,
            ProviderName = provider.Name
        };

        // Weight the phases by how long they actually take: series needs one request per show.
        var channelsWeight = provider.EnableLiveTv ? 0.1d : 0d;
        var moviesWeight = provider.EnableMovies ? 0.3d : 0d;
        var seriesWeight = provider.EnableSeries ? 0.6d : 0d;
        var total = channelsWeight + moviesWeight + seriesWeight;
        if (total <= 0)
        {
            progress?.Report(1d);
            return catalog;
        }

        var completedWeight = 0d;

        if (provider.EnableLiveTv)
        {
            catalog.Channels = await GetChannelsAsync(provider, useCache: false, cancellationToken).ConfigureAwait(false);
            completedWeight += channelsWeight;
            progress?.Report(completedWeight / total);
        }

        if (provider.EnableMovies)
        {
            var detailCache = await _stateStore.LoadMovieDetailCacheAsync(provider.Id, cancellationToken).ConfigureAwait(false);
            var before = detailCache.Count;

            var phase = new Progress<double>(fraction =>
                progress?.Report((completedWeight + (moviesWeight * fraction)) / total));

            catalog.Movies = await source
                .GetMoviesAsync(provider, options, detailCache, phase, cancellationToken)
                .ConfigureAwait(false);

            if (detailCache.Count != before)
            {
                await _stateStore.SaveMovieDetailCacheAsync(provider.Id, detailCache, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "{Provider}: enriched {New} more movies from the panel ({Total} cached in total)",
                    provider.Name,
                    detailCache.Count - before,
                    detailCache.Count);
            }

            completedWeight += moviesWeight;
            progress?.Report(completedWeight / total);
        }

        if (provider.EnableSeries)
        {
            var phase = new Progress<double>(fraction =>
                progress?.Report((completedWeight + (seriesWeight * fraction)) / total));

            catalog.Series = await source.GetSeriesAsync(provider, options, phase, cancellationToken).ConfigureAwait(false);
            completedWeight += seriesWeight;
        }

        progress?.Report(1d);
        return catalog;
    }

    private XtreamClient CreateXtreamClient() => new(CreateHttpClient(), _logger);

    private XtreamCatalogSource CreateXtreamSource() => new(CreateXtreamClient(), _logger);

    private M3uCatalogSource CreateM3uSource() => new(CreateHttpClient(), _logger);

    private sealed record CachedChannels(IReadOnlyList<CatalogChannel> Channels, DateTime FetchedAtUtc);
}

/// <summary>
/// The outcome of testing a provider's settings.
/// </summary>
public class ProviderProbeResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the provider answered.
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Gets or sets a human-readable summary.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of live channels offered.
    /// </summary>
    public int ChannelCount { get; set; }

    /// <summary>
    /// Gets or sets the number of movies offered.
    /// </summary>
    public int MovieCount { get; set; }

    /// <summary>
    /// Gets or sets the number of series offered.
    /// </summary>
    public int SeriesCount { get; set; }

    /// <summary>
    /// Gets or sets the number of episodes offered, when it is known without extra requests.
    /// </summary>
    public int EpisodeCount { get; set; }

    /// <summary>
    /// Gets or sets the account status the panel reported.
    /// </summary>
    public string? AccountStatus { get; set; }

    /// <summary>
    /// Gets or sets when the subscription lapses.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of simultaneous streams allowed.
    /// </summary>
    public int? MaxConnections { get; set; }

    /// <summary>
    /// Gets or sets the number of streams currently open.
    /// </summary>
    public int? ActiveConnections { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a trial line.
    /// </summary>
    public bool IsTrial { get; set; }

    /// <summary>
    /// Gets or sets a guide address discovered during the probe.
    /// </summary>
    public string? DiscoveredEpgUrl { get; set; }
}

/// <summary>
/// One of a provider's categories.
/// </summary>
public class CategorySummary
{
    /// <summary>
    /// Gets or sets the category identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the category display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many titles the category holds.
    /// </summary>
    public int ItemCount { get; set; }
}
