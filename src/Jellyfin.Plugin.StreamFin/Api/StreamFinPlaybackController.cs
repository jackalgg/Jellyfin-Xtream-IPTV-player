using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Playback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.StreamFin.Api;

/// <summary>
/// Serves movies, episodes, and (when needed) live channels through StreamFin so ffmpeg talks to
/// Jellyfin and StreamFin talks to the panel.
/// </summary>
/// <remarks>
/// This endpoint is anonymous on purpose: Jellyfin's strm reader and ffprobe open the URL with no
/// API key. The URL itself is HMAC-signed, so it cannot be used as an open proxy.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("StreamFin/Play")]
public class StreamFinPlaybackController : ControllerBase
{
    private const int PeekBytes = 1024;
    private const int MaxPlaylistBytes = 4 * 1024 * 1024;

    private readonly PlaybackService _playback;
    private readonly PlaybackLinkFactory _links;
    private readonly FfmpegRemuxer _remuxer;
    private readonly ILogger<StreamFinPlaybackController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamFinPlaybackController"/> class.
    /// </summary>
    /// <param name="playback">The playback service.</param>
    /// <param name="links">The playback URL factory.</param>
    /// <param name="remuxer">The remuxer used when the browser cannot play the file.</param>
    /// <param name="logger">The logger.</param>
    public StreamFinPlaybackController(
        PlaybackService playback,
        PlaybackLinkFactory links,
        FfmpegRemuxer remuxer,
        ILogger<StreamFinPlaybackController> logger)
    {
        _playback = playback;
        _links = links;
        _remuxer = remuxer;
        _logger = logger;
    }

    /// <summary>
    /// Opens a signed stream and copies it to the caller.
    /// </summary>
    /// <param name="kind">One of <c>movie</c>, <c>series</c>, <c>live</c>, or <c>url</c>.</param>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="id">The stream identifier within the provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The upstream media, or an error status.</returns>
    [HttpGet("{kind}/{providerId}/{id}")]
    [HttpHead("{kind}/{providerId}/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task Play(
        string kind,
        string providerId,
        string id,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(kind, ignoreCase: true, out PlaybackKind parsedKind))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var parsed = new ParsedPlayUrl
        {
            Kind = parsedKind,
            ProviderId = providerId,
            StreamId = id,
            Container = Request.Query["c"].FirstOrDefault(),
            UpstreamUrl = PlaybackLinkFactory.Decode(Request.Query["u"].FirstOrDefault()),
            Signature = Request.Query["s"].FirstOrDefault()
        };

        if (!_links.Verify(parsed))
        {
            _logger.LogWarning("Rejected a playback request with a bad signature for {Provider}/{Id}", providerId, id);
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var provider = StreamFinPlugin.Instance?.FindProvider(providerId);
        if (provider is null || !provider.Enabled)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var range = Request.Headers[HeaderNames.Range].FirstOrDefault();
        var headOnly = HttpMethods.IsHead(Request.Method);
        var browser = string.Equals(Request.Query["b"].FirstOrDefault(), "1", StringComparison.Ordinal);
        var remux = string.Equals(Request.Query["r"].FirstOrDefault(), "1", StringComparison.Ordinal);

        if (remux)
        {
            if (headOnly)
            {
                Response.StatusCode = StatusCodes.Status200OK;
                Response.ContentType = "video/mp2t";
                return;
            }

            if (string.IsNullOrWhiteSpace(parsed.UpstreamUrl))
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await _remuxer.WriteMpegTsAsync(provider, parsed.UpstreamUrl, Response, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (browser && !headOnly)
        {
            await PlayBrowserAsync(provider, parsed, range, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var upstream = await _playback
            .OpenAsync(provider, parsed.Kind, parsed.StreamId, parsed.Container, parsed.UpstreamUrl, range, headOnly, cancellationToken)
            .ConfigureAwait(false);

        if (upstream is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (headOnly)
        {
            CopyResponseHeaders(upstream, Response);
            Response.StatusCode = (int)upstream.StatusCode;
            return;
        }

        if (PlaybackService.IsHlsPlaylist(upstream))
        {
            var body = await upstream.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var playlistUrl = upstream.RequestMessage?.RequestUri?.ToString() ?? parsed.UpstreamUrl ?? string.Empty;
            var requestPath = Request.PathBase.ToUriComponent() + Request.Path.ToUriComponent();
            var rewritten = _playback.RewritePlaylist(provider, body, playlistUrl, requestPath);

            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "application/vnd.apple.mpegurl";
            await Response.WriteAsync(rewritten, cancellationToken).ConfigureAwait(false);
            return;
        }

        CopyResponseHeaders(upstream, Response);
        Response.StatusCode = (int)upstream.StatusCode;

        var stream = await upstream.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await stream.CopyToAsync(Response.Body, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PlayBrowserAsync(
        ProviderOptions provider,
        ParsedPlayUrl parsed,
        string? range,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parsed.UpstreamUrl))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var opened = await _playback
            .OpenDirectAsync(provider, parsed.UpstreamUrl, range, PlaybackService.BrowserHeaderTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (opened is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var upstream = opened.Response;
        opened.CancelBodyAfter(PlaybackService.BrowserHeaderTimeout);
        Stream body;
        try
        {
            body = await upstream.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await using (body.ConfigureAwait(false))
        {
            var prefix = new byte[PeekBytes];
            int read;
            try
            {
                read = await ReadPrefixAsync(body, prefix, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var sample = prefix.AsMemory(0, read);
            var mediaType = upstream.Content.Headers.ContentType?.MediaType;
            var partial = upstream.StatusCode == HttpStatusCode.PartialContent;
            var accepted = PlaybackSniff.IsPlaylist(sample.Span)
                || (partial && !PlaybackSniff.IsTextDocument(sample.Span) && read > 0)
                || PlaybackSniff.Accepts(parsed.Container, mediaType, sample.Span);
            if (!accepted)
            {
                _logger.LogDebug(
                    "{Provider}: browser rejected {Kind} {StreamId} because the body was not {Container}",
                    provider.Name,
                    parsed.Kind,
                    parsed.StreamId,
                    parsed.Container);
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            opened.CancelBodyAfter(Timeout.InfiniteTimeSpan);
            DisableBuffering(Response);

            if (PlaybackSniff.IsPlaylist(sample.Span))
            {
                var playlist = await ReadPlaylistAsync(body, sample, cancellationToken).ConfigureAwait(false);
                if (playlist is null)
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var playlistUrl = upstream.RequestMessage?.RequestUri?.ToString() ?? parsed.UpstreamUrl;
                var requestPath = Request.PathBase.ToUriComponent() + Request.Path.ToUriComponent();
                Response.StatusCode = StatusCodes.Status200OK;
                Response.ContentType = "application/vnd.apple.mpegurl";
                await Response.WriteAsync(
                    _playback.RewritePlaylist(provider, playlist, playlistUrl, requestPath),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            CopyResponseHeaders(upstream, Response);
            Response.ContentType = PlaybackSniff.ContentTypeFor(parsed.Container, sample.Span);
            Response.StatusCode = (int)upstream.StatusCode;
            await Response.Body.WriteAsync(sample, cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            await body.CopyToAsync(Response.Body, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> ReadPrefixAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            read += count;
            if (read >= 64)
            {
                break;
            }
        }

        return read;
    }

    private static async Task<string?> ReadPlaylistAsync(Stream stream, ReadOnlyMemory<byte> prefix, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        copy.Write(prefix.Span);
        var remaining = MaxPlaylistBytes - prefix.Length;
        var buffer = new byte[16 * 1024];
        while (remaining > 0)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            copy.Write(buffer, 0, count);
            remaining -= count;
        }

        if (remaining == 0)
        {
            var extra = new byte[1];
            if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) > 0)
            {
                return null;
            }
        }

        return Encoding.UTF8.GetString(copy.GetBuffer(), 0, (int)copy.Length);
    }

    private static void DisableBuffering(HttpResponse response)
    {
        var feature = response.HttpContext.Features.Get<IHttpResponseBodyFeature>();
        feature?.DisableBuffering();
    }

    private static void CopyResponseHeaders(HttpResponseMessage upstream, HttpResponse response)
    {
        var contentType = upstream.Content.Headers.ContentType?.ToString();
        response.ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;

        if (upstream.Content.Headers.ContentLength is long length)
        {
            response.ContentLength = length;
        }

        if (upstream.Content.Headers.ContentRange is { } range)
        {
            response.Headers[HeaderNames.ContentRange] = range.ToString();
        }

        if (upstream.Headers.AcceptRanges.Count > 0
            || upstream.StatusCode == HttpStatusCode.PartialContent
            || upstream.Content.Headers.ContentRange is not null)
        {
            response.Headers[HeaderNames.AcceptRanges] = "bytes";
        }

        foreach (var header in new[] { "X-Content-Duration", "Content-Duration" })
        {
            if (upstream.Headers.TryGetValues(header, out var values)
                || upstream.Content.Headers.TryGetValues(header, out values))
            {
                response.Headers[header] = values.ToArray();
            }
        }
    }
}
