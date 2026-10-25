using System;
using System.Globalization;
using Jellyfin.Plugin.StreamFin.Configuration;

namespace Jellyfin.Plugin.StreamFin.Xtream;

/// <summary>
/// Builds the API and playback URLs for an Xtream panel.
/// </summary>
public class XtreamUrlBuilder
{
    private readonly string _baseUrl;
    private readonly string _username;
    private readonly string _password;

    /// <summary>
    /// Initializes a new instance of the <see cref="XtreamUrlBuilder"/> class.
    /// </summary>
    /// <param name="provider">The provider whose credentials to use.</param>
    public XtreamUrlBuilder(ProviderOptions provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _baseUrl = provider.GetNormalizedServerUrl();
        _username = provider.Username ?? string.Empty;
        _password = provider.Password ?? string.Empty;
    }

    /// <summary>
    /// Gets the panel base address.
    /// </summary>
    public string BaseUrl => _baseUrl;

    /// <summary>
    /// Gets a value indicating whether enough detail is configured to build URLs.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl)
        && !string.IsNullOrWhiteSpace(_username)
        && !string.IsNullOrWhiteSpace(_password);

    /// <summary>
    /// Builds a <c>player_api.php</c> URL.
    /// </summary>
    /// <param name="action">The API action, or <c>null</c> for the account handshake.</param>
    /// <param name="parameterName">An extra query parameter name.</param>
    /// <param name="parameterValue">An extra query parameter value.</param>
    /// <returns>The absolute API URL.</returns>
    public string Api(string? action = null, string? parameterName = null, string? parameterValue = null)
    {
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{_baseUrl}/player_api.php?username={Uri.EscapeDataString(_username)}&password={Uri.EscapeDataString(_password)}");

        if (!string.IsNullOrEmpty(action))
        {
            url += "&action=" + Uri.EscapeDataString(action);
        }

        if (!string.IsNullOrEmpty(parameterName) && !string.IsNullOrEmpty(parameterValue))
        {
            url += "&" + Uri.EscapeDataString(parameterName) + "=" + Uri.EscapeDataString(parameterValue);
        }

        return url;
    }

    /// <summary>
    /// Builds the full XMLTV guide URL.
    /// </summary>
    /// <returns>The absolute guide URL.</returns>
    public string Xmltv() => string.Create(
        CultureInfo.InvariantCulture,
        $"{_baseUrl}/xmltv.php?username={Uri.EscapeDataString(_username)}&password={Uri.EscapeDataString(_password)}");

    /// <summary>
    /// Builds the M3U-plus playlist URL, used as a fallback when <c>player_api.php</c> is blocked.
    /// </summary>
    /// <param name="output">The requested output format.</param>
    /// <returns>The absolute playlist URL.</returns>
    public string Playlist(string output = "ts") => string.Create(
        CultureInfo.InvariantCulture,
        $"{_baseUrl}/get.php?username={Uri.EscapeDataString(_username)}&password={Uri.EscapeDataString(_password)}&type=m3u_plus&output={Uri.EscapeDataString(output)}");

    /// <summary>
    /// Builds a live channel playback URL.
    /// </summary>
    /// <param name="streamId">The channel's stream identifier.</param>
    /// <param name="format">The requested container.</param>
    /// <returns>The absolute stream URL.</returns>
    public string LiveStream(long streamId, StreamFormat format)
    {
        var extension = format == StreamFormat.Hls ? "m3u8" : "ts";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_baseUrl}/live/{Uri.EscapeDataString(_username)}/{Uri.EscapeDataString(_password)}/{streamId}.{extension}");
    }

    /// <summary>
    /// Builds a movie playback URL.
    /// </summary>
    /// <param name="streamId">The movie's stream identifier.</param>
    /// <param name="containerExtension">The container the panel serves, defaulting to <c>mp4</c>.</param>
    /// <returns>The absolute stream URL.</returns>
    public string MovieStream(long streamId, string? containerExtension)
    {
        var extension = NormalizeExtension(containerExtension, "mp4");
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_baseUrl}/movie/{Uri.EscapeDataString(_username)}/{Uri.EscapeDataString(_password)}/{streamId}.{extension}");
    }

    /// <summary>
    /// Builds an episode playback URL.
    /// </summary>
    /// <param name="episodeId">The episode's stream identifier.</param>
    /// <param name="containerExtension">The container the panel serves, defaulting to <c>mp4</c>.</param>
    /// <returns>The absolute stream URL.</returns>
    public string EpisodeStream(string episodeId, string? containerExtension)
    {
        var extension = NormalizeExtension(containerExtension, "mp4");
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_baseUrl}/series/{Uri.EscapeDataString(_username)}/{Uri.EscapeDataString(_password)}/{Uri.EscapeDataString(episodeId)}.{extension}");
    }

    /// <summary>
    /// Builds a catch-up playback URL.
    /// </summary>
    /// <param name="streamId">The channel's stream identifier.</param>
    /// <param name="startUtc">When the recording should start.</param>
    /// <param name="duration">How much to play back.</param>
    /// <returns>The absolute stream URL.</returns>
    public string TimeshiftStream(long streamId, DateTime startUtc, TimeSpan duration)
    {
        var minutes = Math.Max(1, (int)Math.Round(duration.TotalMinutes));
        var start = startUtc.ToString("yyyy-MM-dd:HH-mm", CultureInfo.InvariantCulture);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_baseUrl}/timeshift/{Uri.EscapeDataString(_username)}/{Uri.EscapeDataString(_password)}/{minutes}/{start}/{streamId}.ts");
    }

    /// <summary>
    /// Replaces the credentials in a log message or URL with placeholders.
    /// </summary>
    /// <param name="value">The text to redact.</param>
    /// <returns>The redacted text.</returns>
    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var result = value;
        if (!string.IsNullOrEmpty(_password))
        {
            result = result.Replace(_password, "***", StringComparison.Ordinal);
            result = result.Replace(Uri.EscapeDataString(_password), "***", StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(_username))
        {
            result = result.Replace(_username, "user", StringComparison.Ordinal);
            result = result.Replace(Uri.EscapeDataString(_username), "user", StringComparison.Ordinal);
        }

        return result;
    }

    private static string NormalizeExtension(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim().TrimStart('.');
        return trimmed.Length == 0 ? fallback : Uri.EscapeDataString(trimmed);
    }
}
