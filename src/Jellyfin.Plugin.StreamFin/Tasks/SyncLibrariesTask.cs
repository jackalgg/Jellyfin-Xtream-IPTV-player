using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.StreamFin.Tasks;

/// <summary>
/// The scheduled task that refreshes everything StreamFin publishes.
/// </summary>
public class SyncLibrariesTask : IScheduledTask
{
    private readonly SyncService _syncService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncLibrariesTask"/> class.
    /// </summary>
    /// <param name="syncService">The sync service.</param>
    public SyncLibrariesTask(SyncService syncService)
    {
        _syncService = syncService;
    }

    /// <inheritdoc />
    public string Name => "Sync IPTV libraries";

    /// <inheritdoc />
    public string Key => "StreamFinSyncLibraries";

    /// <inheritdoc />
    public string Description =>
        "Fetches each configured IPTV provider and refreshes the generated movie and series libraries, the channel list, and the guide.";

    /// <inheritdoc />
    public string Category => "StreamFin IPTV";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _syncService.SyncAsync(progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var options = StreamFinPlugin.Instance?.Configuration ?? new PluginConfiguration();
        var hours = Math.Clamp(options.SyncIntervalHours, 1, 24 * 7);

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(hours).Ticks
        };

        // Catch up on anything that changed while the server was down.
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };
    }
}
