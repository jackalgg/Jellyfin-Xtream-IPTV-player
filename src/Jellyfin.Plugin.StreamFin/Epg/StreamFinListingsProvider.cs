using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Epg;

/// <summary>
/// Supplies Jellyfin's guide from a provider's XMLTV feed.
/// </summary>
public class StreamFinListingsProvider : IListingsProvider
{
    /// <summary>
    /// The provider type StreamFin registers under, which is what Jellyfin stores in
    /// <see cref="ListingsProviderInfo.Type"/>.
    /// </summary>
    public const string ProviderType = "streamfin-xmltv";

    private static readonly string[] _newsKeywords = { "news", "nieuws", "noticias", "nachrichten", "journal" };
    private static readonly string[] _sportsKeywords = { "sport", "football", "soccer", "basketball", "baseball", "hockey", "racing", "golf", "tennis", "wrestling", "boxing", "mma" };
    private static readonly string[] _kidsKeywords = { "kids", "children", "cartoon", "animation", "anime", "family", "jeunesse", "infantil" };
    private static readonly string[] _movieKeywords = { "movie", "film", "cinema", "pelicula", "película" };
    private static readonly string[] _seriesKeywords = { "series", "sitcom", "soap", "drama", "serie" };

    private readonly XmltvCache _cache;
    private readonly ILogger<StreamFinListingsProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamFinListingsProvider"/> class.
    /// </summary>
    /// <param name="cache">The guide cache.</param>
    /// <param name="logger">The logger.</param>
    public StreamFinListingsProvider(XmltvCache cache, ILogger<StreamFinListingsProvider> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "StreamFin Guide";

    /// <inheritdoc />
    public string Type => ProviderType;

    /// <inheritdoc />
    public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(
        ListingsProviderInfo info,
        string channelId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        var provider = ResolveProvider(info);
        if (provider is null)
        {
            return Array.Empty<ProgramInfo>();
        }

        await _cache.EnsureAsync(provider, forceRefresh: false, cancellationToken).ConfigureAwait(false);

        var programmes = await _cache
            .GetProgrammesAsync(provider.Id, channelId, startDateUtc, endDateUtc, cancellationToken)
            .ConfigureAwait(false);

        return programmes.Select(p => Convert(p, channelId)).ToArray();
    }

    /// <inheritdoc />
    public async Task Validate(ListingsProviderInfo info, bool validateLogin, bool validateListings)
    {
        ArgumentNullException.ThrowIfNull(info);

        var provider = ResolveProvider(info)
            ?? throw new InvalidOperationException(
                "This guide is not linked to a StreamFin provider. Add the provider on the StreamFin settings page first.");

        if (!validateListings)
        {
            return;
        }

        var channels = await _cache.EnsureAsync(provider, forceRefresh: true, CancellationToken.None).ConfigureAwait(false);
        if (channels.Count == 0)
        {
            throw new InvalidOperationException(
                $"No guide data could be loaded for {provider.Name}. Check the guide URL on the StreamFin settings page.");
        }
    }

    /// <inheritdoc />
    public Task<List<NameIdPair>> GetLineups(ListingsProviderInfo info, string country, string location)
        => Task.FromResult(new List<NameIdPair>());

    /// <inheritdoc />
    public async Task<List<ChannelInfo>> GetChannels(ListingsProviderInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        var provider = ResolveProvider(info);
        if (provider is null)
        {
            return new List<ChannelInfo>();
        }

        var channels = await _cache.EnsureAsync(provider, forceRefresh: false, cancellationToken).ConfigureAwait(false);

        return channels
            .Select(c => new ChannelInfo
            {
                Id = c.Id,
                Name = c.Name ?? c.Id,
                Number = c.Number,
                ImageUrl = c.IconUrl,
                HasImage = !string.IsNullOrWhiteSpace(c.IconUrl)
            })
            .ToList();
    }

    /// <summary>
    /// Builds the <see cref="ListingsProviderInfo"/> that represents one provider's guide.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="tunerId">The tuner whose channels this guide applies to.</param>
    /// <param name="existingId">An existing guide identifier to preserve.</param>
    /// <returns>The guide definition.</returns>
    public static ListingsProviderInfo BuildListingsProviderInfo(ProviderOptions provider, string tunerId, string? existingId = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new ListingsProviderInfo
        {
            Id = existingId ?? string.Empty,
            Type = ProviderType,

            // ListingsId is the durable link back to the plugin's provider entry.
            ListingsId = provider.Id,
            Path = Catalog.CatalogService.ResolveEpgUrl(provider) ?? string.Empty,
            UserAgent = UserAgents.Resolve(provider.UserAgent),
            EnableAllTuners = false,
            EnabledTuners = new[] { tunerId },
            ChannelMappings = Array.Empty<NameValuePair>(),
            NewsCategories = _newsKeywords,
            SportsCategories = _sportsKeywords,
            KidsCategories = _kidsKeywords,
            MovieCategories = _movieKeywords,
            MoviePrefix = string.Empty,
            PreferredLanguage = string.Empty
        };
    }

    private ProviderOptions? ResolveProvider(ListingsProviderInfo info)
    {
        var provider = StreamFinPlugin.Instance?.FindProvider(info.ListingsId);
        if (provider is null)
        {
            _logger.LogDebug("No StreamFin provider matches guide {ListingsId}", info.ListingsId);
        }

        return provider;
    }

    private static ProgramInfo Convert(XmltvProgramme programme, string channelId)
    {
        var categories = programme.Categories ?? Array.Empty<string>();

        var isMovie = MatchesAny(categories, _movieKeywords);
        var isSeries = MatchesAny(categories, _seriesKeywords)
            || programme.SeasonNumber is not null
            || programme.EpisodeNumber is not null;

        return new ProgramInfo
        {
            // Jellyfin appends the channel id to this, but a provider can repeat a programme id
            // across channels, so include the slot to keep it unique.
            Id = string.Create(CultureInfo.InvariantCulture, $"{channelId}_{programme.StartUnix}"),
            ChannelId = channelId,
            Name = programme.Title ?? "Unknown",
            EpisodeTitle = programme.SubTitle,
            Overview = programme.Description,
            StartDate = programme.StartUtc,
            EndDate = programme.EndUtc,
            Genres = categories.ToList(),
            OfficialRating = programme.Rating,
            CommunityRating = programme.StarRating is null ? null : (float)programme.StarRating.Value,
            ImageUrl = programme.IconUrl,
            HasImage = !string.IsNullOrWhiteSpace(programme.IconUrl),
            SeasonNumber = programme.SeasonNumber,
            EpisodeNumber = programme.EpisodeNumber,
            ProductionYear = programme.Year,
            OriginalAirDate = programme.OriginalAirDate,
            IsRepeat = programme.IsRepeat && !programme.IsNew,
            IsLive = programme.IsLive,
            IsPremiere = programme.IsPremiere,
            IsMovie = isMovie,
            IsSeries = isSeries && !isMovie,
            IsNews = MatchesAny(categories, _newsKeywords),
            IsSports = MatchesAny(categories, _sportsKeywords),
            IsKids = MatchesAny(categories, _kidsKeywords),

            // A series needs a stable show identifier for Jellyfin to group recordings of it.
            SeriesId = isSeries && !isMovie ? BuildSeriesId(programme) : null,
            ShowId = isSeries && !isMovie ? BuildSeriesId(programme) : null
        };
    }

    private static string? BuildSeriesId(XmltvProgramme programme)
        => string.IsNullOrWhiteSpace(programme.Title) ? null : programme.Title.ToLowerInvariant();

    private static bool MatchesAny(IReadOnlyList<string> categories, string[] keywords)
    {
        foreach (var category in categories)
        {
            foreach (var keyword in keywords)
            {
                if (category.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
