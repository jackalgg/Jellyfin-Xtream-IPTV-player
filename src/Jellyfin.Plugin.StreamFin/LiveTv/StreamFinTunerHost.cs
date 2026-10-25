using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Catalog;
using Jellyfin.Plugin.StreamFin.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.LiveTv;

/// <summary>
/// Serves a provider's live channels to Jellyfin's Live TV.
/// </summary>
public class StreamFinTunerHost : ITunerHost, IConfigurableTunerHost
{
    /// <summary>
    /// The tuner type StreamFin registers under, which is what Jellyfin stores in
    /// <see cref="TunerHostInfo.Type"/>.
    /// </summary>
    public const string TunerType = "streamfin";

    private const string ChannelIdPrefix = "streamfin_";

    private readonly CatalogService _catalogService;
    private readonly IConfigurationManager _configurationManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ILogger<StreamFinTunerHost> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamFinTunerHost"/> class.
    /// </summary>
    /// <param name="catalogService">The catalog service.</param>
    /// <param name="configurationManager">The server configuration manager.</param>
    /// <param name="mediaSourceManager">The media source manager.</param>
    /// <param name="logger">The logger.</param>
    public StreamFinTunerHost(
        CatalogService catalogService,
        IConfigurationManager configurationManager,
        IMediaSourceManager mediaSourceManager,
        ILogger<StreamFinTunerHost> logger)
    {
        _catalogService = catalogService;
        _configurationManager = configurationManager;
        _mediaSourceManager = mediaSourceManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "StreamFin IPTV";

    /// <inheritdoc />
    public string Type => TunerType;

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public async Task<List<ChannelInfo>> GetChannels(bool enableCache, CancellationToken cancellationToken)
    {
        var channels = new List<ChannelInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (tuner, provider) in GetTuners())
        {
            if (!provider.EnableLiveTv)
            {
                continue;
            }

            IReadOnlyList<CatalogChannel> providerChannels;
            try
            {
                providerChannels = await _catalogService
                    .GetChannelsAsync(provider, enableCache, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One broken provider must not empty the guide for the others.
                _logger.LogError(ex, "Could not load channels for {Provider}", provider.Name);
                continue;
            }

            foreach (var channel in providerChannels)
            {
                var id = BuildChannelId(provider.Id, channel.Id);
                if (!seen.Add(id))
                {
                    continue;
                }

                channels.Add(new ChannelInfo
                {
                    Id = id,
                    TunerHostId = tuner.Id,
                    Name = channel.Name,
                    Number = channel.Number,
                    ChannelGroup = channel.Group,
                    ImageUrl = channel.LogoUrl,
                    HasImage = !string.IsNullOrWhiteSpace(channel.LogoUrl),
                    ChannelType = ChannelType.TV,
                    IsHD = channel.IsHd,

                    // Jellyfin matches guide data to a channel through TunerChannelId, so putting
                    // the provider's XMLTV id here is what makes the EPG line up with no manual
                    // channel mapping.
                    TunerChannelId = channel.EpgChannelId,
                    Path = channel.StreamUrl
                });
            }
        }

        _logger.LogDebug("Returning {Count} live channels", channels.Count);
        return channels;
    }

    /// <inheritdoc />
    public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
    {
        var resolved = await ResolveChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return new List<MediaSourceInfo>();
        }

        return new List<MediaSourceInfo> { BuildMediaSource(resolved.Value.Tuner, resolved.Value.Provider, resolved.Value.Channel) };
    }

    /// <inheritdoc />
    public async Task<ILiveStream> GetChannelStream(
        string channelId,
        string streamId,
        IList<ILiveStream> currentLiveStreams,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            throw new FileNotFoundException($"No StreamFin channel matches {channelId}.");
        }

        var (tuner, provider, channel) = resolved.Value;

        var limit = provider.MaxConcurrentStreams;
        if (limit > 0)
        {
            var open = currentLiveStreams.Count(s => string.Equals(s.TunerHostId, tuner.Id, StringComparison.OrdinalIgnoreCase));
            if (open >= limit)
            {
                throw new LiveTvConflictException(
                    $"{provider.Name} allows {limit} simultaneous stream(s) and they are all in use.");
            }
        }

        var mediaSource = BuildMediaSource(tuner, provider, channel);

        var stream = new StreamFinLiveStream(
            mediaSource,
            tuner.Id,
            streamId,
            provider,
            _catalogService,
            _logger);

        // Jellyfin's own tuner hosts open the stream inside GetChannelStream, and the Live TV
        // service relies on that having happened by the time it gets the stream back.
        await stream.Open(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Opened {Channel} from {Provider}", channel.Name, provider.Name);
        return stream;
    }

    /// <inheritdoc />
    public Task<List<TunerHostInfo>> DiscoverDevices(int discoveryDurationMs, CancellationToken cancellationToken)
    {
        // Jellyfin's tuner discovery scans the local network. StreamFin's tuners come from the
        // plugin's own settings, so surface each configured provider that has no tuner yet.
        var existing = GetTuners().Select(t => t.Tuner.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var discovered = CatalogService.GetEnabledProviders()
            .Where(p => p.EnableLiveTv && !existing.Contains(p.Id))
            .Select(p => BuildTunerHostInfo(p))
            .ToList();

        return Task.FromResult(discovered);
    }

    /// <inheritdoc />
    public async Task Validate(TunerHostInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var provider = StreamFinPlugin.Instance?.FindProvider(info.DeviceId)
            ?? throw new InvalidOperationException(
                "This tuner is not linked to a StreamFin provider. Add the provider on the StreamFin settings page first.");

        var channels = await _catalogService.GetChannelsAsync(provider, useCache: false, CancellationToken.None).ConfigureAwait(false);
        if (channels.Count == 0)
        {
            throw new InvalidOperationException(
                $"{provider.Name} answered but offered no live channels. Check the live category filter on the StreamFin settings page.");
        }
    }

    /// <summary>
    /// Builds the <see cref="TunerHostInfo"/> that represents one provider.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="existingId">An existing tuner identifier to preserve.</param>
    /// <returns>The tuner definition.</returns>
    public static TunerHostInfo BuildTunerHostInfo(ProviderOptions provider, string? existingId = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new TunerHostInfo
        {
            Id = existingId ?? string.Empty,
            Type = TunerType,

            // DeviceId is the durable link back to the plugin's provider entry; Jellyfin assigns
            // the tuner's own Id and we must not fight it.
            DeviceId = provider.Id,
            FriendlyName = provider.Name,
            Url = provider.Kind == ProviderKind.Xtream ? provider.GetNormalizedServerUrl() : provider.PlaylistUrl,
            UserAgent = UserAgents.Resolve(provider.UserAgent),
            TunerCount = provider.MaxConcurrentStreams,
            AllowHWTranscoding = true,
            AllowStreamSharing = false,
            AllowFmp4TranscodingContainer = false,
            EnableStreamLooping = false,
            IgnoreDts = true,
            ReadAtNativeFramerate = false,
            ImportFavoritesOnly = false
        };
    }

    /// <summary>
    /// Builds the channel identifier used throughout Live TV.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="channelId">The channel identifier within the provider.</param>
    /// <returns>The channel identifier.</returns>
    public static string BuildChannelId(string providerId, string channelId)
        => string.Create(CultureInfo.InvariantCulture, $"{ChannelIdPrefix}{providerId}_{channelId}");

    private IReadOnlyList<(TunerHostInfo Tuner, ProviderOptions Provider)> GetTuners()
    {
        var plugin = StreamFinPlugin.Instance;
        if (plugin is null)
        {
            return Array.Empty<(TunerHostInfo, ProviderOptions)>();
        }

        var liveTvOptions = _configurationManager.GetConfiguration<LiveTvOptions>("livetv");

        return (liveTvOptions.TunerHosts ?? Array.Empty<TunerHostInfo>())
            .Where(t => string.Equals(t.Type, TunerType, StringComparison.OrdinalIgnoreCase))
            .Select(t => (Tuner: t, Provider: plugin.FindProvider(t.DeviceId)))
            .Where(pair => pair.Provider is not null && pair.Provider.Enabled)
            .Select(pair => (pair.Tuner, pair.Provider!))
            .ToArray();
    }

    private async Task<(TunerHostInfo Tuner, ProviderOptions Provider, CatalogChannel Channel)?> ResolveChannelAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(channelId) || !channelId.StartsWith(ChannelIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var (tuner, provider) in GetTuners())
        {
            var prefix = ChannelIdPrefix + provider.Id + "_";
            if (!channelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var innerId = channelId[prefix.Length..];
            var channels = await _catalogService.GetChannelsAsync(provider, useCache: true, cancellationToken).ConfigureAwait(false);
            var channel = channels.FirstOrDefault(c => string.Equals(c.Id, innerId, StringComparison.OrdinalIgnoreCase));

            if (channel is not null)
            {
                return (tuner, provider, channel);
            }
        }

        return null;
    }

    private MediaSourceInfo BuildMediaSource(TunerHostInfo tuner, ProviderOptions provider, CatalogChannel channel)
    {
        var protocol = _mediaSourceManager.GetPathProtocol(channel.StreamUrl);
        var isHls = channel.StreamUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            || provider.LiveStreamFormat == StreamFormat.Hls;

        var headers = protocol == MediaProtocol.Http
            ? Playback.PlaybackHeaders.Build(provider)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var mediaSource = new MediaSourceInfo
        {
            Id = BuildChannelId(provider.Id, channel.Id),
            Path = channel.StreamUrl,
            Protocol = protocol,
            MediaStreams = new MediaStream[]
            {
                // The index is -1 because the container has not been probed yet; ffmpeg picks the
                // real streams when it opens the URL.
                new MediaStream { Type = MediaStreamType.Video, Index = -1, IsInterlaced = true },
                new MediaStream { Type = MediaStreamType.Audio, Index = -1 }
            },
            RequiresOpening = true,
            RequiresClosing = true,
            RequiresLooping = false,
            IsInfiniteStream = true,
            IsRemote = true,
            SupportsDirectPlay = false,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            SupportsProbing = !isHls,
            IgnoreDts = true,
            ReadAtNativeFramerate = false,
            RequiredHttpHeaders = headers,
            UseMostCompatibleTranscodingProfile = !tuner.AllowFmp4TranscodingContainer,
            FallbackMaxStreamingBitrate = tuner.FallbackMaxStreamingBitrate,
            Name = channel.Name
        };

        mediaSource.InferTotalBitrate();
        return mediaSource;
    }
}
