using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Catalog;
using Jellyfin.Plugin.StreamFin.Epg;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Tasks;

/// <summary>
/// The scheduled task that re-downloads each provider's XMLTV guide.
/// </summary>
/// <remarks>
/// Separate from the library sync because guide data goes stale within hours while a provider's
/// movie and series catalog changes over days. Running them on one schedule would mean either a
/// stale guide or a needlessly hammered panel.
/// </remarks>
public class RefreshGuideCacheTask : IScheduledTask
{
    private readonly XmltvCache _cache;
    private readonly ILogger<RefreshGuideCacheTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshGuideCacheTask"/> class.
    /// </summary>
    /// <param name="cache">The guide cache.</param>
    /// <param name="logger">The logger.</param>
    public RefreshGuideCacheTask(XmltvCache cache, ILogger<RefreshGuideCacheTask> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Refresh IPTV guide data";

    /// <inheritdoc />
    public string Key => "StreamFinRefreshGuide";

    /// <inheritdoc />
    public string Description => "Re-downloads the XMLTV guide for each configured IPTV provider.";

    /// <inheritdoc />
    public string Category => "StreamFin IPTV";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var options = StreamFinPlugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        if (!options.ImportLibraries)
        {
            progress.Report(100);
            return;
        }

        var providers = CatalogService.GetEnabledProviders();
        if (providers.Count == 0)
        {
            progress.Report(100);
            return;
        }

        for (var i = 0; i < providers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var provider = providers[i];
            if (!provider.EnableLiveTv)
            {
                continue;
            }

            try
            {
                await _cache.EnsureAsync(provider, forceRefresh: true, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not refresh the guide for {Provider}", provider.Name);
            }

            progress.Report((i + 1) * 100d / providers.Count);
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }
}
