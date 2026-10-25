using System;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Plugin.StreamFin.Configuration;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// Builds the ffmpeg argument list that turns one provider file into MPEG-TS.
/// </summary>
internal static class FfmpegRemuxArguments
{
    /// <summary>
    /// Arguments that copy the video and encode the audio as AAC.
    /// </summary>
    /// <param name="provider">The provider the file belongs to.</param>
    /// <param name="sourceUrl">The file address.</param>
    /// <returns>The argument list, with the address as its own argument.</returns>
    public static IReadOnlyList<string> Copy(ProviderOptions provider, string sourceUrl)
    {
        var args = Begin(provider, sourceUrl);
        args.Add("-map");
        args.Add("0:v:0");
        args.Add("-map");
        args.Add("0:a:0?");
        args.Add("-dn");
        args.Add("-sn");
        args.Add("-c:v");
        args.Add("copy");
        args.Add("-c:a");
        args.Add("aac");
        args.Add("-ac");
        args.Add("2");
        args.Add("-b:a");
        args.Add("128k");
        AddOutput(args);
        return args;
    }

    /// <summary>
    /// Arguments that transcode the video so a browser can play it.
    /// </summary>
    /// <param name="provider">The provider the file belongs to.</param>
    /// <param name="sourceUrl">The file address.</param>
    /// <returns>The argument list, with the address as its own argument.</returns>
    public static IReadOnlyList<string> Transcode(ProviderOptions provider, string sourceUrl)
    {
        var args = Begin(provider, sourceUrl);
        args.Add("-map");
        args.Add("0:v:0");
        args.Add("-map");
        args.Add("0:a:0?");
        args.Add("-dn");
        args.Add("-sn");
        args.Add("-c:v");
        args.Add("libx264");
        args.Add("-preset");
        args.Add("ultrafast");
        args.Add("-tune");
        args.Add("zerolatency");
        args.Add("-pix_fmt");
        args.Add("yuv420p");
        args.Add("-c:a");
        args.Add("aac");
        args.Add("-ac");
        args.Add("2");
        args.Add("-b:a");
        args.Add("128k");
        AddOutput(args);
        return args;
    }

    private static List<string> Begin(ProviderOptions provider, string sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUrl);

        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel",
            "error",
            "-nostdin"
        };

        var headers = PlaybackHeaders.Build(provider);
        if (headers.TryGetValue("User-Agent", out var userAgent) && !string.IsNullOrWhiteSpace(userAgent))
        {
            args.Add("-user_agent");
            args.Add(Sanitize(userAgent));
        }

        var headerBlock = new StringBuilder();
        if (headers.TryGetValue("Referer", out var referer) && !string.IsNullOrWhiteSpace(referer))
        {
            headerBlock.Append("Referer: ").Append(Sanitize(referer)).Append("\r\n");
        }

        if (headers.TryGetValue("Origin", out var origin) && !string.IsNullOrWhiteSpace(origin))
        {
            headerBlock.Append("Origin: ").Append(Sanitize(origin)).Append("\r\n");
        }

        if (headerBlock.Length > 0)
        {
            args.Add("-headers");
            args.Add(headerBlock.ToString());
        }

        args.Add("-probesize");
        args.Add("1000000");
        args.Add("-analyzeduration");
        args.Add("1000000");
        args.Add("-seekable");
        args.Add("0");
        args.Add("-i");
        args.Add(sourceUrl);
        return args;
    }

    private static void AddOutput(List<string> args)
    {
        args.Add("-f");
        args.Add("mpegts");
        args.Add("-muxdelay");
        args.Add("0");
        args.Add("-muxpreload");
        args.Add("0");
        args.Add("pipe:1");
    }

    private static string Sanitize(string value)
    {
        var end = value.Length;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is '\r' or '\n' or '\0')
            {
                end = i;
                break;
            }
        }

        return value[..end].Trim();
    }
}
