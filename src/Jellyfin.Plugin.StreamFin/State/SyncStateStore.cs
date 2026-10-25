using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Catalog;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.State;

/// <summary>
/// Persists sync results and the movie detail cache under the plugin's data folder.
/// </summary>
public sealed class SyncStateStore : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<SyncStateStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncStateStore"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public SyncStateStore(ILogger<SyncStateStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reads the last sync result.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The last sync result, or an empty one when none has been recorded.</returns>
    public async Task<SyncState> LoadAsync(CancellationToken cancellationToken)
        => await ReadAsync<SyncState>(GetPath("sync-state.json"), cancellationToken).ConfigureAwait(false) ?? new SyncState();

    /// <summary>
    /// Records a sync result.
    /// </summary>
    /// <param name="state">The result to record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the result is on disk.</returns>
    public Task SaveAsync(SyncState state, CancellationToken cancellationToken)
        => WriteAsync(GetPath("sync-state.json"), state, cancellationToken);

    /// <summary>
    /// Reads a provider's cached movie detail.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cache, keyed by stream identifier.</returns>
    public async Task<IDictionary<string, MovieDetailCacheEntry>> LoadMovieDetailCacheAsync(string providerId, CancellationToken cancellationToken)
    {
        var cache = await ReadAsync<Dictionary<string, MovieDetailCacheEntry>>(
            GetPath($"movie-detail-{Sanitize(providerId)}.json"),
            cancellationToken).ConfigureAwait(false);

        return cache ?? new Dictionary<string, MovieDetailCacheEntry>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes a provider's cached movie detail.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="cache">The cache to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the cache is on disk.</returns>
    public Task SaveMovieDetailCacheAsync(string providerId, IDictionary<string, MovieDetailCacheEntry> cache, CancellationToken cancellationToken)
        => WriteAsync(GetPath($"movie-detail-{Sanitize(providerId)}.json"), cache, cancellationToken);

    /// <summary>
    /// Reads the set of files the last sync wrote for a provider, which is what makes pruning
    /// possible without scanning the whole library tree.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The relative paths written last time.</returns>
    public async Task<HashSet<string>> LoadWrittenFilesAsync(string providerId, CancellationToken cancellationToken)
    {
        var files = await ReadAsync<List<string>>(
            GetPath($"files-{Sanitize(providerId)}.json"),
            cancellationToken).ConfigureAwait(false);

        return files is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes the set of files a sync produced for a provider.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="files">The relative paths written.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the set is on disk.</returns>
    public Task SaveWrittenFilesAsync(string providerId, IEnumerable<string> files, CancellationToken cancellationToken)
        => WriteAsync(GetPath($"files-{Sanitize(providerId)}.json"), files, cancellationToken);

    private static string GetPath(string fileName)
    {
        var directory = StreamFinPlugin.Instance?.GetStateDirectory()
            ?? Path.Combine(Path.GetTempPath(), "streamfin");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private static string Sanitize(string value)
    {
        var characters = value.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            if (!char.IsLetterOrDigit(characters[i]) && characters[i] != '-' && characters[i] != '_')
            {
                characters[i] = '_';
            }
        }

        return new string(characters);
    }

    private async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // State is a cache, not a source of truth; a corrupt file just means a slower sync.
            _logger.LogWarning("Discarding unreadable state file {Path}: {Reason}", path, ex.Message);
            return default;
        }
    }

    private async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var temporaryPath = path + ".tmp";

            var stream = File.Create(temporaryPath);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not save state to {Path}: {Reason}", path, ex.Message);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _writeLock.Dispose();
}
