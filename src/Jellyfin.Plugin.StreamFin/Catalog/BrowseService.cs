using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.M3u;
using Jellyfin.Plugin.StreamFin.Playback;
using Jellyfin.Plugin.StreamFin.Xtream;
using Jellyfin.Plugin.StreamFin.Xtream.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Catalog;

/// <summary>
/// Lists one category at a time for the Watch page and builds a play path for the title that was opened.
/// </summary>
/// <remarks>
/// An IPTV player does not copy the catalog. Category names come from the small category endpoints.
/// Opening a category asks the panel for that category only. Some panels ignore <c>category_id</c>
/// and send everything; that full list is cached so the next category is filtered locally.
/// </remarks>
public class BrowseService
{
    private readonly CatalogService _catalog;
    private readonly PlaybackLinkFactory _links;
    private readonly ILogger<BrowseService> _logger;
    private readonly ConcurrentDictionary<string, CacheBox> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowseService"/> class.
    /// </summary>
    /// <param name="catalog">The catalog service, used for its HTTP client.</param>
    /// <param name="links">The playback URL factory.</param>
    /// <param name="logger">The logger.</param>
    public BrowseService(CatalogService catalog, PlaybackLinkFactory links, ILogger<BrowseService> logger)
    {
        _catalog = catalog;
        _links = links;
        _logger = logger;
    }

    /// <summary>
    /// Lists the enabled subscriptions without their credentials.
    /// </summary>
    /// <returns>The subscriptions.</returns>
    public static IReadOnlyList<BrowseProviderInfo> GetProviders()
    {
        var providers = CatalogService.GetEnabledProviders();
        var listed = new List<BrowseProviderInfo>(providers.Count);
        foreach (var provider in providers)
        {
            listed.Add(new BrowseProviderInfo
            {
                Id = provider.Id,
                Name = string.IsNullOrWhiteSpace(provider.Name) ? "IPTV" : provider.Name.Trim(),
                Kind = provider.Kind.ToString(),
                Live = provider.EnableLiveTv,
                Movies = provider.EnableMovies,
                Series = provider.EnableSeries
            });
        }

        listed.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return listed;
    }

    /// <summary>
    /// Lists category names for one content type. Xtream category calls do not download the titles.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="contentType">One of <c>live</c>, <c>movies</c>, or <c>series</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The categories.</returns>
    public async Task<BrowseCategoryList> GetCategoriesAsync(
        string providerId,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var provider = RequireProvider(providerId);
        var content = NormalizeContent(contentType);

        if (provider.Kind == ProviderKind.M3u)
        {
            var playlist = await PlaylistAsync(provider, cancellationToken).ConfigureAwait(false);
            return new BrowseCategoryList
            {
                Kind = provider.Kind.ToString(),
                Categories = GroupCategories(playlist, content)
            };
        }

        var client = CreateXtreamClient();
        var categories = content switch
        {
            "movies" => await client.GetVodCategoriesAsync(provider, cancellationToken).ConfigureAwait(false),
            "series" => await client.GetSeriesCategoriesAsync(provider, cancellationToken).ConfigureAwait(false),
            _ => await client.GetLiveCategoriesAsync(provider, cancellationToken).ConfigureAwait(false)
        };

        var listed = new List<BrowseCategory>(categories.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            if (string.IsNullOrWhiteSpace(category.Id) || !seen.Add(category.Id.Trim()))
            {
                continue;
            }

            var id = category.Id.Trim();
            listed.Add(new BrowseCategory
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(category.Name) ? id : category.Name.Trim()
            });
        }

        listed.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return new BrowseCategoryList { Kind = provider.Kind.ToString(), Categories = listed };
    }

    /// <summary>
    /// Lists one page of a single category.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="contentType">One of <c>live</c>, <c>movies</c>, or <c>series</c>.</param>
    /// <param name="categoryId">The category to open.</param>
    /// <param name="skip">How many matching items to skip.</param>
    /// <param name="take">How many matching items to return.</param>
    /// <param name="query">An optional name filter applied before paging.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page.</returns>
    public async Task<BrowseItemPage> GetItemsAsync(
        string providerId,
        string? contentType,
        string categoryId,
        int skip,
        int take,
        string? query,
        CancellationToken cancellationToken)
    {
        var provider = RequireProvider(providerId);
        var content = NormalizeContent(contentType);
        take = Math.Clamp(take, 1, 200);
        if (skip < 0)
        {
            skip = 0;
        }

        if (provider.Kind == ProviderKind.M3u)
        {
            var playlist = await PlaylistAsync(provider, cancellationToken).ConfigureAwait(false);
            var wanted = M3uKind(content);
            var matched = new List<M3uEntry>();
            foreach (var entry in playlist.Entries)
            {
                if (entry.Kind == wanted && string.Equals(GroupId(entry), categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(entry);
                }
            }

            var paged = BrowseListing.Page(matched, query, static entry => entry.Name, skip, take);
            var items = new List<BrowseItem>(paged.Items.Count);
            foreach (var entry in paged.Items)
            {
                var item = ToM3uItem(provider, entry, content == "live");
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            return new BrowseItemPage
            {
                Total = paged.Total,
                Skip = skip,
                Take = take,
                Playlist = true,
                Items = items
            };
        }

        return content switch
        {
            "movies" => await PageAsync(
                provider,
                categoryId,
                skip,
                take,
                query,
                static stream => stream.CategoryId,
                (id, token) => CreateXtreamClient().GetVodStreamsAsync(provider, id, token),
                static stream => stream.Name,
                stream => ToMovie(provider, stream),
                cancellationToken).ConfigureAwait(false),
            "series" => await PageAsync(
                provider,
                categoryId,
                skip,
                take,
                query,
                static series => series.CategoryId,
                (id, token) => CreateXtreamClient().GetSeriesAsync(provider, id, token),
                static series => series.Name,
                stream => ToSeries(stream),
                cancellationToken).ConfigureAwait(false),
            _ => await PageAsync(
                provider,
                categoryId,
                skip,
                take,
                query,
                static stream => stream.CategoryId,
                (id, token) => CreateXtreamClient().GetLiveStreamsAsync(provider, id, token),
                static stream => stream.Name,
                stream => ToLive(provider, stream),
                cancellationToken).ConfigureAwait(false)
        };
    }

    /// <summary>
    /// Lists the episodes of one show. The series detail call happens only when the show is opened.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The episodes, in season order.</returns>
    public async Task<IReadOnlyList<BrowseEpisode>> GetEpisodesAsync(
        string providerId,
        string seriesId,
        CancellationToken cancellationToken)
    {
        var provider = RequireProvider(providerId);
        if (provider.Kind != ProviderKind.Xtream)
        {
            throw new XtreamApiException("This playlist plays each episode directly. Open it from the category.");
        }

        if (!long.TryParse(seriesId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            throw new XtreamApiException("That show has no identifier.");
        }

        var key = provider.Id + "\nseries-info\n" + seriesId;
        var detail = await GetOrCreateAsync(
            key,
            async token => await CreateXtreamClient().GetSeriesInfoAsync(provider, id, token).ConfigureAwait(false)
                ?? new XtreamSeriesDetail(),
            cancellationToken).ConfigureAwait(false);

        if (detail?.Episodes is null || detail.Episodes.Count == 0)
        {
            return Array.Empty<BrowseEpisode>();
        }

        var episodes = new List<BrowseEpisode>();
        foreach (var pair in detail.Episodes)
        {
            if (pair.Value is null)
            {
                continue;
            }

            var seasonFromKey = int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

            foreach (var episode in pair.Value)
            {
                var mapped = ToEpisode(provider, episode, seasonFromKey);
                if (mapped is not null)
                {
                    episodes.Add(mapped);
                }
            }
        }

        episodes.Sort(static (a, b) =>
        {
            var season = a.Season.CompareTo(b.Season);
            return season != 0 ? season : a.Number.CompareTo(b.Number);
        });

        return episodes;
    }

    private async Task<BrowseItemPage> PageAsync<T>(
        ProviderOptions provider,
        string categoryId,
        int skip,
        int take,
        string? query,
        Func<T, string?> categoryOf,
        Func<string?, CancellationToken, Task<IReadOnlyList<T>>> fetch,
        Func<T, string?> nameOf,
        Func<T, BrowseItem?> map,
        CancellationToken cancellationToken)
        where T : class
    {
        var slice = await LoadSliceAsync(provider, categoryId, categoryOf, fetch, cancellationToken).ConfigureAwait(false);
        var matched = slice.FullCatalog
            ? BrowseListing.Matching(slice.Items, categoryId, categoryOf)
            : slice.Items;
        var paged = BrowseListing.Page(matched, query, nameOf, skip, take);
        var items = new List<BrowseItem>(paged.Items.Count);
        foreach (var row in paged.Items)
        {
            var item = map(row);
            if (item is not null && (item.Series || !string.IsNullOrWhiteSpace(item.PlayUrl)))
            {
                items.Add(item);
            }
        }

        return new BrowseItemPage
        {
            Total = paged.Total,
            Skip = skip,
            Take = take,
            FullCatalog = slice.FullCatalog,
            Items = items
        };
    }

    private async Task<Slice<T>> LoadSliceAsync<T>(
        ProviderOptions provider,
        string categoryId,
        Func<T, string?> categoryOf,
        Func<string?, CancellationToken, Task<IReadOnlyList<T>>> fetch,
        CancellationToken cancellationToken)
        where T : class
    {
        var scope = provider.Id + "\n" + typeof(T).Name;
        var fullKey = scope + "\n*";
        if (TryGet(fullKey, out IReadOnlyList<T>? full) && full is not null)
        {
            return new Slice<T>(full, true);
        }

        var sliceKey = scope + "\n" + categoryId;
        if (TryGet(sliceKey, out IReadOnlyList<T>? slice) && slice is not null)
        {
            return new Slice<T>(slice, false);
        }

        var gate = _gates.GetOrAdd(scope, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGet(fullKey, out full) && full is not null)
            {
                return new Slice<T>(full, true);
            }

            if (TryGet(sliceKey, out slice) && slice is not null)
            {
                return new Slice<T>(slice, false);
            }

            var fetched = await fetch(categoryId, cancellationToken).ConfigureAwait(false);
            if (BrowseListing.PanelReturnedOtherCategories(fetched, categoryId, categoryOf))
            {
                _logger.LogInformation(
                    "{Provider} sent the whole list for one category; later categories are filtered locally",
                    provider.Name);
                Put(fullKey, fetched);
                return new Slice<T>(fetched, true);
            }

            Put(sliceKey, fetched);
            return new Slice<T>(fetched, false);
        }
        finally
        {
            gate.Release();
        }
    }

    private BrowseItem? ToLive(ProviderOptions provider, XtreamLiveStream stream)
    {
        if (stream.StreamId is not long id && !BrowseListing.IsAbsoluteHttp(stream.DirectSource))
        {
            return null;
        }

        var streamId = stream.StreamId?.ToString(CultureInfo.InvariantCulture) ?? BrowseListing.ShortId(stream.DirectSource ?? stream.Name ?? "live");
        var urls = new XtreamUrlBuilder(provider);
        string? hls = null;
        string? ts = null;
        if (urls.IsConfigured && stream.StreamId is long liveId)
        {
            hls = urls.LiveStream(liveId, StreamFormat.Hls);
            ts = urls.LiveStream(liveId, StreamFormat.MpegTs);
        }

        string? play;
        string? fallback;
        string container;
        if (BrowseListing.IsAbsoluteHttp(stream.DirectSource))
        {
            var direct = stream.DirectSource!.Trim();
            container = BrowseListing.PlaybackContainer(direct, live: true);
            play = Play(provider, PlaybackKind.Url, streamId, direct, container);
            fallback = container == "m3u8"
                ? (ts is null ? null : Play(provider, PlaybackKind.Live, streamId, ts, "ts"))
                : (hls is null ? null : Play(provider, PlaybackKind.Live, streamId, hls, "m3u8"));
        }
        else if (hls is not null)
        {
            container = "m3u8";
            play = Play(provider, PlaybackKind.Live, streamId, hls, "m3u8");
            fallback = ts is null ? null : Play(provider, PlaybackKind.Live, streamId, ts, "ts");
        }
        else
        {
            return null;
        }

        return new BrowseItem
        {
            Id = streamId,
            Name = CleanName(stream.Name, "Channel"),
            Poster = BrowseListing.SafePoster(stream.Icon),
            ChannelNumber = stream.Number,
            Live = true,
            PlayUrl = play,
            FallbackPlayUrl = fallback,
            Container = container
        };
    }

    private BrowseItem? ToMovie(ProviderOptions provider, XtreamVodStream stream)
    {
        if (stream.StreamId is not long id)
        {
            return null;
        }

        var streamId = id.ToString(CultureInfo.InvariantCulture);
        var links = LinksForXtream(
            provider,
            PlaybackKind.Movie,
            streamId,
            stream.DirectSource,
            stream.ContainerExtension,
            extension => new XtreamUrlBuilder(provider).MovieStream(id, extension));
        if (links is null)
        {
            return null;
        }

        var item = new BrowseItem
        {
            Id = streamId,
            Name = CleanName(stream.Name, "Untitled"),
            Poster = BrowseListing.SafePoster(stream.Icon),
            Rating = stream.Rating
        };
        Apply(item, links.Value);
        return item;
    }

    private static BrowseItem? ToSeries(XtreamSeries series)
    {
        if (series.SeriesId is not long id)
        {
            return null;
        }

        return new BrowseItem
        {
            Id = id.ToString(CultureInfo.InvariantCulture),
            Name = CleanName(series.Name, "Untitled"),
            Poster = BrowseListing.SafePoster(series.Cover),
            Plot = string.IsNullOrWhiteSpace(series.Plot) ? null : series.Plot.Trim(),
            Rating = series.Rating,
            Series = true
        };
    }

    private BrowseEpisode? ToEpisode(ProviderOptions provider, XtreamEpisode episode, int seasonFromKey)
    {
        if (string.IsNullOrWhiteSpace(episode.Id))
        {
            return null;
        }

        var episodeId = episode.Id.Trim();
        var links = LinksForXtream(
            provider,
            PlaybackKind.Series,
            episodeId,
            episode.DirectSource,
            episode.ContainerExtension,
            extension => new XtreamUrlBuilder(provider).EpisodeStream(episodeId, extension));
        if (links is null)
        {
            return null;
        }

        var number = episode.EpisodeNumber ?? 0;
        var title = string.IsNullOrWhiteSpace(episode.Title) ? "Episode " + number.ToString(CultureInfo.InvariantCulture) : episode.Title.Trim();
        var signed = links.Value;
        return new BrowseEpisode
        {
            Id = episodeId,
            Title = title,
            Season = episode.Season ?? episode.Info?.Season ?? seasonFromKey,
            Number = number,
            Poster = BrowseListing.SafePoster(episode.Info?.Image),
            Plot = string.IsNullOrWhiteSpace(episode.Info?.EffectivePlot) ? null : episode.Info!.EffectivePlot!.Trim(),
            PlayUrl = signed.PlayUrl,
            FallbackPlayUrl = signed.FallbackPlayUrl,
            FallbackContainer = signed.FallbackContainer,
            RemuxUrl = signed.RemuxUrl,
            Container = signed.Container
        };
    }

    private BrowseItem? ToM3uItem(ProviderOptions provider, M3uEntry entry, bool live)
    {
        if (!BrowseListing.IsAbsoluteHttp(entry.Url))
        {
            return null;
        }

        var url = entry.Url.Trim();
        var id = BrowseListing.ShortId(url);
        int? number = null;
        if (int.TryParse(entry.TvgChannelNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            number = parsed;
        }

        if (!live)
        {
            // A playlist file is already a concrete address. Do not invent a sibling .m3u8 on a CDN.
            var links = LinksForOnDemand(
                provider,
                PlaybackKind.Url,
                id,
                url,
                StreamUrlCandidates.GetExtension(url),
                alternateExtensions: false,
                buildXtream: null);
            if (links is null)
            {
                return null;
            }

            var item = new BrowseItem
            {
                Id = id,
                Name = CleanName(entry.Name, "Untitled"),
                Poster = BrowseListing.SafePoster(entry.TvgLogo),
                ChannelNumber = number
            };
            Apply(item, links.Value);
            return item;
        }

        var container = BrowseListing.PlaybackContainer(url, live: true);
        string? fallback = null;
        if (container == "m3u8")
        {
            // The same address with a transport-stream container is what mpegts.js can play
            // when the playlist is not actually HLS.
            fallback = Play(provider, PlaybackKind.Url, id, url, "ts");
        }

        return new BrowseItem
        {
            Id = id,
            Name = CleanName(entry.Name, "Untitled"),
            Poster = BrowseListing.SafePoster(entry.TvgLogo),
            ChannelNumber = number,
            Live = true,
            Container = container,
            PlayUrl = Play(provider, PlaybackKind.Url, id, url, container),
            FallbackPlayUrl = fallback
        };
    }

    private SignedOnDemand? LinksForXtream(
        ProviderOptions provider,
        PlaybackKind kind,
        string streamId,
        string? directSource,
        string? containerExtension,
        Func<string, string> buildXtream)
    {
        if (BrowseListing.IsAbsoluteHttp(directSource))
        {
            var advertised = StreamUrlCandidates.GetExtension(directSource) ?? containerExtension;
            return LinksForOnDemand(
                provider,
                PlaybackKind.Url,
                streamId,
                directSource,
                advertised,
                alternateExtensions: true,
                buildXtream: null);
        }

        if (!new XtreamUrlBuilder(provider).IsConfigured)
        {
            return null;
        }

        return LinksForOnDemand(
            provider,
            kind,
            streamId,
            directUrl: null,
            containerExtension,
            alternateExtensions: true,
            buildXtream);
    }

    private SignedOnDemand? LinksForOnDemand(
        ProviderOptions provider,
        PlaybackKind kind,
        string streamId,
        string? directUrl,
        string? advertised,
        bool alternateExtensions,
        Func<string, string>? buildXtream)
    {
        var targets = BrowseListing.Targets(advertised, alternateExtensions);

        string? Resolve(string? extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return null;
            }

            if (BrowseListing.IsAbsoluteHttp(directUrl))
            {
                return BrowseListing.DirectAddress(directUrl!, extension);
            }

            return buildXtream?.Invoke(extension);
        }

        string Sign(string upstream, string container, bool remux)
        {
            var path = _links.BuildRelativePlayUrl(provider, kind, streamId, upstream, container);
            return remux ? path + "&b=1&r=1" : path + "&b=1";
        }

        string? play = null;
        var containerName = targets.PlayContainer;
        if (targets.PlayExtension is not null)
        {
            var upstream = Resolve(targets.PlayExtension);
            if (!string.IsNullOrWhiteSpace(upstream))
            {
                play = Sign(upstream, targets.PlayContainer, remux: false);
            }
        }

        string? fallback = null;
        string? fallbackContainer = null;
        if (targets.FallbackExtension is not null)
        {
            var upstream = Resolve(targets.FallbackExtension);
            if (!string.IsNullOrWhiteSpace(upstream))
            {
                fallback = Sign(upstream, targets.FallbackContainer ?? targets.FallbackExtension, remux: false);
                fallbackContainer = targets.FallbackContainer;
            }
        }

        string? remuxUrl = null;
        if (targets.RemuxExtension is not null)
        {
            var upstream = Resolve(targets.RemuxExtension);
            if (!string.IsNullOrWhiteSpace(upstream))
            {
                remuxUrl = Sign(upstream, "ts", remux: true);
            }
        }

        if (string.IsNullOrEmpty(play))
        {
            if (string.IsNullOrEmpty(remuxUrl))
            {
                return null;
            }

            play = remuxUrl;
            containerName = "ts";
            remuxUrl = null;
        }

        return new SignedOnDemand(play, containerName, fallback, fallbackContainer, remuxUrl);
    }

    private static void Apply(BrowseItem item, SignedOnDemand links)
    {
        item.PlayUrl = links.PlayUrl;
        item.Container = links.Container;
        item.FallbackPlayUrl = links.FallbackPlayUrl;
        item.FallbackContainer = links.FallbackContainer;
        item.RemuxUrl = links.RemuxUrl;
    }

    private string Play(ProviderOptions provider, PlaybackKind kind, string streamId, string upstream, string container)
        => _links.BuildRelativePlayUrl(provider, kind, streamId, upstream, container);

    private Task<M3uPlaylist> PlaylistAsync(ProviderOptions provider, CancellationToken cancellationToken)
        => GetOrCreateAsync(
            provider.Id + "\nplaylist",
            token => CreateM3u().FetchPlaylistAsync(provider, token),
            cancellationToken);

    private static List<BrowseCategory> GroupCategories(M3uPlaylist playlist, string content)
    {
        var wanted = M3uKind(content);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listed = new List<BrowseCategory>();
        foreach (var entry in playlist.Entries)
        {
            if (entry.Kind != wanted)
            {
                continue;
            }

            var id = GroupId(entry);
            if (!seen.Add(id))
            {
                continue;
            }

            listed.Add(new BrowseCategory { Id = id, Name = id });
        }

        listed.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return listed;
    }

    private static string GroupId(M3uEntry entry)
        => string.IsNullOrWhiteSpace(entry.GroupTitle) ? "Ungrouped" : entry.GroupTitle.Trim();

    private static M3uEntryKind M3uKind(string content) => content switch
    {
        "movies" => M3uEntryKind.Movie,
        "series" => M3uEntryKind.Episode,
        _ => M3uEntryKind.Live
    };

    private static string NormalizeContent(string? contentType) => (contentType ?? "live").Trim().ToLowerInvariant() switch
    {
        "movies" or "movie" or "vod" => "movies",
        "series" or "shows" or "show" => "series",
        _ => "live"
    };

    private static string CleanName(string? name, string fallback)
        => string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    private static ProviderOptions RequireProvider(string? providerId)
    {
        var provider = StreamFinPlugin.Instance?.FindProvider(providerId);
        if (provider is null || !provider.Enabled)
        {
            throw new XtreamApiException("That subscription is not saved. Save it on the StreamFin page, then open Watch.");
        }

        return provider;
    }

    private XtreamClient CreateXtreamClient() => new(_catalog.CreateHttpClient(), _logger);

    private M3uCatalogSource CreateM3u() => new(_catalog.CreateHttpClient(), _logger);

    private async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
        where T : class
    {
        if (TryGet(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var gate = _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGet(key, out cached) && cached is not null)
            {
                return cached;
            }

            var created = await factory(cancellationToken).ConfigureAwait(false);
            Put(key, created);
            return created;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGet<T>(string key, out T? value)
        where T : class
    {
        value = null;
        if (!_cache.TryGetValue(key, out var box))
        {
            return false;
        }

        if (box.Expires <= DateTimeOffset.UtcNow)
        {
            _cache.TryRemove(key, out _);
            return false;
        }

        if (box.Value is T typed)
        {
            value = typed;
            return true;
        }

        return false;
    }

    private void Put(string key, object value)
    {
        var minutes = Math.Clamp(Options.CacheMinutes, 1, 1440);
        _cache[key] = new CacheBox(value, DateTimeOffset.UtcNow.AddMinutes(minutes));
    }

    private static PluginConfiguration Options
        => StreamFinPlugin.Instance?.Configuration ?? new PluginConfiguration();

    private sealed class CacheBox
    {
        public CacheBox(object value, DateTimeOffset expires)
        {
            Value = value;
            Expires = expires;
        }

        public object Value { get; }

        public DateTimeOffset Expires { get; }
    }

    private readonly record struct Slice<T>(IReadOnlyList<T> Items, bool FullCatalog);

    private readonly record struct SignedOnDemand(
        string PlayUrl,
        string Container,
        string? FallbackPlayUrl,
        string? FallbackContainer,
        string? RemuxUrl);
}
