namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// The kind of stream a playback request is for.
/// </summary>
public enum PlaybackKind
{
    /// <summary>
    /// An Xtream or playlist movie.
    /// </summary>
    Movie = 0,

    /// <summary>
    /// A series episode.
    /// </summary>
    Series = 1,

    /// <summary>
    /// A live channel.
    /// </summary>
    Live = 2,

    /// <summary>
    /// An already-absolute URL, used for M3U entries and <c>direct_source</c> streams.
    /// </summary>
    Url = 3
}
