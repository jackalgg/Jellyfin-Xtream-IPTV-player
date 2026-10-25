namespace Jellyfin.Plugin.StreamFin;

/// <summary>
/// Supplies the User-Agent sent to providers.
/// </summary>
public static class UserAgents
{
    /// <summary>
    /// The agent used when a provider does not specify one.
    /// </summary>
    /// <remarks>
    /// Panels commonly allowlist the agents their official apps send and answer anything else with
    /// a 403 or an HTML block page, so posing as a desktop browser is the safer default.
    /// </remarks>
    public const string Default = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    /// <summary>
    /// The agent some panels require to serve VOD, matching the common set-top box clients.
    /// </summary>
    public const string Vlc = "VLC/3.0.20 LibVLC/3.0.20";

    /// <summary>
    /// Returns the configured agent, or <see cref="Default"/> when none is configured.
    /// </summary>
    /// <param name="configured">The configured agent.</param>
    /// <returns>The agent to send.</returns>
    public static string Resolve(string? configured)
        => string.IsNullOrWhiteSpace(configured) ? Default : configured.Trim();
}
