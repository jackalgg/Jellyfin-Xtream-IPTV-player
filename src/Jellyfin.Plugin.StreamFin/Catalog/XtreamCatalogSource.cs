using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Xtream;
using Jellyfin.Plugin.StreamFin.Xtream.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Catalog;

/// <summary>
/// Builds a <see cref="ProviderCatalog"/> from an Xtream Codes panel.
/// </summary>
public class XtreamCatalogSource
{
    private readonly XtreamClient _client;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="XtreamCatalogSource"/> class.
    /// </summary>
    /// <param name="client">The panel client.</param>
    /// <param name="logger">The logger.</param>
    public XtreamCatalogSource(XtreamClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Fetches the live channel list.
    /// </summary>
    /// <param name="provider">The provider to fetch from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The channels that pass the provider's live filter.</returns>
    public async Task<IReadOnlyList<CatalogChannel>> GetChannelsAsync(ProviderOptions provider, CancellationToken cancellationToken)
    {
        var urls = new XtreamUrlBuilder(provider);
        var categories = await _client.GetLiveCategoriesAsync(provider, cancellationToken).ConfigureAwait(false);
        var categoryNames = categories
            .Where(c => c.Id is not null)
            .ToDictionary(c => c.Id!, c => c.Name ?? c.Id!, StringComparer.OrdinalIgnoreCase);

        var streams = await _client.GetLiveStreamsAsync(provider, null, cancellationToken).ConfigureAwait(false);

        var channels = new List<CatalogChannel>(streams.Count);
        var perCategoryCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var filter = provider.LiveFilter;
        var position = 0;

        foreach (var stream in streams)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stream.StreamId is null)
            {
                continue;
            }

            var categoryId = stream.CategoryId ?? string.Empty;
            categoryNames.TryGetValue(categoryId, out var categoryName);

            if (!filter.AllowsCategory(categoryId, categoryName) || !filter.AllowsTitle(stream.Name))
            {
                continue;
            }

            if (filter.MaxItemsPerCategory > 0)
            {
                perCategoryCount.TryGetValue(categoryId, out var used);
                if (used >= filter.MaxItemsPerCategory)
                {
                    continue;
                }

                perCategoryCount[categoryId] = used + 1;
            }

            position++;
            var name = (stream.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                name = string.Create(CultureInfo.InvariantCulture, $"Channel {stream.StreamId}");
            }

            var streamUrl = !string.IsNullOrWhiteSpace(stream.DirectSource)
                ? UrlNormalizer.ResolveAbsolute(urls.BaseUrl, stream.DirectSource) ?? urls.LiveStream(stream.StreamId.Value, provider.LiveStreamFormat)
                : urls.LiveStream(stream.StreamId.Value, provider.LiveStreamFormat);

            channels.Add(new CatalogChannel
            {
                Id = stream.StreamId.Value.ToString(CultureInfo.InvariantCulture),
                Name = name,
                Number = ChannelNumbering.Resolve(provider, stream.Number, position),
                Group = categoryName,
                LogoUrl = UrlNormalizer.ResolveAbsolute(urls.BaseUrl, stream.Icon),
                EpgChannelId = stream.EpgChannelId,
                StreamUrl = streamUrl,
                HasArchive = stream.HasArchive,
                ArchiveDays = stream.ArchiveDays ?? 0,
                IsHd = InferHd(name)
            });
        }

        _logger.LogInformation(
            "{Provider}: {Kept} of {Total} live channels passed the filter",
            provider.Name,
            channels.Count,
            streams.Count);

        return channels;
    }

    /// <summary>
    /// Fetches the movie list, enriching as many titles as the detail budget allows.
    /// </summary>
    /// <param name="provider">The provider to fetch from.</param>
    /// <param name="options">The plugin settings.</param>
    /// <param name="detailCache">Previously fetched detail, keyed by stream identifier. Updated in place.</param>
    /// <param name="progress">Receives a 0-1 completion fraction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The movies that pass the provider's movie filter.</returns>
    public async Task<IReadOnlyList<CatalogMovie>> GetMoviesAsync(
        ProviderOptions provider,
        PluginConfiguration options,
        IDictionary<string, MovieDetailCacheEntry> detailCache,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var urls = new XtreamUrlBuilder(provider);
        var categories = await _client.GetVodCategoriesAsync(provider, cancellationToken).ConfigureAwait(false);
        var categoryNames = categories
            .Where(c => c.Id is not null)
            .ToDictionary(c => c.Id!, c => c.Name ?? c.Id!, StringComparer.OrdinalIgnoreCase);

        var streams = await _client.GetVodStreamsAsync(provider, null, cancellationToken).ConfigureAwait(false);

        var selected = new List<XtreamVodStream>();
        var perCategoryCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var filter = provider.MovieFilter;

        foreach (var stream in streams)
        {
            if (stream.StreamId is null)
            {
                continue;
            }

            var categoryId = stream.CategoryId ?? string.Empty;
            categoryNames.TryGetValue(categoryId, out var categoryName);

            if (!filter.AllowsCategory(categoryId, categoryName) || !filter.AllowsTitle(stream.Name))
            {
                continue;
            }

            if (filter.MaxItemsPerCategory > 0)
            {
                perCategoryCount.TryGetValue(categoryId, out var used);
                if (used >= filter.MaxItemsPerCategory)
                {
                    continue;
                }

                perCategoryCount[categoryId] = used + 1;
            }

            selected.Add(stream);
        }

        _logger.LogInformation(
            "{Provider}: {Kept} of {Total} movies passed the filter",
            provider.Name,
            selected.Count,
            streams.Count);

        var budget = options.FetchMovieDetails ? Math.Max(0, options.MovieDetailBudget) : 0;
        var movies = new List<CatalogMovie>(selected.Count);
        var processed = 0;

        foreach (var stream in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = stream.StreamId!.Value.ToString(CultureInfo.InvariantCulture);
            detailCache.TryGetValue(key, out var cached);

            if (cached is null && budget > 0)
            {
                budget--;
                cached = await FetchMovieDetailAsync(provider, stream.StreamId.Value, cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                {
                    detailCache[key] = cached;
                }
            }

            movies.Add(BuildMovie(provider, urls, stream, categoryNames, cached));

            processed++;
            progress?.Report(selected.Count == 0 ? 1d : (double)processed / selected.Count);
        }

        return movies;
    }

    /// <summary>
    /// Fetches the series list and each series' episodes.
    /// </summary>
    /// <param name="provider">The provider to fetch from.</param>
    /// <param name="options">The plugin settings.</param>
    /// <param name="progress">Receives a 0-1 completion fraction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The series that pass the provider's series filter.</returns>
    public async Task<IReadOnlyList<CatalogSeries>> GetSeriesAsync(
        ProviderOptions provider,
        PluginConfiguration options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var urls = new XtreamUrlBuilder(provider);
        var categories = await _client.GetSeriesCategoriesAsync(provider, cancellationToken).ConfigureAwait(false);
        var categoryNames = categories
            .Where(c => c.Id is not null)
            .ToDictionary(c => c.Id!, c => c.Name ?? c.Id!, StringComparer.OrdinalIgnoreCase);

        var listed = await _client.GetSeriesAsync(provider, null, cancellationToken).ConfigureAwait(false);

        var selected = new List<XtreamSeries>();
        var perCategoryCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var filter = provider.SeriesFilter;

        foreach (var series in listed)
        {
            if (series.SeriesId is null)
            {
                continue;
            }

            var categoryId = series.CategoryId ?? string.Empty;
            categoryNames.TryGetValue(categoryId, out var categoryName);

            if (!filter.AllowsCategory(categoryId, categoryName) || !filter.AllowsTitle(series.Name))
            {
                continue;
            }

            if (filter.MaxItemsPerCategory > 0)
            {
                perCategoryCount.TryGetValue(categoryId, out var used);
                if (used >= filter.MaxItemsPerCategory)
                {
                    continue;
                }

                perCategoryCount[categoryId] = used + 1;
            }

            selected.Add(series);
        }

        _logger.LogInformation(
            "{Provider}: {Kept} of {Total} series passed the filter; fetching episode listings",
            provider.Name,
            selected.Count,
            listed.Count);

        var concurrency = Math.Clamp(options.SeriesConcurrency, 1, 16);
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var completed = 0;

        var tasks = selected.Select(async series =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var built = await BuildSeriesAsync(provider, urls, series, categoryNames, cancellationToken).ConfigureAwait(false);
                return built;
            }
            finally
            {
                gate.Release();
                var done = Interlocked.Increment(ref completed);
                progress?.Report(selected.Count == 0 ? 1d : (double)done / selected.Count);
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(s => s is not null).Select(s => s!).ToArray();
    }

    private async Task<MovieDetailCacheEntry?> FetchMovieDetailAsync(ProviderOptions provider, long streamId, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _client.GetVodInfoAsync(provider, streamId, cancellationToken).ConfigureAwait(false);
            if (detail?.Info is null)
            {
                // Remember the miss so later syncs do not keep spending budget on it.
                return new MovieDetailCacheEntry();
            }

            var info = detail.Info;
            return new MovieDetailCacheEntry
            {
                Title = info.Name,
                OriginalTitle = info.OriginalName,
                Plot = info.EffectivePlot,
                PosterUrl = info.EffectivePoster,
                BackdropUrls = info.BackdropPaths,
                TmdbId = info.TmdbId,
                Genres = TitleParser.SplitList(info.Genre),
                Cast = TitleParser.SplitList(info.EffectiveCast),
                Directors = TitleParser.SplitList(info.Director),
                Rating = info.Rating,
                ReleaseDate = TitleParser.ParseDate(info.EffectiveReleaseDate),
                RuntimeSeconds = info.DurationSeconds,
                Certification = info.MpaaRating,
                TrailerId = info.YoutubeTrailer
            };
        }
        catch (XtreamApiException ex)
        {
            _logger.LogDebug("{Provider}: no detail for movie {StreamId}: {Reason}", provider.Name, streamId, ex.Message);
            return null;
        }
    }

    private static CatalogMovie BuildMovie(
        ProviderOptions provider,
        XtreamUrlBuilder urls,
        XtreamVodStream stream,
        IReadOnlyDictionary<string, string> categoryNames,
        MovieDetailCacheEntry? detail)
    {
        var streamId = stream.StreamId!.Value;
        categoryNames.TryGetValue(stream.CategoryId ?? string.Empty, out var categoryName);

        // Prefer the metadata title, which is usually free of the quality tags the stream name carries.
        var rawTitle = !string.IsNullOrWhiteSpace(detail?.Title) ? detail!.Title! : stream.Name;
        var parsed = TitleParser.ParseMovie(rawTitle);

        var releaseDate = detail?.ReleaseDate;
        var year = parsed.Year ?? releaseDate?.Year;

        var streamUrl = !string.IsNullOrWhiteSpace(stream.DirectSource)
            ? UrlNormalizer.ResolveAbsolute(urls.BaseUrl, stream.DirectSource) ?? urls.MovieStream(streamId, stream.ContainerExtension)
            : urls.MovieStream(streamId, stream.ContainerExtension);

        return new CatalogMovie
        {
            Id = streamId.ToString(CultureInfo.InvariantCulture),
            Title = parsed.Title,
            Year = year,
            OriginalTitle = detail?.OriginalTitle,
            Group = categoryName,
            StreamUrl = streamUrl,
            Container = stream.ContainerExtension,
            PosterUrl = UrlNormalizer.ResolveAbsolute(urls.BaseUrl, detail?.PosterUrl ?? stream.Icon),
            BackdropUrls = ResolveAll(urls.BaseUrl, detail?.BackdropUrls),
            Plot = detail?.Plot,
            Genres = detail?.Genres ?? Array.Empty<string>(),
            Cast = detail?.Cast ?? Array.Empty<string>(),
            Directors = detail?.Directors ?? Array.Empty<string>(),
            Rating = detail?.Rating ?? stream.Rating,
            TmdbId = detail?.TmdbId ?? stream.TmdbId,
            ReleaseDate = releaseDate,
            Certification = detail?.Certification,
            TrailerUrl = BuildTrailerUrl(detail?.TrailerId),
            Runtime = detail?.RuntimeSeconds is > 0 ? TimeSpan.FromSeconds(detail.RuntimeSeconds.Value) : null
        };
    }

    private async Task<CatalogSeries?> BuildSeriesAsync(
        ProviderOptions provider,
        XtreamUrlBuilder urls,
        XtreamSeries listed,
        IReadOnlyDictionary<string, string> categoryNames,
        CancellationToken cancellationToken)
    {
        var seriesId = listed.SeriesId!.Value;

        XtreamSeriesDetail? detail;
        try
        {
            detail = await GetSeriesInfoWithRetryAsync(provider, seriesId, listed.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (XtreamApiException ex)
        {
            _logger.LogWarning("{Provider}: could not load episodes for series {SeriesId} ({Name}): {Reason}", provider.Name, seriesId, listed.Name, ex.Message);
            return null;
        }

        if (detail is null || detail.Episodes.Count == 0)
        {
            _logger.LogDebug("{Provider}: series {Name} has no episodes; skipping", provider.Name, listed.Name);
            return null;
        }

        var info = detail.Info ?? listed;
        var parsed = TitleParser.ParseMovie(string.IsNullOrWhiteSpace(info.Name) ? listed.Name : info.Name);
        var releaseDate = TitleParser.ParseDate(info.EffectiveReleaseDate ?? listed.EffectiveReleaseDate);
        categoryNames.TryGetValue(listed.CategoryId ?? string.Empty, out var categoryName);

        var seasonMetadata = detail.Seasons
            .Where(s => s.SeasonNumber is not null)
            .GroupBy(s => s.SeasonNumber!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var seasons = new List<CatalogSeason>();

        foreach (var pair in detail.Episodes)
        {
            if (!int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seasonNumber))
            {
                // Some panels key the map by an opaque label; fall back to the episodes' own field.
                seasonNumber = pair.Value.Select(e => e.Season ?? e.Info?.Season).FirstOrDefault(s => s is not null) ?? 1;
            }

            var episodes = new List<CatalogEpisode>();
            var fallbackNumber = 0;

            foreach (var episode in pair.Value.OrderBy(e => e.EpisodeNumber ?? int.MaxValue))
            {
                if (string.IsNullOrWhiteSpace(episode.Id))
                {
                    continue;
                }

                fallbackNumber++;
                var episodeNumber = episode.EpisodeNumber ?? fallbackNumber;

                var streamUrl = !string.IsNullOrWhiteSpace(episode.DirectSource)
                    ? UrlNormalizer.ResolveAbsolute(urls.BaseUrl, episode.DirectSource) ?? urls.EpisodeStream(episode.Id!, episode.ContainerExtension)
                    : urls.EpisodeStream(episode.Id!, episode.ContainerExtension);

                episodes.Add(new CatalogEpisode
                {
                    Id = episode.Id!,
                    SeasonNumber = episode.Season ?? episode.Info?.Season ?? seasonNumber,
                    EpisodeNumber = episodeNumber,
                    Title = CleanEpisodeTitle(episode.Title, parsed.Title, seasonNumber, episodeNumber),
                    StreamUrl = streamUrl,
                    Container = episode.ContainerExtension,
                    Plot = episode.Info?.EffectivePlot,
                    ImageUrl = UrlNormalizer.ResolveAbsolute(urls.BaseUrl, episode.Info?.Image),
                    Runtime = episode.Info?.DurationSeconds is > 0 ? TimeSpan.FromSeconds(episode.Info.DurationSeconds!.Value) : null,
                    AirDate = TitleParser.ParseDate(episode.Info?.EffectiveReleaseDate),
                    Rating = episode.Info?.Rating,
                    TmdbId = episode.Info?.TmdbId
                });
            }

            if (episodes.Count == 0)
            {
                continue;
            }

            seasonMetadata.TryGetValue(seasonNumber, out var seasonInfo);

            seasons.Add(new CatalogSeason
            {
                Number = seasonNumber,
                Name = seasonInfo?.Name,
                Overview = seasonInfo?.Overview,
                PosterUrl = UrlNormalizer.ResolveAbsolute(urls.BaseUrl, seasonInfo?.Cover),
                AirDate = TitleParser.ParseDate(seasonInfo?.AirDate),
                Episodes = episodes
            });
        }

        if (seasons.Count == 0)
        {
            return null;
        }

        return new CatalogSeries
        {
            Id = seriesId.ToString(CultureInfo.InvariantCulture),
            Title = parsed.Title,
            Year = parsed.Year ?? releaseDate?.Year,
            Group = categoryName,
            PosterUrl = UrlNormalizer.ResolveAbsolute(urls.BaseUrl, info.Cover ?? listed.Cover),
            BackdropUrls = ResolveAll(urls.BaseUrl, info.BackdropPaths.Count > 0 ? info.BackdropPaths : listed.BackdropPaths),
            Plot = info.Plot ?? listed.Plot,
            Genres = TitleParser.SplitList(info.Genre ?? listed.Genre),
            Cast = TitleParser.SplitList(info.Cast ?? listed.Cast),
            Directors = TitleParser.SplitList(info.Director ?? listed.Director),
            Rating = info.Rating ?? listed.Rating,
            TmdbId = info.TmdbId ?? listed.TmdbId,
            ReleaseDate = releaseDate,
            EpisodeRuntime = (info.EpisodeRuntimeMinutes ?? listed.EpisodeRuntimeMinutes) is > 0
                ? TimeSpan.FromMinutes((info.EpisodeRuntimeMinutes ?? listed.EpisodeRuntimeMinutes)!.Value)
                : null,
            Seasons = seasons.OrderBy(s => s.Number).ToArray()
        };
    }

    private async Task<XtreamSeriesDetail?> GetSeriesInfoWithRetryAsync(
        ProviderOptions provider,
        long seriesId,
        string? seriesName,
        CancellationToken cancellationToken)
    {
        const int attempts = 3;
        XtreamApiException? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await _client.GetSeriesInfoAsync(provider, seriesId, cancellationToken).ConfigureAwait(false);
            }
            catch (XtreamApiException ex)
            {
                lastError = ex;
                if (attempt == attempts)
                {
                    break;
                }

                _logger.LogDebug(
                    "{Provider}: series {Name} ({SeriesId}) attempt {Attempt} failed ({Reason}); retrying",
                    provider.Name,
                    seriesName,
                    seriesId,
                    attempt,
                    ex.Message);

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastError ?? new XtreamApiException("The panel did not return series details.");
    }

    private static string? CleanEpisodeTitle(string? rawTitle, string seriesTitle, int season, int episode)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return null;
        }

        // Panels usually repeat the whole "Show S01E01 Pilot" string in the episode title.
        var parsedEpisode = TitleParser.ParseEpisode(rawTitle);
        if (parsedEpisode is not null)
        {
            var candidate = parsedEpisode.Value.EpisodeTitle;
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }

            return null;
        }

        var cleaned = TitleParser.ParseMovie(rawTitle).Title;
        if (cleaned.Length == 0)
        {
            return null;
        }

        // Drop a title that is just the series name, which carries no information.
        if (string.Equals(cleaned, seriesTitle, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return cleaned;
    }

    private static string? BuildTrailerUrl(string? trailerId)
    {
        if (string.IsNullOrWhiteSpace(trailerId))
        {
            return null;
        }

        var value = trailerId.Trim();
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? value
            : "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(value);
    }

    private static bool? InferHd(string name)
    {
        if (name.Contains("UHD", StringComparison.OrdinalIgnoreCase)
            || name.Contains("4K", StringComparison.OrdinalIgnoreCase)
            || name.Contains("FHD", StringComparison.OrdinalIgnoreCase)
            || name.Contains("HD", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.Contains("SD", StringComparison.OrdinalIgnoreCase) ? false : null;
    }

    private static IReadOnlyList<string> ResolveAll(string baseUrl, IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        return values
            .Select(v => UrlNormalizer.ResolveAbsolute(baseUrl, v))
            .Where(v => v is not null)
            .Select(v => v!)
            .ToArray();
    }
}
