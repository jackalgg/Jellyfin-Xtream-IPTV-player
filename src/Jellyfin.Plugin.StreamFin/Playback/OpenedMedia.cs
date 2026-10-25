using System;
using System.Net.Http;
using System.Threading;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// An upstream response whose header timeout stays alive until the body is finished.
/// </summary>
public sealed class OpenedMedia : IDisposable
{
    private readonly CancellationTokenSource _lifetime;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenedMedia"/> class.
    /// </summary>
    /// <param name="response">The upstream response.</param>
    /// <param name="lifetime">The token source that cancels the response body.</param>
    public OpenedMedia(HttpResponseMessage response, CancellationTokenSource lifetime)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(lifetime);
        Response = response;
        _lifetime = lifetime;
    }

    /// <summary>
    /// Gets the upstream response.
    /// </summary>
    public HttpResponseMessage Response { get; }

    /// <summary>
    /// Limits how long the body may stall. <see cref="Timeout.InfiniteTimeSpan"/> keeps it open.
    /// </summary>
    /// <param name="timeout">The stall limit.</param>
    public void CancelBodyAfter(TimeSpan timeout) => _lifetime.CancelAfter(timeout);

    /// <inheritdoc />
    public void Dispose()
    {
        Response.Dispose();
        _lifetime.Dispose();
    }
}
