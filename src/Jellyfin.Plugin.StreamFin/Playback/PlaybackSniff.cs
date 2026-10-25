using System;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// Identifies a media body from its first bytes.
/// </summary>
/// <remarks>
/// Panels answer an unknown container with HTTP 200 and an HTML or JSON page, and they answer a
/// movie URL with MKV bytes even when the path says <c>.m3u8</c>. The path and the content type
/// are not enough.
/// </remarks>
internal static class PlaybackSniff
{
    /// <summary>
    /// Returns whether the body is an HLS playlist.
    /// </summary>
    /// <param name="prefix">The first bytes of the body.</param>
    /// <returns><c>true</c> when the body starts with <c>#EXTM3U</c>.</returns>
    public static bool IsPlaylist(ReadOnlySpan<byte> prefix)
        => Trim(prefix).StartsWith("#EXTM3U"u8);

    /// <summary>
    /// Returns whether the body is an HTML or JSON document.
    /// </summary>
    /// <param name="prefix">The first bytes of the body.</param>
    /// <returns><c>true</c> when the body is text the browser cannot play.</returns>
    public static bool IsTextDocument(ReadOnlySpan<byte> prefix)
    {
        var body = Trim(prefix);
        if (body.IsEmpty || IsPlaylist(prefix))
        {
            return false;
        }

        return body[0] is (byte)'<' or (byte)'{' or (byte)'[';
    }

    /// <summary>
    /// Returns whether the body is an MP4 file.
    /// </summary>
    /// <param name="prefix">The first bytes of the body.</param>
    /// <returns><c>true</c> when an <c>ftyp</c> box is present.</returns>
    public static bool IsMp4(ReadOnlySpan<byte> prefix)
    {
        var window = prefix.Length > 64 ? prefix[..64] : prefix;
        return window.IndexOf("ftyp"u8) >= 0;
    }

    /// <summary>
    /// Returns whether the body is WebM or Matroska.
    /// </summary>
    /// <param name="prefix">The first bytes of the body.</param>
    /// <returns><c>true</c> when the body starts with the EBML header.</returns>
    public static bool IsWebm(ReadOnlySpan<byte> prefix)
        => prefix.Length >= 4
            && prefix[0] == 0x1A
            && prefix[1] == 0x45
            && prefix[2] == 0xDF
            && prefix[3] == 0xA3;

    /// <summary>
    /// Returns whether the body is an MPEG transport stream.
    /// </summary>
    /// <param name="prefix">The first bytes of the body.</param>
    /// <returns><c>true</c> when the first byte is the sync byte.</returns>
    public static bool IsTs(ReadOnlySpan<byte> prefix)
        => prefix.Length > 0 && prefix[0] == 0x47;

    /// <summary>
    /// Returns whether the body is the container that was requested.
    /// </summary>
    /// <param name="container">The container the address asked for.</param>
    /// <param name="mediaType">The upstream content type, used when the bytes are ambiguous.</param>
    /// <param name="prefix">The first bytes of the body.</param>
    /// <returns><c>true</c> when the browser should receive this body.</returns>
    public static bool Accepts(string? container, string? mediaType, ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length == 0 || IsTextDocument(prefix))
        {
            return false;
        }

        var normalized = StreamUrlCandidates.NormalizeExtension(container);
        if (IsPlaylist(prefix))
        {
            return normalized is "m3u8" or "m3u";
        }

        return normalized switch
        {
            "mp4" or "m4v" or "mov" => IsMp4(prefix) || Contains(mediaType, "mp4") || Contains(mediaType, "quicktime"),
            "webm" => IsWebm(prefix) || Contains(mediaType, "webm"),
            "ts" or "mpegts" or "mpg" or "mpeg" => IsTs(prefix) || Contains(mediaType, "mp2t"),
            "m3u8" or "m3u" => false,
            _ => IsMp4(prefix) || IsWebm(prefix) || IsTs(prefix)
        };
    }

    /// <summary>
    /// Picks the content type to send for a body the browser accepted.
    /// </summary>
    /// <param name="container">The container that was requested.</param>
    /// <param name="prefix">The first bytes of the body.</param>
    /// <returns>A video content type.</returns>
    public static string ContentTypeFor(string? container, ReadOnlySpan<byte> prefix)
    {
        if (IsPlaylist(prefix))
        {
            return "application/vnd.apple.mpegurl";
        }

        if (IsWebm(prefix) || StreamUrlCandidates.NormalizeExtension(container) == "webm")
        {
            return "video/webm";
        }

        if (IsTs(prefix) || StreamUrlCandidates.NormalizeExtension(container) is "ts" or "mpegts")
        {
            return "video/mp2t";
        }

        return "video/mp4";
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
        {
            prefix = prefix[3..];
        }

        var index = 0;
        while (index < prefix.Length && prefix[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            index++;
        }

        return prefix[index..];
    }

    private static bool Contains(string? mediaType, string token)
        => mediaType is not null && mediaType.Contains(token, StringComparison.OrdinalIgnoreCase);
}
