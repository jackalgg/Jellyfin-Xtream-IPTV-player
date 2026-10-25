namespace Jellyfin.Plugin.StreamFin.Configuration;

/// <summary>
/// The kind of IPTV source a provider talks to.
/// </summary>
public enum ProviderKind
{
    /// <summary>
    /// An Xtream Codes compatible panel, queried through <c>player_api.php</c>.
    /// </summary>
    Xtream = 0,

    /// <summary>
    /// A plain M3U or M3U-plus playlist, optionally paired with an XMLTV guide.
    /// </summary>
    M3u = 1
}
