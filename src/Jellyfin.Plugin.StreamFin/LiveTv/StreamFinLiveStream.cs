using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Catalog;
using Jellyfin.Plugin.StreamFin.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.LiveTv;

/// <summary>
/// A live channel handed to Jellyfin as a direct URL rather than a buffered copy.
/// </summary>
/// <remarks>
/// Jellyfin's built-in M3U tuner can pull the whole stream into a temp file so several clients can
/// share it. For IPTV that mostly buys disk churn and startup latency, because the provider is the
/// bottleneck and its connection limit is per account, not per Jellyfin client. Handing ffmpeg the
/// resolved URL instead means one hop fewer and a channel that starts as fast as the provider can
/// serve it.
/// </remarks>
public sealed class StreamFinLiveStream : ILiveStream
{
    private readonly ProviderOptions _provider;
    private readonly CatalogService _catalogService;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamFinLiveStream"/> class.
    /// </summary>
    /// <param name="mediaSource">The media source to serve.</param>
    /// <param name="tunerHostId">The owning tuner's identifier.</param>
    /// <param name="originalStreamId">The stream identifier Jellyfin assigned.</param>
    /// <param name="provider">The owning provider.</param>
    /// <param name="catalogService">The catalog service, used for its HTTP client.</param>
    /// <param name="logger">The logger.</param>
    public StreamFinLiveStream(
        MediaSourceInfo mediaSource,
        string tunerHostId,
        string originalStreamId,
        ProviderOptions provider,
        CatalogService catalogService,
        ILogger logger)
    {
        MediaSource = mediaSource;
        OriginalStreamId = originalStreamId;
        TunerHostId = tunerHostId;
        _provider = provider;
        _catalogService = catalogService;
        _logger = logger;

        ConsumerCount = 1;
        UniqueId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public int ConsumerCount { get; set; }

    /// <inheritdoc />
    public string OriginalStreamId { get; set; }

    /// <inheritdoc />
    public string TunerHostId { get; }

    /// <inheritdoc />
    public bool EnableStreamSharing => false;

    /// <inheritdoc />
    public MediaSourceInfo MediaSource { get; set; }

    /// <inheritdoc />
    public string UniqueId { get; }

    /// <summary>
    /// Gets when the stream was opened.
    /// </summary>
    public DateTime DateOpened { get; private set; }

    /// <inheritdoc />
    public async Task Open(CancellationToken openCancellationToken)
    {
        _cancellationTokenSource.Token.ThrowIfCancellationRequested();
        DateOpened = DateTime.UtcNow;

        var options = StreamFinPlugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!options.ResolveStreamRedirects || MediaSource.Protocol != MediaProtocol.Http)
        {
            return;
        }

        var resolved = await ResolveRedirectsAsync(MediaSource.Path, openCancellationToken).ConfigureAwait(false);
        if (resolved is not null && !string.Equals(resolved, MediaSource.Path, StringComparison.Ordinal))
        {
            _logger.LogDebug("{Provider}: resolved a channel to its edge node before handing it off", _provider.Name);
            MediaSource.Path = resolved;
        }
    }

    /// <inheritdoc />
    public Task Close()
    {
        _cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Stream GetStream()
        => throw new NotSupportedException("StreamFin serves live channels as a direct URL and has no shared buffer to read.");

    /// <inheritdoc />
    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// Follows the provider's redirect chain with HEAD requests.
    /// </summary>
    /// <remarks>
    /// Deliberately HEAD and never GET. A GET against the final URL would open a real stream that
    /// we then throw away, and because a provider's connection limit is counted per account, that
    /// abandoned stream can be exactly what makes the playback request fail. HEAD is answered by
    /// the same redirect logic without reserving a slot, and a panel that rejects HEAD simply
    /// means no resolution happens.
    /// </remarks>
    private async Task<string?> ResolveRedirectsAsync(string url, CancellationToken cancellationToken)
    {
        var client = _catalogService.CreateRedirectProbeClient();
        var current = url;

        // Panels chain two or three hops to reach a node; more than that is a redirect loop.
        for (var hop = 0; hop < 5; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, current);
            foreach (var header in Playback.PlaybackHeaders.Build(_provider))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            HttpResponseMessage response;
            try
            {
                response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                _logger.LogDebug("{Provider}: could not pre-resolve the stream URL: {Reason}", _provider.Name, ex.Message);
                return null;
            }

            using (response)
            {
                var isRedirect = response.StatusCode is HttpStatusCode.Moved
                    or HttpStatusCode.Found
                    or HttpStatusCode.TemporaryRedirect
                    or HttpStatusCode.PermanentRedirect
                    or HttpStatusCode.SeeOther;

                if (!isRedirect)
                {
                    if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
                    {
                        // The panel does not do HEAD. Nothing to resolve; ffmpeg will follow the
                        // redirect itself.
                        return null;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning(
                            "{Provider}: the channel URL answered HTTP {Status}. The line may be out of connections.",
                            _provider.Name,
                            (int)response.StatusCode);
                        return null;
                    }

                    return current;
                }

                var location = response.Headers.Location;
                if (location is null)
                {
                    return current;
                }

                current = location.IsAbsoluteUri
                    ? location.ToString()
                    : new Uri(new Uri(current), location).ToString();
            }
        }

        _logger.LogWarning("{Provider}: gave up following redirects for a channel", _provider.Name);
        return null;
    }
}
