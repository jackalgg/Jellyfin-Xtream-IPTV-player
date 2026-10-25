using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.M3u;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Catalog;

/// <summary>
/// Builds a <see cref="ProviderCatalog"/> from an M3U playlist.
/// </summary>
/// <remarks>
/// A playlist carries no structure beyond free text, so the series support here comes entirely
/// from grouping entries by the show name and episode number parsed out of their titles. That is
/// why an M3U-only subscription still ends up with real seasons and episodes rather than a flat
/// list of files.
/// </remarks>
public class M3uCatalogSource
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="M3uCatalogSource"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to fetch playlists with.</param>
    /// <param name="logger">The logger.</param>
    public M3uCatalogSource(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Fetches and parses a provider's playlist.
    /// </summary>
    /// <param name="provider">The provider to fetch from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed playlist.</returns>
    public async Task<M3uPlaylist> FetchPlaylistAsync(ProviderOptions provider, CancellationToken cancellationToken)
    {
        var location = ResolvePlaylistLocation(provider);

        if (!location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(location))
            {
                throw new Xtream.XtreamApiException($"No playlist found at {location}.");
            }

            var fileStream = File.OpenRead(location);
            await using (fileStream.ConfigureAwait(false))
            {
                return await M3uParser.ParseAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgents.Resolve(provider.UserAgent));

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Xtream.XtreamApiException($"The playlist URL returned HTTP {(int)response.StatusCode}.");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var playlist = await M3uParser.ParseAsync(stream, cancellationToken).ConfigureAwait(false);

            if (playlist.Entries.Count == 0)
            {
                throw new Xtream.XtreamApiException(
                    "The playlist loaded but held no entries. Check that the URL returns an M3U file rather than an error page.");
            }

            return playlist;
        }
    }

    /// <summary>
    /// Turns a parsed playlist into a catalog.
    /// </summary>
    /// <param name="provider">The owning provider.</param>
    /// <param name="playlist">The parsed playlist.</param>
    /// <returns>The catalog.</returns>
    public ProviderCatalog BuildCatalog(ProviderOptions provider, M3uPlaylist playlist)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(playlist);

        var channels = BuildChannels(provider, playlist);
        var movies = BuildMovies(provider, playlist);
        var series = BuildSeries(provider, playlist);

        _logger.LogInformation(
            "{Provider}: playlist held {Entries} entries, giving {Channels} channels, {Movies} movies, and {Series} series",
            provider.Name,
            playlist.Entries.Count,
            channels.Count,
            movies.Count,
            series.Count);

        return new ProviderCatalog
        {
            ProviderId = provider.Id,
            ProviderName = provider.Name,
            Channels = channels,
            Movies = movies,
            Series = series,
            DiscoveredEpgUrl = playlist.EpgUrl
        };
    }

    /// <summary>
    /// Works out which address a provider's playlist lives at.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The playlist address or local path.</returns>
    public static string ResolvePlaylistLocation(ProviderOptions provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!string.IsNullOrWhiteSpace(provider.PlaylistUrl))
        {
            return provider.PlaylistUrl.Trim();
        }

        // An Xtream provider falling back to playlist mode can build its own playlist URL.
        var urls = new Xtream.XtreamUrlBuilder(provider);
        if (urls.IsConfigured)
        {
            return urls.Playlist(provider.LiveStreamFormat == StreamFormat.Hls ? "m3u8" : "ts");
        }

        throw new Xtream.XtreamApiException("No playlist URL is configured for this provider.");
    }

    private static IReadOnlyList<CatalogChannel> BuildChannels(ProviderOptions provider, M3uPlaylist playlist)
    {
        var filter = provider.LiveFilter;
        var channels = new List<CatalogChannel>();
        var perGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;

        foreach (var entry in playlist.Entries.Where(e => e.Kind == M3uEntryKind.Live))
        {
            var group = entry.GroupTitle ?? string.Empty;
            if (!filter.AllowsCategory(group, group) || !filter.AllowsTitle(entry.Name))
            {
                continue;
            }

            if (filter.MaxItemsPerCategory > 0)
            {
                perGroup.TryGetValue(group, out var used);
                if (used >= filter.MaxItemsPerCategory)
                {
                    continue;
                }

                perGroup[group] = used + 1;
            }

            position++;
            var name = string.IsNullOrWhiteSpace(entry.Name) ? entry.TvgName ?? "Channel" : entry.Name;
            var id = MakeUniqueId(seenIds, entry.TvgId, entry.Url, name);

            channels.Add(new CatalogChannel
            {
                Id = id,
                Name = name.Trim(),
                Number = ChannelNumbering.Resolve(provider, entry.TvgChannelNumber, position),
                Group = string.IsNullOrWhiteSpace(group) ? null : group,
                LogoUrl = entry.TvgLogo,
                EpgChannelId = entry.TvgId,
                StreamUrl = entry.Url,
                HasArchive = !string.IsNullOrWhiteSpace(entry.CatchupDays),
                ArchiveDays = int.TryParse(entry.CatchupDays, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) ? days : 0,
                IsHd = InferHd(name)
            });
        }

        return channels;
    }

    private static IReadOnlyList<CatalogMovie> BuildMovies(ProviderOptions provider, M3uPlaylist playlist)
    {
        var filter = provider.MovieFilter;
        var movies = new List<CatalogMovie>();
        var perGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in playlist.Entries.Where(e => e.Kind == M3uEntryKind.Movie))
        {
            var group = entry.GroupTitle ?? string.Empty;
            if (!filter.AllowsCategory(group, group) || !filter.AllowsTitle(entry.Name))
            {
                continue;
            }

            if (filter.MaxItemsPerCategory > 0)
            {
                perGroup.TryGetValue(group, out var used);
                if (used >= filter.MaxItemsPerCategory)
                {
                    continue;
                }

                perGroup[group] = used + 1;
            }

            var parsed = TitleParser.ParseMovie(entry.Name);
            if (parsed.Title.Length == 0)
            {
                continue;
            }

            movies.Add(new CatalogMovie
            {
                Id = MakeUniqueId(seenIds, null, entry.Url, parsed.Title),
                Title = parsed.Title,
                Year = parsed.Year,
                Group = string.IsNullOrWhiteSpace(group) ? null : group,
                StreamUrl = entry.Url,
                Container = GetExtension(entry.Url),
                PosterUrl = entry.TvgLogo,
                Runtime = entry.Duration > 0 ? TimeSpan.FromSeconds(entry.Duration) : null
            });
        }

        return movies;
    }

    private static IReadOnlyList<CatalogSeries> BuildSeries(ProviderOptions provider, M3uPlaylist playlist)
    {
        var filter = provider.SeriesFilter;

        // Group parsed episodes by show, then by season. The show title is the grouping key, so
        // "EN - Show S01E01" and "Show S02E03 [1080p]" land in the same series.
        var shows = new Dictionary<string, SeriesAccumulator>(StringComparer.OrdinalIgnoreCase);
        var perGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in playlist.Entries.Where(e => e.Kind == M3uEntryKind.Episode))
        {
            var parsed = TitleParser.ParseEpisode(entry.Name);
            if (parsed is null)
            {
                continue;
            }

            var episode = parsed.Value;
            var group = entry.GroupTitle ?? string.Empty;

            if (!filter.AllowsCategory(group, group) || !filter.AllowsTitle(episode.SeriesTitle))
            {
                continue;
            }

            var key = BuildSeriesKey(episode.SeriesTitle, episode.Year);

            if (!shows.TryGetValue(key, out var accumulator))
            {
                if (filter.MaxItemsPerCategory > 0)
                {
                    perGroup.TryGetValue(group, out var used);
                    if (used >= filter.MaxItemsPerCategory)
                    {
                        continue;
                    }

                    perGroup[group] = used + 1;
                }

                accumulator = new SeriesAccumulator
                {
                    Title = episode.SeriesTitle,
                    Year = episode.Year,
                    Group = string.IsNullOrWhiteSpace(group) ? null : group,
                    PosterUrl = entry.TvgLogo
                };
                shows[key] = accumulator;
            }

            accumulator.PosterUrl ??= entry.TvgLogo;

            var episodeKey = (episode.SeasonNumber, episode.EpisodeNumber);
            if (accumulator.Episodes.ContainsKey(episodeKey))
            {
                // Providers list the same episode in several qualities; keep the first.
                continue;
            }

            accumulator.Episodes[episodeKey] = new CatalogEpisode
            {
                Id = StableId(entry.Url),
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                Title = episode.EpisodeTitle,
                StreamUrl = entry.Url,
                Container = GetExtension(entry.Url),
                Runtime = entry.Duration > 0 ? TimeSpan.FromSeconds(entry.Duration) : null
            };
        }

        return shows.Values
            .Select(accumulator => new CatalogSeries
            {
                Id = StableId(accumulator.Title + "|" + accumulator.Year),
                Title = accumulator.Title,
                Year = accumulator.Year,
                Group = accumulator.Group,
                PosterUrl = accumulator.PosterUrl,
                Seasons = accumulator.Episodes
                    .GroupBy(pair => pair.Key.Season)
                    .OrderBy(g => g.Key)
                    .Select(g => new CatalogSeason
                    {
                        Number = g.Key,
                        Episodes = g.OrderBy(pair => pair.Key.Episode).Select(pair => pair.Value).ToArray()
                    })
                    .ToArray()
            })
            .ToArray();
    }

    private static string BuildSeriesKey(string title, int? year)
        => year is null ? title : string.Create(CultureInfo.InvariantCulture, $"{title} ({year})");

    private static string MakeUniqueId(HashSet<string> seen, string? preferred, string url, string name)
    {
        var candidate = !string.IsNullOrWhiteSpace(preferred) ? preferred!.Trim() : StableId(url);

        if (seen.Add(candidate))
        {
            return candidate;
        }

        // A playlist can reuse a tvg-id across several feeds of the same channel.
        var fallback = StableId(candidate + "|" + url + "|" + name);
        var suffix = 2;
        while (!seen.Add(fallback))
        {
            fallback = StableId(candidate + "|" + url + "|" + name + "|" + suffix.ToString(CultureInfo.InvariantCulture));
            suffix++;
        }

        return fallback;
    }

    private static string StableId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static string? GetExtension(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.');
        return string.IsNullOrWhiteSpace(extension) ? null : extension;
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

    private sealed class SeriesAccumulator
    {
        public string Title { get; set; } = string.Empty;

        public int? Year { get; set; }

        public string? Group { get; set; }

        public string? PosterUrl { get; set; }

        public Dictionary<(int Season, int Episode), CatalogEpisode> Episodes { get; } = new();
    }
}
