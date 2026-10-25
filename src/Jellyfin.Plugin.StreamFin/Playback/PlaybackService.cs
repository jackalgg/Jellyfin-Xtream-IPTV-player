using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Catalog;
using Jellyfin.Plugin.StreamFin.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// Opens a provider stream the way a regular IPTV player does: the right headers, a walk of
/// container extensions, and redirect following.
/// </summary>
public class PlaybackService
{
    /// <summary>
    /// The name of the HTTP client used to pull media from the provider.
    /// </summary>
    public const string HttpClientName = "StreamFinPlayback";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PlaybackLinkFactory _links;
    private readonly ILogger<PlaybackService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="links">The playback URL factory.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackService(
        IHttpClientFactory httpClientFactory,
        PlaybackLinkFactory links,
        ILogger<PlaybackService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _links = links;
        _logger = logger;
    }

    /// <summary>
    /// Opens the first candidate URL that the provider will actually serve.
    /// </summary>
    /// <param name="provider">The owning provider.</param>
    /// <param name="kind">The stream kind.</param>
    /// <param name="streamId">The stream identifier within the provider.</param>
    /// <param name="preferredContainer">The advertised container extension.</param>
    /// <param name="upstreamUrl">The original upstream address.</param>
    /// <param name="rangeHeader">The client's Range header, when seeking.</param>
    /// <param name="headOnly">Whether to issue HEAD rather than GET.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The upstream response, which the caller owns, or <c>null</c> when none answered.</returns>
    public async Task<HttpResponseMessage?> OpenAsync(
        ProviderOptions provider,
        PlaybackKind kind,
        string streamId,
        string? preferredContainer,
        string? upstreamUrl,
        string? rangeHeader,
        bool headOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var candidates = StreamUrlCandidates.Build(provider, kind, streamId, preferredContainer, upstreamUrl);
        if (candidates.Count == 0)
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        Exception? lastError = null;

        foreach (var url in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage? response = null;
            try
            {
                response = await SendAsync(client, provider, url, rangeHeader, headOnly, cancellationToken)
                    .ConfigureAwait(false);

                if (IsUsable(response))
                {
                    if (candidates.Count > 1 && !string.Equals(url, candidates[0], StringComparison.Ordinal))
                    {
                        _logger.LogInformation(
                            "{Provider}: {Kind} {StreamId} is served as {Url} after the advertised address failed",
                            provider.Name,
                            kind,
                            streamId,
                            Redact(provider, url));
                    }

                    return response;
                }

                _logger.LogDebug(
                    "{Provider}: {Kind} {StreamId} rejected {Url} with HTTP {Status}",
                    provider.Name,
                    kind,
                    streamId,
                    Redact(provider, url),
                    (int)response.StatusCode);

                response.Dispose();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                response?.Dispose();
                lastError = ex;
                _logger.LogDebug(
                    "{Provider}: {Kind} {StreamId} could not open {Url}: {Reason}",
                    provider.Name,
                    kind,
                    streamId,
                    Redact(provider, url),
                    ex.Message);
            }
        }

        if (lastError is not null)
        {
            _logger.LogWarning(
                lastError,
                "{Provider}: every candidate failed for {Kind} {StreamId}",
                provider.Name,
                kind,
                streamId);
        }

        return null;
    }

    /// <summary>
    /// How long a browser playback request waits for the panel to send headers.
    /// </summary>
    /// <remarks>
    /// A panel that does not have the requested container often never answers. The browser tries
    /// the next address, so this stays short. Library playback does not use it.
    /// </remarks>
    public static readonly TimeSpan BrowserHeaderTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Opens one upstream address and does not try any other container.
    /// </summary>
    /// <param name="provider">The owning provider.</param>
    /// <param name="url">The exact address to open.</param>
    /// <param name="rangeHeader">The client's Range header, when seeking.</param>
    /// <param name="headerTimeout">How long to wait for response headers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The upstream response, which the caller owns, or <c>null</c> when it did not answer.</returns>
    public async Task<OpenedMedia?> OpenDirectAsync(
        ProviderOptions provider,
        string url,
        string? rangeHeader,
        TimeSpan headerTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!BrowseListing.IsAbsoluteHttp(url))
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(headerTimeout);
        try
        {
            var response = await SendAsync(client, provider, url, rangeHeader, headOnly: false, timeout.Token)
                .ConfigureAwait(false);
            if (!IsUsable(response))
            {
                response.Dispose();
                timeout.Dispose();
                _logger.LogDebug(
                    "{Provider} rejected a direct play of {Url} with HTTP {Status}",
                    provider.Name,
                    Redact(provider, url),
                    (int)response.StatusCode);
                return null;
            }

            // Headers arrived. Keep the body tied to the caller's cancellation, not the header timer.
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
            return new OpenedMedia(response, timeout);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timeout.Dispose();
            _logger.LogDebug("{Provider} did not send headers for {Url} before the browser timeout", provider.Name, Redact(provider, url));
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            timeout.Dispose();
            _logger.LogDebug(
                "{Provider} could not open {Url}: {Reason}",
                provider.Name,
                Redact(provider, url),
                ex.GetType().Name);
            return null;
        }
        catch
        {
            timeout.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Rewrites an HLS playlist so every segment also goes through StreamFin.
    /// </summary>
    /// <param name="provider">The owning provider.</param>
    /// <param name="playlist">The playlist body.</param>
    /// <param name="playlistUrl">The address the playlist was fetched from, used to resolve relative URIs.</param>
    /// <param name="requestPath">The path of this playlist request, including any base path, such as <c>/StreamFin/Play/live/provider/1</c>. Segment links are written relative to it so they stay on whatever host served the playlist.</param>
    /// <returns>The rewritten playlist.</returns>
    public string RewritePlaylist(ProviderOptions provider, string playlist, string playlistUrl, string? requestPath)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(playlist);

        if (!Uri.TryCreate(playlistUrl, UriKind.Absolute, out var baseUri))
        {
            return playlist;
        }

        var builder = new StringBuilder(playlist.Length + 256);
        using var reader = new StringReader(playlist);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                builder.Append('\n');
                continue;
            }

            if (line.StartsWith('#'))
            {
                builder.Append(RewriteTagUris(provider, baseUri, requestPath, line));
                builder.Append('\n');
                continue;
            }

            builder.Append(ProxyMediaUrl(provider, baseUri, requestPath, line.Trim()));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private string RewriteTagUris(ProviderOptions provider, Uri baseUri, string? requestPath, string line)
    {
        const string Marker = "URI=\"";
        var searchFrom = 0;
        var result = new StringBuilder(line.Length + 64);

        while (true)
        {
            var start = line.IndexOf(Marker, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                result.Append(line.AsSpan(searchFrom));
                return result.ToString();
            }

            var valueStart = start + Marker.Length;
            var end = line.IndexOf('"', valueStart);
            if (end < 0)
            {
                result.Append(line.AsSpan(searchFrom));
                return result.ToString();
            }

            result.Append(line.AsSpan(searchFrom, start - searchFrom));
            result.Append(line.AsSpan(start, Marker.Length));
            result.Append(ProxyMediaUrl(provider, baseUri, requestPath, line[valueStart..end]));
            result.Append('"');
            searchFrom = end + 1;
        }
    }

    private string ProxyMediaUrl(ProviderOptions provider, Uri baseUri, string? requestPath, string reference)
    {
        if (reference.Contains(PlaybackLinkFactory.PlayPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return reference;
        }

        var absolute = Uri.TryCreate(baseUri, reference, out var resolved)
            ? resolved.ToString()
            : reference;

        var playPath = _links.BuildRelativePlayUrl(
            provider,
            PlaybackKind.Url,
            "x",
            absolute,
            StreamUrlCandidates.GetExtension(absolute));

        if (string.IsNullOrEmpty(requestPath))
        {
            return playPath;
        }

        return PlaylistPaths.RelativeTo(requestPath, playPath);
    }

    /// <summary>
    /// Returns whether a response looks like an HLS playlist.
    /// </summary>
    /// <param name="response">The upstream response.</param>
    /// <returns><c>true</c> when the body should be treated as a playlist.</returns>
    public static bool IsHlsPlaylist(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null
            && (mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("m3u8", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var url = response.RequestMessage?.RequestUri?.AbsolutePath;
        return url is not null && url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        ProviderOptions provider,
        string url,
        string? rangeHeader,
        bool headOnly,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(headOnly ? HttpMethod.Head : HttpMethod.Get, url);

        foreach (var header in PlaybackHeaders.Build(provider))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (!string.IsNullOrWhiteSpace(rangeHeader))
        {
            request.Headers.TryAddWithoutValidation("Range", rangeHeader);
        }

        return await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsUsable(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode
            || response.StatusCode == HttpStatusCode.PartialContent)
        {
            return true;
        }

        // A panel that rejects HEAD still has the file; the GET that follows will work.
        return response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented;
    }

    private static string Redact(ProviderOptions provider, string url)
    {
        var builder = new Xtream.XtreamUrlBuilder(provider);
        return builder.IsConfigured ? builder.Redact(url) : url;
    }
}
