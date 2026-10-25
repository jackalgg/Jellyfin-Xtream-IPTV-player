using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Xtream.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Xtream;

/// <summary>
/// Talks to an Xtream Codes panel.
/// </summary>
public class XtreamClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="XtreamClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to issue requests with.</param>
    /// <param name="logger">The logger.</param>
    public XtreamClient(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Fetches the account handshake and verifies the credentials work.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The account details.</returns>
    /// <exception cref="XtreamApiException">The panel rejected the credentials or sent an unusable reply.</exception>
    public async Task<XtreamAccount> GetAccountAsync(ProviderOptions provider, CancellationToken cancellationToken)
    {
        var urls = new XtreamUrlBuilder(provider);
        if (!urls.IsConfigured)
        {
            throw new XtreamApiException("Server address, username, and password are all required.");
        }

        var account = await GetAsync<XtreamAccount>(provider, urls.Api(), cancellationToken).ConfigureAwait(false);

        if (account?.UserInfo is null)
        {
            throw new XtreamApiException(
                "The server replied, but not with an Xtream account. Check that the address points at the panel root and not at a playlist URL.");
        }

        if (!account.UserInfo.Authenticated)
        {
            throw new XtreamApiException("The panel rejected these credentials.");
        }

        if (!account.UserInfo.IsActive)
        {
            var status = account.UserInfo.Status ?? "inactive";
            var expiry = account.UserInfo.ExpiresAtUtc is null
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" (expired {account.UserInfo.ExpiresAtUtc:yyyy-MM-dd})");
            throw new XtreamApiException($"The credentials are valid but the line is {status}{expiry}.");
        }

        return account;
    }

    /// <summary>
    /// Fetches the live channel categories.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The categories.</returns>
    public Task<IReadOnlyList<XtreamCategory>> GetLiveCategoriesAsync(ProviderOptions provider, CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>(provider, "get_live_categories", null, null, cancellationToken);

    /// <summary>
    /// Fetches the movie categories.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The categories.</returns>
    public Task<IReadOnlyList<XtreamCategory>> GetVodCategoriesAsync(ProviderOptions provider, CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>(provider, "get_vod_categories", null, null, cancellationToken);

    /// <summary>
    /// Fetches the series categories.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The categories.</returns>
    public Task<IReadOnlyList<XtreamCategory>> GetSeriesCategoriesAsync(ProviderOptions provider, CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>(provider, "get_series_categories", null, null, cancellationToken);

    /// <summary>
    /// Fetches live channels, optionally limited to one category.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="categoryId">The category to limit to, or <c>null</c> for all of them.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The channels.</returns>
    public Task<IReadOnlyList<XtreamLiveStream>> GetLiveStreamsAsync(ProviderOptions provider, string? categoryId, CancellationToken cancellationToken)
        => GetListAsync<XtreamLiveStream>(provider, "get_live_streams", categoryId is null ? null : "category_id", categoryId, cancellationToken);

    /// <summary>
    /// Fetches movies, optionally limited to one category.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="categoryId">The category to limit to, or <c>null</c> for all of them.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The movies.</returns>
    public Task<IReadOnlyList<XtreamVodStream>> GetVodStreamsAsync(ProviderOptions provider, string? categoryId, CancellationToken cancellationToken)
        => GetListAsync<XtreamVodStream>(provider, "get_vod_streams", categoryId is null ? null : "category_id", categoryId, cancellationToken);

    /// <summary>
    /// Fetches series, optionally limited to one category.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="categoryId">The category to limit to, or <c>null</c> for all of them.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The series.</returns>
    public Task<IReadOnlyList<XtreamSeries>> GetSeriesAsync(ProviderOptions provider, string? categoryId, CancellationToken cancellationToken)
        => GetListAsync<XtreamSeries>(provider, "get_series", categoryId is null ? null : "category_id", categoryId, cancellationToken);

    /// <summary>
    /// Fetches one movie's full metadata.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="vodId">The movie's stream identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The movie detail, or <c>null</c> when the panel has none.</returns>
    public async Task<XtreamVodDetail?> GetVodInfoAsync(ProviderOptions provider, long vodId, CancellationToken cancellationToken)
    {
        var urls = new XtreamUrlBuilder(provider);
        var url = urls.Api("get_vod_info", "vod_id", vodId.ToString(CultureInfo.InvariantCulture));
        return await GetAsync<XtreamVodDetail>(provider, url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches one series' seasons and episodes.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The series detail, or <c>null</c> when the panel has none.</returns>
    public async Task<XtreamSeriesDetail?> GetSeriesInfoAsync(ProviderOptions provider, long seriesId, CancellationToken cancellationToken)
    {
        var urls = new XtreamUrlBuilder(provider);
        var url = urls.Api("get_series_info", "series_id", seriesId.ToString(CultureInfo.InvariantCulture));
        return await GetAsync<XtreamSeriesDetail>(provider, url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the provider's XMLTV guide for streaming.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="url">The guide address.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response stream, which the caller owns.</returns>
    public async Task<Stream> OpenStreamAsync(ProviderOptions provider, string url, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(provider, url);
        var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new ResponseOwningStream(response, stream);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(
        ProviderOptions provider,
        string action,
        string? parameterName,
        string? parameterValue,
        CancellationToken cancellationToken)
    {
        var urls = new XtreamUrlBuilder(provider);
        var url = urls.Api(action, parameterName, parameterValue);
        var result = await GetAsync<List<T>>(provider, url, cancellationToken).ConfigureAwait(false);
        return result ?? (IReadOnlyList<T>)Array.Empty<T>();
    }

    private async Task<T?> GetAsync<T>(ProviderOptions provider, string url, CancellationToken cancellationToken)
    {
        var urls = new XtreamUrlBuilder(provider);
        var safeUrl = urls.Redact(url);

        using var request = CreateRequest(provider, url);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new XtreamApiException($"Could not reach {urls.BaseUrl}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new XtreamApiException($"{urls.BaseUrl} did not respond in time.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new XtreamApiException($"{urls.BaseUrl} refused the request ({(int)response.StatusCode}). The credentials or the line's allowed IP may be wrong.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new XtreamApiException($"{urls.BaseUrl} returned HTTP {(int)response.StatusCode} for {Describe(url)}.");
            }

            var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (payload.Length == 0)
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(payload, _jsonOptions);
            }
            catch (JsonException ex)
            {
                // Panels under load answer with an HTML error page or a PHP warning followed by
                // JSON. Salvage the JSON body if one is in there before giving up.
                var salvaged = TryDeserializeEmbeddedJson<T>(payload);
                if (salvaged is not null)
                {
                    _logger.LogWarning("Recovered embedded JSON from a malformed reply to {Url}", safeUrl);
                    return salvaged;
                }

                var preview = Preview(payload);
                _logger.LogDebug(ex, "Unparseable reply from {Url}: {Preview}", safeUrl, preview);
                throw new XtreamApiException($"{urls.BaseUrl} answered {Describe(url)} with something that is not JSON: {preview}", ex);
            }
        }
    }

    private static T? TryDeserializeEmbeddedJson<T>(byte[] payload)
    {
        var text = Encoding.UTF8.GetString(payload);
        var starts = new[] { text.IndexOf('{', StringComparison.Ordinal), text.IndexOf('[', StringComparison.Ordinal) };
        var start = -1;
        foreach (var candidate in starts)
        {
            if (candidate >= 0 && (start < 0 || candidate < start))
            {
                start = candidate;
            }
        }

        if (start < 0)
        {
            return default;
        }

        var end = Math.Max(text.LastIndexOf('}'), text.LastIndexOf(']'));
        if (end <= start)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text.AsSpan(start, end - start + 1), _jsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string Describe(string url)
    {
        var actionIndex = url.IndexOf("action=", StringComparison.OrdinalIgnoreCase);
        if (actionIndex < 0)
        {
            return "the account handshake";
        }

        var action = url[(actionIndex + "action=".Length)..];
        var ampersand = action.IndexOf('&', StringComparison.Ordinal);
        return ampersand < 0 ? action : action[..ampersand];
    }

    private static string Preview(byte[] payload)
    {
        var text = Encoding.UTF8.GetString(payload, 0, Math.Min(payload.Length, 200))
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();

        return text.Length == 0 ? "an empty body" : text;
    }

    private static HttpRequestMessage CreateRequest(ProviderOptions provider, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgents.Resolve(provider.UserAgent));
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        return request;
    }

    /// <summary>
    /// Keeps an <see cref="HttpResponseMessage"/> alive for as long as its content stream is in use.
    /// </summary>
    private sealed class ResponseOwningStream : Stream
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _inner;

        public ResponseOwningStream(HttpResponseMessage response, Stream inner)
        {
            _response = response;
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            _response.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
