using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// Remuxes one provider file into MPEG-TS so the browser can play it.
/// </summary>
/// <remarks>
/// Only the title that was clicked is opened. The catalog is not imported, and other container
/// extensions are not probed. Video is copied when the panel's file already starts; otherwise that
/// same file is transcoded.
/// </remarks>
public sealed class FfmpegRemuxer
{
    private static readonly TimeSpan CopyFirstByteTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan TranscodeFirstByteTimeout = TimeSpan.FromSeconds(20);

    private readonly IMediaEncoder _encoder;
    private readonly ILogger<FfmpegRemuxer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FfmpegRemuxer"/> class.
    /// </summary>
    /// <param name="encoder">Jellyfin's encoder, used to find ffmpeg.</param>
    /// <param name="logger">The logger.</param>
    public FfmpegRemuxer(IMediaEncoder encoder, ILogger<FfmpegRemuxer> logger)
    {
        _encoder = encoder;
        _logger = logger;
    }

    /// <summary>
    /// Writes an MPEG-TS stream of <paramref name="sourceUrl"/> to the response.
    /// </summary>
    /// <param name="provider">The provider that owns the file.</param>
    /// <param name="sourceUrl">The file address. It is not written to the log.</param>
    /// <param name="response">The response to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the remux finishes or the client disconnects.</returns>
    public async Task WriteMpegTsAsync(
        ProviderOptions provider,
        string sourceUrl,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUrl);
        ArgumentNullException.ThrowIfNull(response);

        var ffmpeg = ResolveFfmpeg();
        _logger.LogInformation("Remuxing one title from {Provider}", provider.Name);

        if (await WriteOnceAsync(ffmpeg, provider, sourceUrl, response, copy: true, CopyFirstByteTimeout, cancellationToken)
                .ConfigureAwait(false)
            || cancellationToken.IsCancellationRequested
            || response.HasStarted)
        {
            return;
        }

        _logger.LogInformation("Remux copy produced no video for {Provider}; transcoding that title", provider.Name);
        if (!await WriteOnceAsync(ffmpeg, provider, sourceUrl, response, copy: false, TranscodeFirstByteTimeout, cancellationToken)
                .ConfigureAwait(false)
            && !response.HasStarted
            && !cancellationToken.IsCancellationRequested)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
        }
    }

    private async Task<bool> WriteOnceAsync(
        string ffmpeg,
        ProviderOptions provider,
        string sourceUrl,
        HttpResponse response,
        bool copy,
        TimeSpan firstByteTimeout,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in copy
            ? FfmpegRemuxArguments.Copy(provider, sourceUrl)
            : FfmpegRemuxArguments.Transcode(provider, sourceUrl))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.ErrorDataReceived += static (_, _) => { };

        try
        {
            if (!process.Start())
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogWarning("ffmpeg could not start for {Provider}: {Reason}", provider.Name, ex.GetType().Name);
            return false;
        }

        using var killOnCancel = cancellationToken.Register(() => Kill(process));
        var started = false;

        try
        {
            process.BeginErrorReadLine();
            var stdout = process.StandardOutput.BaseStream;
            var buffer = new byte[188 * 32];
            using var firstByte = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            firstByte.CancelAfter(firstByteTimeout);

            var read = 0;
            var syncAt = -1;
            while (syncAt < 0)
            {
                int count;
                try
                {
                    count = await stdout.ReadAsync(buffer.AsMemory(read), firstByte.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (count == 0)
                {
                    return false;
                }

                var found = Array.IndexOf(buffer, (byte)0x47, read, count);
                read += count;
                if (found >= 0)
                {
                    syncAt = found;
                    break;
                }

                if (read >= buffer.Length)
                {
                    return false;
                }
            }

            DisableBuffering(response);
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "video/mp2t";
            response.Headers[HeaderNames.CacheControl] = "no-store";
            started = true;

            await response.Body.WriteAsync(buffer.AsMemory(syncAt, read - syncAt), cancellationToken).ConfigureAwait(false);
            await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            await stdout.CopyToAsync(response.Body, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (!started && ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning("Remux failed for {Provider}: {Reason}", provider.Name, ex.GetType().Name);
            return false;
        }
        finally
        {
            Kill(process);
        }
    }

    private string ResolveFfmpeg()
    {
        try
        {
            var path = _encoder.EncoderPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Jellyfin's ffmpeg path is not ready");
        }

        return "ffmpeg";
    }

    private static void DisableBuffering(HttpResponse response)
    {
        var feature = response.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        feature?.DisableBuffering();
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process already exited.
        }
    }
}
