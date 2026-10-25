using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Epg;

/// <summary>
/// Downloads a provider's XMLTV guide and reshapes it into one small file per channel.
/// </summary>
/// <remarks>
/// Jellyfin refreshes the guide one channel at a time. Scanning a multi-hundred-megabyte XMLTV
/// document once per channel is what makes a large IPTV guide refresh take hours, so the download
/// is split into per-channel shards on the way in. After that, each channel's guide request reads
/// a file measured in kilobytes.
/// </remarks>
public sealed class XmltvCache : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Catalog.CatalogService _catalogService;
    private readonly ILogger<XmltvCache> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="XmltvCache"/> class.
    /// </summary>
    /// <param name="catalogService">The catalog service, used for its HTTP client.</param>
    /// <param name="logger">The logger.</param>
    public XmltvCache(Catalog.CatalogService catalogService, ILogger<XmltvCache> logger)
    {
        _catalogService = catalogService;
        _logger = logger;
    }

    /// <summary>
    /// Makes sure a provider's guide shards are present and recent, downloading if not.
    /// </summary>
    /// <param name="provider">The provider whose guide to load.</param>
    /// <param name="forceRefresh">Whether to download even when the shards look fresh.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The channels the guide declares.</returns>
    public async Task<IReadOnlyList<XmltvChannel>> EnsureAsync(
        ProviderOptions provider,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var url = Catalog.CatalogService.ResolveEpgUrl(provider);
        if (string.IsNullOrWhiteSpace(url))
        {
            return Array.Empty<XmltvChannel>();
        }

        var directory = GetProviderDirectory(provider.Id);
        var metadataPath = Path.Combine(directory, "meta.json");
        var ttl = TimeSpan.FromMinutes(Math.Clamp(
            StreamFinPlugin.Instance?.Configuration.CacheMinutes ?? 60,
            15,
            24 * 60));

        var existing = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        var isFresh = existing is not null
            && string.Equals(existing.Url, url, StringComparison.Ordinal)
            && DateTime.UtcNow - existing.DownloadedAtUtc < ttl;

        if (isFresh && !forceRefresh)
        {
            return existing!.Channels;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            if (existing is not null
                && string.Equals(existing.Url, url, StringComparison.Ordinal)
                && DateTime.UtcNow - existing.DownloadedAtUtc < ttl
                && !forceRefresh)
            {
                return existing.Channels;
            }

            try
            {
                return await DownloadAndShardAsync(provider, url, directory, metadataPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or Xtream.XtreamApiException)
            {
                if (existing is not null)
                {
                    _logger.LogWarning(
                        "{Provider}: guide download failed ({Reason}); keeping the copy from {Downloaded:u}",
                        provider.Name,
                        ex.Message,
                        existing.DownloadedAtUtc);
                    return existing.Channels;
                }

                _logger.LogError(ex, "{Provider}: could not download the guide", provider.Name);
                return Array.Empty<XmltvChannel>();
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Reads one channel's programmes within a window.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="channelId">The XMLTV channel identifier.</param>
    /// <param name="startUtc">The window start.</param>
    /// <param name="endUtc">The window end.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The programmes that overlap the window, in start order.</returns>
    public async Task<IReadOnlyList<XmltvProgramme>> GetProgrammesAsync(
        string providerId,
        string channelId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var path = GetShardPath(providerId, channelId);
        if (!File.Exists(path))
        {
            return Array.Empty<XmltvProgramme>();
        }

        var start = new DateTimeOffset(startUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        var end = new DateTimeOffset(endUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        var programmes = new List<XmltvProgramme>();

        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);

                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    XmltvProgramme? programme;
                    try
                    {
                        programme = JsonSerializer.Deserialize<XmltvProgramme>(line, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (programme is not null && programme.EndUnix > start && programme.StartUnix < end)
                    {
                        programmes.Add(programme);
                    }
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogDebug("Unreadable guide shard {Path}: {Reason}", path, ex.Message);
            return Array.Empty<XmltvProgramme>();
        }

        programmes.Sort((left, right) => left.StartUnix.CompareTo(right.StartUnix));
        return programmes;
    }

    /// <summary>
    /// Discards a provider's cached guide.
    /// </summary>
    /// <param name="providerId">The provider identifier, or <c>null</c> for all of them.</param>
    public void Invalidate(string? providerId = null)
    {
        try
        {
            var root = GetRootDirectory();
            if (providerId is null)
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                return;
            }

            var directory = GetProviderDirectory(providerId);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not clear the guide cache: {Reason}", ex.Message);
        }
    }

    private async Task<IReadOnlyList<XmltvChannel>> DownloadAndShardAsync(
        ProviderOptions provider,
        string url,
        string directory,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("{Provider}: downloading the guide", provider.Name);

        var downloadPath = Path.Combine(directory, "guide.xml");
        Directory.CreateDirectory(directory);

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var client = _catalogService.CreateHttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgents.Resolve(provider.UserAgent));
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new Xtream.XtreamApiException($"The guide URL returned HTTP {(int)response.StatusCode}.");
            }

            var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var destination = File.Create(downloadPath);
                await using (destination.ConfigureAwait(false))
                {
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        else
        {
            if (!File.Exists(url))
            {
                throw new Xtream.XtreamApiException($"No guide file found at {url}.");
            }

            File.Copy(url, downloadPath, overwrite: true);
        }

        var shardDirectory = Path.Combine(directory, "channels");
        var stagingDirectory = Path.Combine(directory, "channels.new");

        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        Directory.CreateDirectory(stagingDirectory);

        var channels = new Dictionary<string, XmltvChannel>(StringComparer.OrdinalIgnoreCase);
        var buffers = new Dictionary<string, List<XmltvProgramme>>(StringComparer.OrdinalIgnoreCase);
        var programmeCount = 0;

        var guideStream = OpenMaybeCompressed(downloadPath);
        await using (guideStream.ConfigureAwait(false))
        {
            foreach (var item in XmltvReader.Read(guideStream, cancellationToken))
            {
                if (item.Channel is not null)
                {
                    channels[item.Channel.Id] = item.Channel;
                    continue;
                }

                if (item.Programme is null)
                {
                    continue;
                }

                if (!buffers.TryGetValue(item.Programme.ChannelId, out var buffer))
                {
                    buffer = new List<XmltvProgramme>();
                    buffers[item.Programme.ChannelId] = buffer;
                }

                buffer.Add(item.Programme);
                programmeCount++;

                // Flush a channel once it has a sizeable block so a very large guide never needs
                // to be held in memory all at once.
                if (buffer.Count >= 2000)
                {
                    await AppendShardAsync(stagingDirectory, item.Programme.ChannelId, buffer, cancellationToken).ConfigureAwait(false);
                    buffer.Clear();
                }
            }
        }

        foreach (var pair in buffers.Where(p => p.Value.Count > 0))
        {
            await AppendShardAsync(stagingDirectory, pair.Key, pair.Value, cancellationToken).ConfigureAwait(false);
        }

        // Channels referenced only by programmes still deserve to exist.
        foreach (var channelId in buffers.Keys.Where(id => !channels.ContainsKey(id)))
        {
            channels[channelId] = new XmltvChannel { Id = channelId, Name = channelId };
        }

        // Swap the staged shards in only once the whole guide parsed, so a truncated download
        // never replaces a good guide with a partial one.
        if (Directory.Exists(shardDirectory))
        {
            Directory.Delete(shardDirectory, recursive: true);
        }

        Directory.Move(stagingDirectory, shardDirectory);
        TryDelete(downloadPath);

        var metadata = new CacheMetadata
        {
            Url = url,
            DownloadedAtUtc = DateTime.UtcNow,
            Channels = channels.Values.OrderBy(c => c.Name ?? c.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
            ProgrammeCount = programmeCount
        };

        var metadataStream = File.Create(metadataPath);
        await using (metadataStream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(metadataStream, metadata, _jsonOptions, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "{Provider}: guide holds {Programmes:N0} programmes across {Channels:N0} channels",
            provider.Name,
            programmeCount,
            channels.Count);

        return metadata.Channels;
    }

    private static async Task AppendShardAsync(
        string directory,
        string channelId,
        List<XmltvProgramme> programmes,
        CancellationToken cancellationToken)
    {
        // Shards are written as newline-delimited JSON arrays while building, then read back as a
        // single array; appending keeps peak memory flat for channels with a lot of guide data.
        var path = Path.Combine(directory, HashChannelId(channelId) + ".jsonl");

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
        await using (stream.ConfigureAwait(false))
        {
            foreach (var programme in programmes)
            {
                var line = JsonSerializer.SerializeToUtf8Bytes(programme, _jsonOptions);
                await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
                stream.WriteByte((byte)'\n');
            }
        }
    }

    private static Stream OpenMaybeCompressed(string path)
    {
        var fileStream = File.OpenRead(path);

        // Guides are frequently served gzipped with a .xml name, or not gzipped with a .gz name,
        // so sniff the magic bytes rather than trusting either.
        var header = new byte[2];
        var read = fileStream.Read(header, 0, 2);
        fileStream.Seek(0, SeekOrigin.Begin);

        if (read == 2 && header[0] == 0x1F && header[1] == 0x8B)
        {
            return new GZipStream(fileStream, CompressionMode.Decompress);
        }

        return fileStream;
    }

    private static string GetRootDirectory()
    {
        var directory = Path.Combine(
            StreamFinPlugin.Instance?.GetStateDirectory() ?? Path.GetTempPath(),
            "epg");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetProviderDirectory(string providerId)
    {
        var directory = Path.Combine(GetRootDirectory(), HashChannelId(providerId));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetShardPath(string providerId, string channelId)
        => Path.Combine(GetProviderDirectory(providerId), "channels", HashChannelId(channelId) + ".jsonl");

    private static string HashChannelId(string value)
    {
        // Channel identifiers routinely contain characters a file name cannot hold.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 10).ToLowerInvariant();
    }

    private async Task<CacheMetadata?> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<CacheMetadata>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogDebug("Unreadable guide metadata {Path}: {Reason}", path, ex.Message);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The download is a scratch file; leaving it costs nothing but disk.
            _ = ex;
        }
    }

    private sealed class CacheMetadata
    {
        public string Url { get; set; } = string.Empty;

        public DateTime DownloadedAtUtc { get; set; }

        public IReadOnlyList<XmltvChannel> Channels { get; set; } = Array.Empty<XmltvChannel>();

        public int ProgrammeCount { get; set; }
    }

    /// <inheritdoc />
    public void Dispose() => _refreshLock.Dispose();
}
