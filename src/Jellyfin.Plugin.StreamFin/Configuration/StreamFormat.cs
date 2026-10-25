namespace Jellyfin.Plugin.StreamFin.Configuration;

/// <summary>
/// The container a provider should serve live streams in.
/// </summary>
public enum StreamFormat
{
    /// <summary>
    /// MPEG-TS, requested as <c>/live/user/pass/1234.ts</c>. The most widely supported option.
    /// </summary>
    MpegTs = 0,

    /// <summary>
    /// HLS, requested as <c>/live/user/pass/1234.m3u8</c>. Lower latency on panels that support it.
    /// </summary>
    Hls = 1
}
