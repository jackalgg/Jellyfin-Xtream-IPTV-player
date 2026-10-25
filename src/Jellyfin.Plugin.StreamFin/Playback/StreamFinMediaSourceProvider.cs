using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// Hands Jellyfin a playable <see cref="MediaSourceInfo"/> for StreamFin library items.
/// </summary>
/// <remarks>
/// The default strm source is a bare URL with no headers and no container fallback. Adding a
/// StreamFin source with those set is what makes movies and series start instead of dying in
/// ffprobe with "streams and format are both null".
/// </remarks>
public class StreamFinMediaSourceProvider : IMediaSourceProvider
{
    private readonly PlaybackLinkFactory _links;
    private readonly ILogger<StreamFinMediaSourceProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamFinMediaSourceProvider"/> class.
    /// </summary>
    /// <param name="links">The playback URL factory.</param>
    /// <param name="logger">The logger.</param>
    public StreamFinMediaSourceProvider(PlaybackLinkFactory links, ILogger<StreamFinMediaSourceProvider> logger)
    {
        _links = links;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IEnumerable<MediaSourceInfo>> GetMediaSources(BaseItem item, CancellationToken cancellationToken)
    {
        if (item is not Video)
        {
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var plugin = StreamFinPlugin.Instance;
        if (plugin is null || !IsStreamFinItem(item, plugin))
        {
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var strmPath = item.Path;
        if (string.IsNullOrWhiteSpace(strmPath) || !strmPath.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        string contents;
        try
        {
            contents = File.ReadAllText(strmPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug("Could not read {Path}: {Reason}", strmPath, ex.Message);
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var url = ReadStrmUrl(contents);
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var options = plugin.Configuration;
        var playUrl = url;
        Dictionary<string, string> headers;

        var parsed = PlaybackLinkFactory.TryParse(url);
        if (parsed is not null)
        {
            var provider = plugin.FindProvider(parsed.ProviderId);
            headers = provider is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : PlaybackHeaders.Build(provider);

            // Rebuild against the current listen port so a port change does not break playback.
            if (provider is not null && options.ProxyPlayback)
            {
                playUrl = _links.BuildPlayUrl(provider, parsed.Kind, parsed.StreamId, parsed.UpstreamUrl, parsed.Container);
            }
        }
        else
        {
            var owner = TryFindProvider(plugin, item);
            if (owner is not null && options.ProxyPlayback)
            {
                var kind = item is Episode ? PlaybackKind.Series : PlaybackKind.Movie;
                playUrl = _links.BuildPlayUrl(owner, kind, item.Id.ToString("N"), url, StreamUrlCandidates.GetExtension(url));
            }

            headers = owner is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["User-Agent"] = UserAgents.Default }
                : PlaybackHeaders.Build(owner);
        }

        var isHls = playUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);

        var source = new MediaSourceInfo
        {
            Id = "streamfin",
            Path = playUrl,
            Protocol = MediaProtocol.Http,
            IsRemote = true,
            SupportsDirectPlay = options.PreferDirectPlay && !isHls,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            SupportsProbing = !isHls,
            IgnoreDts = true,
            ReadAtNativeFramerate = false,
            RequiredHttpHeaders = headers,
            Name = "StreamFin",
            MediaStreams = new MediaStream[]
            {
                new MediaStream { Type = MediaStreamType.Video, Index = -1 },
                new MediaStream { Type = MediaStreamType.Audio, Index = -1 }
            }
        };

        if (item.RunTimeTicks is > 0)
        {
            source.RunTimeTicks = item.RunTimeTicks;
        }

        source.InferTotalBitrate();
        return Task.FromResult<IEnumerable<MediaSourceInfo>>(new[] { source });
    }

    /// <inheritdoc />
    public Task<ILiveStream> OpenMediaSource(
        string openToken,
        List<ILiveStream> currentLiveStreams,
        CancellationToken cancellationToken)
        => throw new FileNotFoundException($"StreamFin does not open live-stream tokens ({openToken}).");

    private static bool IsStreamFinItem(BaseItem item, StreamFinPlugin plugin)
    {
        var path = item.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var root = plugin.GetLibraryRoot();
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderOptions? TryFindProvider(StreamFinPlugin plugin, BaseItem item)
    {
        var path = item.Path ?? string.Empty;
        var root = plugin.GetLibraryRoot();

        foreach (var candidate in plugin.Configuration.Providers)
        {
            var providerRoot = Library.LibraryWriter.GetProviderRoot(candidate, plugin.Configuration);
            if (path.StartsWith(providerRoot, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(Path.Combine(root, Library.PathSanitizer.Sanitize(candidate.Name, candidate.Id)), StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ReadStrmUrl(string contents)
    {
        foreach (var raw in contents.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var pipe = line.IndexOf('|', StringComparison.Ordinal);
            return pipe > 0 ? line[..pipe].Trim() : line;
        }

        return null;
    }
}
