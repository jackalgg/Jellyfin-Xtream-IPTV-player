using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Catalog;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Playback;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Library;

/// <summary>
/// What one write pass changed on disk.
/// </summary>
public class LibraryWriteResult
{
    /// <summary>
    /// Gets or sets how many files were created or rewritten.
    /// </summary>
    public int FilesWritten { get; set; }

    /// <summary>
    /// Gets or sets how many files were already correct and left alone.
    /// </summary>
    public int FilesUnchanged { get; set; }

    /// <summary>
    /// Gets or sets how many files were removed because the provider no longer offers them.
    /// </summary>
    public int FilesRemoved { get; set; }

    /// <summary>
    /// Gets or sets how many movies were published.
    /// </summary>
    public int MovieCount { get; set; }

    /// <summary>
    /// Gets or sets how many series were published.
    /// </summary>
    public int SeriesCount { get; set; }

    /// <summary>
    /// Gets or sets how many episodes were published.
    /// </summary>
    public int EpisodeCount { get; set; }

    /// <summary>
    /// Gets or sets the relative paths this pass produced, which the next pass prunes against.
    /// </summary>
    public IReadOnlySet<string> WrittenPaths { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Publishes a provider's movies and series as a <c>.strm</c> and <c>.nfo</c> tree that Jellyfin's
/// own library scanner understands.
/// </summary>
/// <remarks>
/// This is the design decision that separates StreamFin from plugins that expose on-demand content
/// through Jellyfin's channel API. A channel is a flat browse surface: no season grouping, no
/// resume, no "next up", no subtitle search, no collections. A folder of <c>.strm</c> shortcuts is
/// a real library, so every feature Jellyfin has for local media works on IPTV content unchanged.
/// </remarks>
public class LibraryWriter
{
    private readonly PlaybackLinkFactory _links;
    private readonly ILogger<LibraryWriter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryWriter"/> class.
    /// </summary>
    /// <param name="links">The playback URL factory.</param>
    /// <param name="logger">The logger.</param>
    public LibraryWriter(PlaybackLinkFactory links, ILogger<LibraryWriter> logger)
    {
        _links = links;
        _logger = logger;
    }

    /// <summary>
    /// Writes a provider's movie and series libraries.
    /// </summary>
    /// <param name="provider">The provider being published.</param>
    /// <param name="catalog">What the provider offers.</param>
    /// <param name="options">The plugin settings.</param>
    /// <param name="previouslyWritten">The relative paths the previous pass wrote.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What changed.</returns>
    public async Task<LibraryWriteResult> WriteAsync(
        ProviderOptions provider,
        ProviderCatalog catalog,
        PluginConfiguration options,
        IReadOnlySet<string> previouslyWritten,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);

        var result = new LibraryWriteResult();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = GetProviderRoot(provider, options);

        if (provider.EnableMovies)
        {
            await WriteMoviesAsync(provider, root, catalog.Movies, options, written, result, cancellationToken).ConfigureAwait(false);
        }

        if (provider.EnableSeries)
        {
            await WriteSeriesAsync(provider, root, catalog.Series, options, written, result, cancellationToken).ConfigureAwait(false);
        }

        if (options.PruneRemovedItems)
        {
            result.FilesRemoved = PruneStaleFiles(root, previouslyWritten, written);
        }

        result.WrittenPaths = written;

        _logger.LogInformation(
            "{Provider}: wrote {Written} files, left {Unchanged} unchanged, removed {Removed}; {Movies} movies, {Series} series, {Episodes} episodes",
            provider.Name,
            result.FilesWritten,
            result.FilesUnchanged,
            result.FilesRemoved,
            result.MovieCount,
            result.SeriesCount,
            result.EpisodeCount);

        return result;
    }

    /// <summary>
    /// Gets the directory a provider's movie library lives in.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="options">The plugin settings.</param>
    /// <returns>An absolute directory path.</returns>
    public static string GetMoviesPath(ProviderOptions provider, PluginConfiguration options)
        => Path.Combine(GetProviderRoot(provider, options), "Movies");

    /// <summary>
    /// Gets the directory a provider's series library lives in.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="options">The plugin settings.</param>
    /// <returns>An absolute directory path.</returns>
    public static string GetSeriesPath(ProviderOptions provider, PluginConfiguration options)
        => Path.Combine(GetProviderRoot(provider, options), "Shows");

    /// <summary>
    /// Gets the directory holding everything StreamFin generated for a provider.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="options">The plugin settings.</param>
    /// <returns>An absolute directory path.</returns>
    public static string GetProviderRoot(ProviderOptions provider, PluginConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var root = StreamFinPlugin.Instance?.GetLibraryRoot()
            ?? Path.Combine(Path.GetTempPath(), "streamfin", "media");

        return Path.Combine(root, PathSanitizer.Sanitize(provider.Name, provider.Id));
    }

    private async Task WriteMoviesAsync(
        ProviderOptions provider,
        string root,
        IReadOnlyList<CatalogMovie> movies,
        PluginConfiguration options,
        HashSet<string> written,
        LibraryWriteResult result,
        CancellationToken cancellationToken)
    {
        var moviesRoot = Path.Combine(root, "Movies");
        var usedFolders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var movie in movies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(movie.Title) || string.IsNullOrWhiteSpace(movie.StreamUrl))
            {
                continue;
            }

            var folderName = Disambiguate(usedFolders, PathSanitizer.BuildTitleSegment(movie.Title, movie.Year));
            var folderPath = Path.Combine(moviesRoot, folderName);

            await WriteFileAsync(
                Path.Combine(folderPath, folderName + ".strm"),
                _links.BuildStrm(options, provider, PlaybackKind.Movie, movie.Id, movie.StreamUrl, movie.Container),
                root,
                written,
                result,
                cancellationToken).ConfigureAwait(false);

            if (options.WriteNfoMetadata)
            {
                await WriteFileAsync(
                    Path.Combine(folderPath, "movie.nfo"),
                    NfoWriter.BuildMovie(movie),
                    root,
                    written,
                    result,
                    cancellationToken).ConfigureAwait(false);
            }

            result.MovieCount++;
        }
    }

    private async Task WriteSeriesAsync(
        ProviderOptions provider,
        string root,
        IReadOnlyList<CatalogSeries> allSeries,
        PluginConfiguration options,
        HashSet<string> written,
        LibraryWriteResult result,
        CancellationToken cancellationToken)
    {
        var seriesRoot = Path.Combine(root, "Shows");
        var usedFolders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // A provider often lists the same show once per language or quality tier. Merging them
        // into a single series is what keeps the library from showing six copies of one show.
        foreach (var group in allSeries
            .Where(s => !string.IsNullOrWhiteSpace(s.Title) && s.Seasons.Count > 0)
            .GroupBy(s => PathSanitizer.BuildTitleSegment(s.Title, s.Year), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var merged = MergeSeries(group.ToArray());
            var folderName = Disambiguate(usedFolders, group.Key);
            var folderPath = Path.Combine(seriesRoot, folderName);

            if (options.WriteNfoMetadata)
            {
                await WriteFileAsync(
                    Path.Combine(folderPath, "tvshow.nfo"),
                    NfoWriter.BuildSeries(merged),
                    root,
                    written,
                    result,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var season in merged.Seasons)
            {
                var seasonPath = Path.Combine(folderPath, PathSanitizer.BuildSeasonSegment(season.Number));

                if (options.WriteNfoMetadata && (season.AirDate is not null || !string.IsNullOrWhiteSpace(season.Overview)))
                {
                    await WriteFileAsync(
                        Path.Combine(seasonPath, "season.nfo"),
                        NfoWriter.BuildSeason(season, merged.Title),
                        root,
                        written,
                        result,
                        cancellationToken).ConfigureAwait(false);
                }

                foreach (var episode in season.Episodes)
                {
                    if (string.IsNullOrWhiteSpace(episode.StreamUrl))
                    {
                        continue;
                    }

                    var stem = PathSanitizer.BuildEpisodeSegment(
                        folderName,
                        episode.SeasonNumber,
                        episode.EpisodeNumber,
                        episode.Title);

                    await WriteFileAsync(
                        Path.Combine(seasonPath, stem + ".strm"),
                        _links.BuildStrm(options, provider, PlaybackKind.Series, episode.Id, episode.StreamUrl, episode.Container),
                        root,
                        written,
                        result,
                        cancellationToken).ConfigureAwait(false);

                    if (options.WriteNfoMetadata)
                    {
                        await WriteFileAsync(
                            Path.Combine(seasonPath, stem + ".nfo"),
                            NfoWriter.BuildEpisode(episode, merged),
                            root,
                            written,
                            result,
                            cancellationToken).ConfigureAwait(false);
                    }

                    result.EpisodeCount++;
                }
            }

            result.SeriesCount++;
        }
    }

    private static CatalogSeries MergeSeries(IReadOnlyList<CatalogSeries> duplicates)
    {
        if (duplicates.Count == 1)
        {
            return duplicates[0];
        }

        // Treat the richest entry as the metadata source, then union the episodes.
        var primary = duplicates
            .OrderByDescending(s => s.Plot?.Length ?? 0)
            .ThenByDescending(s => s.Seasons.Sum(season => season.Episodes.Count))
            .First();

        var episodes = new Dictionary<(int Season, int Episode), CatalogEpisode>();
        var seasonMetadata = new Dictionary<int, CatalogSeason>();

        foreach (var series in duplicates)
        {
            foreach (var season in series.Seasons)
            {
                if (!seasonMetadata.TryGetValue(season.Number, out var known)
                    || (known.Overview is null && season.Overview is not null))
                {
                    seasonMetadata[season.Number] = season;
                }

                foreach (var episode in season.Episodes)
                {
                    episodes.TryAdd((episode.SeasonNumber, episode.EpisodeNumber), episode);
                }
            }
        }

        return new CatalogSeries
        {
            Id = primary.Id,
            Title = primary.Title,
            Year = primary.Year,
            OriginalTitle = primary.OriginalTitle,
            Group = primary.Group,
            PosterUrl = duplicates.Select(s => s.PosterUrl).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)),
            BackdropUrls = duplicates.SelectMany(s => s.BackdropUrls).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Plot = primary.Plot,
            Genres = duplicates.SelectMany(s => s.Genres).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Cast = primary.Cast,
            Directors = primary.Directors,
            Rating = primary.Rating,
            TmdbId = duplicates.Select(s => s.TmdbId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
            ReleaseDate = primary.ReleaseDate,
            Certification = primary.Certification,
            EpisodeRuntime = primary.EpisodeRuntime,
            Seasons = episodes.Values
                .GroupBy(e => e.SeasonNumber)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    seasonMetadata.TryGetValue(g.Key, out var metadata);
                    return new CatalogSeason
                    {
                        Number = g.Key,
                        Name = metadata?.Name,
                        Overview = metadata?.Overview,
                        PosterUrl = metadata?.PosterUrl,
                        AirDate = metadata?.AirDate,
                        Episodes = g.OrderBy(e => e.EpisodeNumber).ToArray()
                    };
                })
                .ToArray()
        };
    }

    private async Task WriteFileAsync(
        string absolutePath,
        string content,
        string root,
        HashSet<string> written,
        LibraryWriteResult result,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(root, absolutePath);
        written.Add(relativePath);

        // Rewriting an unchanged file would bump its timestamp and make Jellyfin re-probe the
        // item on the next scan, which on a large library is the difference between a scan that
        // takes a minute and one that takes an hour.
        if (File.Exists(absolutePath))
        {
            try
            {
                var existing = await File.ReadAllTextAsync(absolutePath, cancellationToken).ConfigureAwait(false);
                if (string.Equals(existing, content, StringComparison.Ordinal))
                {
                    result.FilesUnchanged++;
                    return;
                }
            }
            catch (IOException)
            {
                // Unreadable means rewrite.
            }
        }

        try
        {
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(absolutePath, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            result.FilesWritten++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not write {Path}: {Reason}", absolutePath, ex.Message);
        }
    }

    private int PruneStaleFiles(string root, IReadOnlySet<string> previouslyWritten, IReadOnlySet<string> currentlyWritten)
    {
        var removed = 0;

        foreach (var relativePath in previouslyWritten)
        {
            if (currentlyWritten.Contains(relativePath))
            {
                continue;
            }

            var absolutePath = Path.Combine(root, relativePath);

            // Never follow a relative path out of the library root, however the state file got
            // that way.
            if (!absolutePath.StartsWith(root, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Could not remove {Path}: {Reason}", absolutePath, ex.Message);
            }
        }

        if (removed > 0)
        {
            RemoveEmptyDirectories(root);
        }

        return removed;
    }

    private void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug("Could not tidy empty folders under {Root}: {Reason}", root, ex.Message);
        }
    }

    private static string Disambiguate(Dictionary<string, int> used, string candidate)
    {
        if (used.TryAdd(candidate, 1))
        {
            return candidate;
        }

        var count = ++used[candidate];
        return string.Create(CultureInfo.InvariantCulture, $"{candidate} ({count})");
    }
}
