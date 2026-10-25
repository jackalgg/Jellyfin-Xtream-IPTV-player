using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Catalog;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Library;
using Jellyfin.Plugin.StreamFin.State;
using Jellyfin.Plugin.StreamFin.Tasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Api;

/// <summary>
/// Backs the StreamFin settings page.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("StreamFin")]
[Produces(MediaTypeNames.Application.Json)]
public class StreamFinController : ControllerBase
{
    private readonly CatalogService _catalogService;
    private readonly SyncService _syncService;
    private readonly LibraryProvisioner _provisioner;
    private readonly SyncStateStore _stateStore;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<StreamFinController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamFinController"/> class.
    /// </summary>
    /// <param name="catalogService">The catalog service.</param>
    /// <param name="syncService">The sync service.</param>
    /// <param name="provisioner">The library provisioner.</param>
    /// <param name="stateStore">The sync state store.</param>
    /// <param name="taskManager">The task manager.</param>
    /// <param name="logger">The logger.</param>
    public StreamFinController(
        CatalogService catalogService,
        SyncService syncService,
        LibraryProvisioner provisioner,
        SyncStateStore stateStore,
        ITaskManager taskManager,
        ILogger<StreamFinController> logger)
    {
        _catalogService = catalogService;
        _syncService = syncService;
        _provisioner = provisioner;
        _stateStore = stateStore;
        _taskManager = taskManager;
        _logger = logger;
    }

    /// <summary>
    /// Tests a provider's settings without saving them.
    /// </summary>
    /// <param name="provider">The provider settings to test.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The test ran; check the body for the outcome.</response>
    /// <returns>What the provider reported.</returns>
    [HttpPost("TestProvider")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProviderTestResponse>> TestProvider(
        [FromBody] ProviderOptions provider,
        CancellationToken cancellationToken)
    {
        if (provider is null)
        {
            return BadRequest();
        }

        try
        {
            var probe = await _catalogService.ProbeAsync(provider, cancellationToken).ConfigureAwait(false);

            return new ProviderTestResponse
            {
                Success = true,
                Message = probe.Message,
                ChannelCount = probe.ChannelCount,
                MovieCount = probe.MovieCount,
                SeriesCount = probe.SeriesCount,
                EpisodeCount = probe.EpisodeCount,
                AccountStatus = probe.AccountStatus,
                ExpiresAtUtc = probe.ExpiresAtUtc,
                MaxConnections = probe.MaxConnections,
                ActiveConnections = probe.ActiveConnections,
                IsTrial = probe.IsTrial,
                DiscoveredEpgUrl = probe.DiscoveredEpgUrl
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The settings page needs the reason, so report the failure in the body with a 200
            // rather than as an error status the browser would render as a generic failure.
            _logger.LogInformation("Provider test for {Provider} failed: {Reason}", provider.Name, ex.Message);
            return new ProviderTestResponse { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Lists a provider's categories for one content type.
    /// </summary>
    /// <param name="contentType">One of <c>live</c>, <c>movies</c>, or <c>series</c>.</param>
    /// <param name="provider">The provider settings to query with.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The categories were listed.</response>
    /// <returns>The categories.</returns>
    [HttpPost("Categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryListResponse>> GetCategories(
        [FromQuery] string contentType,
        [FromBody] ProviderOptions provider,
        CancellationToken cancellationToken)
    {
        if (provider is null)
        {
            return BadRequest();
        }

        var normalized = (contentType ?? "live").ToLowerInvariant();

        try
        {
            var categories = await _catalogService
                .GetCategoriesAsync(provider, normalized, cancellationToken)
                .ConfigureAwait(false);

            return new CategoryListResponse { Success = true, Categories = categories };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CategoryListResponse { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Reports what the last sync produced.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The status was read.</response>
    /// <returns>The current status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<StatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var options = StreamFinPlugin.Instance?.Configuration ?? new PluginConfiguration();
        var providers = options.Providers ?? Array.Empty<ProviderOptions>();

        return new StatusResponse
        {
            IsSyncRunning = _syncService.IsRunning,
            LastSyncUtc = state.LastSyncUtc,
            LastSyncSucceeded = state.LastSyncSucceeded,
            LastError = state.LastError,
            Providers = state.Providers,
            LibraryRoot = StreamFinPlugin.Instance?.GetLibraryRoot() ?? string.Empty,
            ConfiguredProviderCount = providers.Length,
            EnabledProviderCount = providers.Count(p => p.Enabled)
        };
    }

    /// <summary>
    /// Queues a full sync.
    /// </summary>
    /// <response code="204">The sync was queued.</response>
    /// <returns>No content.</returns>
    [HttpPost("Sync")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult StartSync()
    {
        // Queue it through the task manager so it shows up on the Scheduled Tasks page with a
        // progress bar and a cancel button, rather than running invisibly inside a request.
        _taskManager.CancelIfRunningAndQueue<SyncLibrariesTask>();
        return NoContent();
    }

    /// <summary>
    /// Creates the Jellyfin libraries, tuners, and guide providers for the saved settings.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Provisioning ran; check the body for details.</response>
    /// <returns>What was set up.</returns>
    [HttpPost("Provision")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProvisionResponse>> Provision(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _provisioner.ProvisionAsync(cancellationToken).ConfigureAwait(false);

            return new ProvisionResponse
            {
                Success = true,
                LibrariesCreated = result.LibrariesCreated.ToArray(),
                TunersConfigured = result.TunersConfigured.ToArray(),
                GuidesConfigured = result.GuidesConfigured.ToArray(),
                Warnings = result.Warnings.ToArray()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provisioning failed");
            return new ProvisionResponse { Success = false, Warnings = new[] { ex.Message } };
        }
    }
}

/// <summary>
/// The result of testing a provider.
/// </summary>
public class ProviderTestResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the provider answered usefully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets a human-readable summary or failure reason.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of live channels offered.
    /// </summary>
    public int ChannelCount { get; set; }

    /// <summary>
    /// Gets or sets the number of movies offered.
    /// </summary>
    public int MovieCount { get; set; }

    /// <summary>
    /// Gets or sets the number of series offered.
    /// </summary>
    public int SeriesCount { get; set; }

    /// <summary>
    /// Gets or sets the number of episodes offered, when known.
    /// </summary>
    public int EpisodeCount { get; set; }

    /// <summary>
    /// Gets or sets the account status the panel reported.
    /// </summary>
    public string? AccountStatus { get; set; }

    /// <summary>
    /// Gets or sets when the subscription lapses.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of simultaneous streams allowed.
    /// </summary>
    public int? MaxConnections { get; set; }

    /// <summary>
    /// Gets or sets the number of streams currently open.
    /// </summary>
    public int? ActiveConnections { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a trial line.
    /// </summary>
    public bool IsTrial { get; set; }

    /// <summary>
    /// Gets or sets a guide address discovered during the test.
    /// </summary>
    public string? DiscoveredEpgUrl { get; set; }
}

/// <summary>
/// A provider's categories.
/// </summary>
public class CategoryListResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the listing succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the failure reason.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the categories.
    /// </summary>
    public IReadOnlyList<CategorySummary> Categories { get; set; } = Array.Empty<CategorySummary>();
}

/// <summary>
/// What the last sync produced.
/// </summary>
public class StatusResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether a sync is running right now.
    /// </summary>
    public bool IsSyncRunning { get; set; }

    /// <summary>
    /// Gets or sets when the last sync finished.
    /// </summary>
    public DateTime? LastSyncUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the last sync completed without error.
    /// </summary>
    public bool LastSyncSucceeded { get; set; }

    /// <summary>
    /// Gets or sets the error from the last sync.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets the per-provider results.
    /// </summary>
    public IReadOnlyList<ProviderSyncResult> Providers { get; set; } = Array.Empty<ProviderSyncResult>();

    /// <summary>
    /// Gets or sets the directory the generated libraries live in.
    /// </summary>
    public string LibraryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many providers are configured.
    /// </summary>
    public int ConfiguredProviderCount { get; set; }

    /// <summary>
    /// Gets or sets how many providers are enabled.
    /// </summary>
    public int EnabledProviderCount { get; set; }
}

/// <summary>
/// What provisioning set up.
/// </summary>
public class ProvisionResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether provisioning ran.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the libraries that were created.
    /// </summary>
    public IReadOnlyList<string> LibrariesCreated { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the tuners that were configured.
    /// </summary>
    public IReadOnlyList<string> TunersConfigured { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the guide providers that were configured.
    /// </summary>
    public IReadOnlyList<string> GuidesConfigured { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the problems encountered.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}
