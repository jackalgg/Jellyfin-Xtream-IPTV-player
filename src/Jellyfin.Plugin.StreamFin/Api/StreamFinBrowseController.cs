using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Catalog;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Api;

/// <summary>
/// Backs the Watch page. Every call returns JSON so the page can show the reason a provider failed.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("StreamFin/Browse")]
public class StreamFinBrowseController : ControllerBase
{
    private readonly BrowseService _browse;
    private readonly ILogger<StreamFinBrowseController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamFinBrowseController"/> class.
    /// </summary>
    /// <param name="browse">The browse service.</param>
    /// <param name="logger">The logger.</param>
    public StreamFinBrowseController(BrowseService browse, ILogger<StreamFinBrowseController> logger)
    {
        _browse = browse;
        _logger = logger;
    }

    /// <summary>
    /// Lists saved subscriptions. Credentials are not included.
    /// </summary>
    /// <response code="200">The subscriptions were listed.</response>
    /// <returns>The subscriptions.</returns>
    [HttpGet("Providers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<BrowseProvidersResponse> GetProviders()
    {
        var providers = BrowseService.GetProviders();
        _logger.LogDebug("Watch listed {Count} subscriptions", providers.Count);
        return new BrowseProvidersResponse { Success = true, Providers = providers };
    }

    /// <summary>
    /// Lists category names for one subscription.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="contentType">One of <c>live</c>, <c>movies</c>, or <c>series</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The categories were listed, or the body explains why not.</response>
    /// <returns>The categories.</returns>
    [HttpGet("Categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<BrowseCategoriesResponse>> GetCategories(
        [FromQuery] string? providerId,
        [FromQuery] string? contentType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return new BrowseCategoriesResponse { Success = false, Message = "Choose a subscription first." };
        }

        try
        {
            var listed = await _browse.GetCategoriesAsync(providerId, contentType, cancellationToken).ConfigureAwait(false);
            return new BrowseCategoriesResponse
            {
                Success = true,
                Kind = listed.Kind,
                Categories = listed.Categories
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Watch categories failed: {Reason}", ex.GetType().Name);
            return new BrowseCategoriesResponse { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Lists one page of a single category.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="contentType">One of <c>live</c>, <c>movies</c>, or <c>series</c>.</param>
    /// <param name="categoryId">The category to open.</param>
    /// <param name="skip">How many matching items to skip.</param>
    /// <param name="take">How many matching items to return.</param>
    /// <param name="q">An optional name filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The page was listed, or the body explains why not.</response>
    /// <returns>The page.</returns>
    [HttpGet("Items")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<BrowseItemsResponse>> GetItems(
        [FromQuery] string? providerId,
        [FromQuery] string? contentType,
        [FromQuery] string? categoryId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 80,
        [FromQuery] string? q = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return new BrowseItemsResponse { Success = false, Message = "Choose a subscription first." };
        }

        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return new BrowseItemsResponse { Success = false, Message = "Pick a category." };
        }

        try
        {
            var page = await _browse
                .GetItemsAsync(providerId, contentType, categoryId, skip, take, q, cancellationToken)
                .ConfigureAwait(false);

            return new BrowseItemsResponse
            {
                Success = true,
                Total = page.Total,
                Skip = page.Skip,
                Take = page.Take,
                FullCatalog = page.FullCatalog,
                Playlist = page.Playlist,
                Items = page.Items
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Watch items failed: {Reason}", ex.GetType().Name);
            return new BrowseItemsResponse { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Lists the episodes of one show.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The episodes were listed, or the body explains why not.</response>
    /// <returns>The episodes.</returns>
    [HttpGet("Episodes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<BrowseEpisodesResponse>> GetEpisodes(
        [FromQuery] string? providerId,
        [FromQuery] string? seriesId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(seriesId))
        {
            return new BrowseEpisodesResponse { Success = false, Message = "Choose a show first." };
        }

        try
        {
            var episodes = await _browse.GetEpisodesAsync(providerId, seriesId, cancellationToken).ConfigureAwait(false);
            return new BrowseEpisodesResponse { Success = true, Episodes = episodes };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Watch episodes failed: {Reason}", ex.GetType().Name);
            return new BrowseEpisodesResponse { Success = false, Message = ex.Message };
        }
    }
}

/// <summary>
/// The subscriptions Watch can open.
/// </summary>
public class BrowseProvidersResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the list was read.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the failure reason.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the subscriptions.
    /// </summary>
    public IReadOnlyList<BrowseProviderInfo> Providers { get; set; } = Array.Empty<BrowseProviderInfo>();
}

/// <summary>
/// Category names for one subscription.
/// </summary>
public class BrowseCategoriesResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the categories were listed.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the failure reason.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the source kind, <c>Xtream</c> or <c>M3u</c>.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the categories.
    /// </summary>
    public IReadOnlyList<BrowseCategory> Categories { get; set; } = Array.Empty<BrowseCategory>();
}

/// <summary>
/// One page of a category.
/// </summary>
public class BrowseItemsResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the page was listed.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the failure reason.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets how many items matched before paging.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Gets or sets how many matching items were skipped.
    /// </summary>
    public int Skip { get; set; }

    /// <summary>
    /// Gets or sets how many items were requested.
    /// </summary>
    public int Take { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the panel sent every title for one category.
    /// </summary>
    public bool FullCatalog { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the items came from an M3U playlist.
    /// </summary>
    public bool Playlist { get; set; }

    /// <summary>
    /// Gets or sets the items.
    /// </summary>
    public IReadOnlyList<BrowseItem> Items { get; set; } = Array.Empty<BrowseItem>();
}

/// <summary>
/// The episodes of one show.
/// </summary>
public class BrowseEpisodesResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the episodes were listed.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the failure reason.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the episodes.
    /// </summary>
    public IReadOnlyList<BrowseEpisode> Episodes { get; set; } = Array.Empty<BrowseEpisode>();
}
