using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Catalog;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Epg;
using Jellyfin.Plugin.StreamFin.State;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Library;

/// <summary>
/// Runs a full catalog sync: fetch each provider, publish its libraries, and refresh the guide.
/// </summary>
public sealed class SyncService : IDisposable
{
    private readonly CatalogService _catalogService;
    private readonly LibraryWriter _libraryWriter;
    private readonly LibraryProvisioner _provisioner;
    private readonly XmltvCache _epgCache;
    private readonly SyncStateStore _stateStore;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SyncService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncService"/> class.
    /// </summary>
    /// <param name="catalogService">The catalog service.</param>
    /// <param name="libraryWriter">The library writer.</param>
    /// <param name="provisioner">The library provisioner.</param>
    /// <param name="epgCache">The guide cache.</param>
    /// <param name="stateStore">The sync state store.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public SyncService(
        CatalogService catalogService,
        LibraryWriter libraryWriter,
        LibraryProvisioner provisioner,
        XmltvCache epgCache,
        SyncStateStore stateStore,
        ILibraryManager libraryManager,
        ILogger<SyncService> logger)
    {
        _catalogService = catalogService;
        _libraryWriter = libraryWriter;
        _provisioner = provisioner;
        _epgCache = epgCache;
        _stateStore = stateStore;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether a sync is running right now.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Syncs every enabled provider.
    /// </summary>
    /// <param name="progress">Receives a 0-100 completion percentage.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the sync produced.</returns>
    public async Task<SyncState> SyncAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!await _syncLock.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("A StreamFin sync is already running; skipping this one");
            return await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        IsRunning = true;
        try
        {
            return await RunAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IsRunning = false;
            _syncLock.Release();
        }
    }

    private async Task<SyncState> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var options = StreamFinPlugin.Instance?.Configuration ?? new PluginConfiguration();
        var providers = CatalogService.GetEnabledProviders();
        var state = new SyncState { LastSyncUtc = DateTime.UtcNow };

        if (!options.ImportLibraries)
        {
            _logger.LogInformation("Library import is off; Watch plays from the provider without copying the catalog");
            state.LastSyncSucceeded = true;
            progress?.Report(100);
            return state;
        }

        if (providers.Count == 0)
        {
            _logger.LogInformation("No StreamFin providers are enabled; nothing to sync");
            state.LastSyncSucceeded = true;
            await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            progress?.Report(100);
            return state;
        }

        if (options.AutoProvision)
        {
            var provisioned = await _provisioner.ProvisionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var warning in provisioned.Warnings)
            {
                _logger.LogWarning("Provisioning: {Warning}", warning);
            }
        }

        var results = new List<ProviderSyncResult>();
        var anyLibraryChanged = false;

        for (var i = 0; i < providers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var provider = providers[i];
            var slice = 100d / providers.Count;
            var completed = i * slice;

            var providerProgress = new Progress<double>(fraction =>
                progress?.Report(completed + (slice * Math.Clamp(fraction, 0d, 1d))));

            var result = await SyncProviderAsync(provider, options, providerProgress, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (result.FilesWritten > 0 || result.FilesRemoved > 0)
            {
                anyLibraryChanged = true;
            }

            progress?.Report(completed + slice);
        }

        state.Providers = results;
        state.LastSyncSucceeded = results.All(r => r.Succeeded);
        state.LastError = results.FirstOrDefault(r => !r.Succeeded)?.Error;

        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);

        if (anyLibraryChanged)
        {
            // A scan is only worth queueing when something actually moved on disk.
            _logger.LogInformation("Library files changed; queueing a Jellyfin library scan");
            _libraryManager.QueueLibraryScan();
        }

        progress?.Report(100);
        return state;
    }

    private async Task<ProviderSyncResult> SyncProviderAsync(
        ProviderOptions provider,
        PluginConfiguration options,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ProviderSyncResult
        {
            ProviderId = provider.Id,
            ProviderName = provider.Name
        };

        try
        {
            _logger.LogInformation("Syncing {Provider}", provider.Name);

            // The catalog fetch is the long pole, so give it most of the progress bar.
            var catalogProgress = new Progress<double>(fraction => progress.Report(fraction * 0.8d));
            var catalog = await _catalogService.GetFullCatalogAsync(provider, catalogProgress, cancellationToken).ConfigureAwait(false);

            result.ChannelCount = catalog.Channels.Count;

            if (provider.EnableMovies || provider.EnableSeries)
            {
                var previouslyWritten = await _stateStore.LoadWrittenFilesAsync(provider.Id, cancellationToken).ConfigureAwait(false);
                var written = await _libraryWriter
                    .WriteAsync(provider, catalog, options, previouslyWritten, cancellationToken)
                    .ConfigureAwait(false);

                result.MovieCount = written.MovieCount;
                result.SeriesCount = written.SeriesCount;
                result.EpisodeCount = written.EpisodeCount;
                result.FilesWritten = written.FilesWritten;
                result.FilesRemoved = written.FilesRemoved;

                await _stateStore.SaveWrittenFilesAsync(provider.Id, written.WrittenPaths, cancellationToken).ConfigureAwait(false);
            }

            progress.Report(0.9d);

            if (provider.EnableLiveTv)
            {
                // An M3U header can carry the guide URL, so pick it up if the provider has none set.
                if (string.IsNullOrWhiteSpace(provider.EpgUrl) && !string.IsNullOrWhiteSpace(catalog.DiscoveredEpgUrl))
                {
                    _logger.LogInformation("{Provider}: using the guide URL declared in the playlist header", provider.Name);
                    provider.EpgUrl = catalog.DiscoveredEpgUrl!;
                    StreamFinPlugin.Instance?.SaveConfiguration();
                }

                await _epgCache.EnsureAsync(provider, forceRefresh: true, cancellationToken).ConfigureAwait(false);
            }

            result.Succeeded = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed for {Provider}", provider.Name);
            result.Succeeded = false;
            result.Error = ex.Message;
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        progress.Report(1d);

        return result;
    }

    /// <inheritdoc />
    public void Dispose() => _syncLock.Dispose();
}
