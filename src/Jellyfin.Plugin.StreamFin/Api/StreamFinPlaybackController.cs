using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Playback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    private readonly PlaybackService _playback;
    private readonly PlaybackLinkFactory _links;
    private readonly ILogger<StreamFinPlaybackController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamFinPlaybackController"/> class.
    /// </summary>
    /// <param name="playback">The playback service.</param>
    /// <param name="links">The playback URL factory.</param>
    /// <param name="logger">The logger.</param>
    public StreamFinPlaybackController(
        PlaybackService playback,
        PlaybackLinkFactory links,
        ILogger<StreamFinPlaybackController> logger)
    {
        _playback = playback;
        _links = links;
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
