using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Epg;
using Jellyfin.Plugin.StreamFin.LiveTv;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamFin.Library;

/// <summary>
/// What one provisioning pass set up.
/// </summary>
public class ProvisionResult
{
    /// <summary>
    /// Gets the libraries that were created.
    /// </summary>
    public IList<string> LibrariesCreated { get; } = new List<string>();

    /// <summary>
    /// Gets the Live TV tuners that were created or updated.
    /// </summary>
    public IList<string> TunersConfigured { get; } = new List<string>();

    /// <summary>
    /// Gets the guide providers that were created or updated.
    /// </summary>
    public IList<string> GuidesConfigured { get; } = new List<string>();

    /// <summary>
    /// Gets the problems encountered, which do not stop the rest of provisioning.
    /// </summary>
    public IList<string> Warnings { get; } = new List<string>();
}

/// <summary>
/// Creates the Jellyfin libraries, Live TV tuners, and guide providers for each configured
/// subscription.
/// </summary>
/// <remarks>
/// Doing this from the plugin is the "seamless" part of the setup. The manual alternative is six
/// screens of Jellyfin configuration per provider: add a tuner, pick its type, paste the URL, add
/// a guide, map its channels, then create two libraries pointed at folders the user has to know
/// the plugin generated.
/// </remarks>
public class LibraryProvisioner
{
    private readonly ILibraryManager _libraryManager;
    private readonly ITunerHostManager _tunerHostManager;
    private readonly IListingsManager _listingsManager;
    private readonly IConfigurationManager _configurationManager;
    private readonly ILogger<LibraryProvisioner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryProvisioner"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tunerHostManager">The tuner host manager.</param>
    /// <param name="listingsManager">The listings manager.</param>
    /// <param name="configurationManager">The server configuration manager.</param>
    /// <param name="logger">The logger.</param>
    public LibraryProvisioner(
        ILibraryManager libraryManager,
        ITunerHostManager tunerHostManager,
        IListingsManager listingsManager,
        IConfigurationManager configurationManager,
        ILogger<LibraryProvisioner> logger)
    {
        _libraryManager = libraryManager;
        _tunerHostManager = tunerHostManager;
        _listingsManager = listingsManager;
        _configurationManager = configurationManager;
        _logger = logger;
    }

    /// <summary>
    /// Sets up everything the enabled providers need.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was set up.</returns>
    public async Task<ProvisionResult> ProvisionAsync(CancellationToken cancellationToken)
    {
        var result = new ProvisionResult();
        var plugin = StreamFinPlugin.Instance;
        if (plugin is null)
        {
            return result;
        }

        var options = plugin.Configuration;

        foreach (var provider in options.Providers.Where(p => p.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (provider.EnableLiveTv)
            {
                await ProvisionLiveTvAsync(provider, result).ConfigureAwait(false);
            }

            if (provider.EnableMovies)
            {
                await ProvisionLibraryAsync(
                    options,
                    $"{provider.Name} Movies",
                    CollectionTypeOptions.movies,
                    LibraryWriter.GetMoviesPath(provider, options),
                    result).ConfigureAwait(false);
            }

            if (provider.EnableSeries)
            {
                await ProvisionLibraryAsync(
                    options,
                    $"{provider.Name} TV Shows",
                    CollectionTypeOptions.tvshows,
                    LibraryWriter.GetSeriesPath(provider, options),
                    result).ConfigureAwait(false);
            }
        }

        return result;
    }

    private async Task ProvisionLiveTvAsync(ProviderOptions provider, ProvisionResult result)
    {
        var liveTvOptions = _configurationManager.GetConfiguration<LiveTvOptions>("livetv");

        var existingTuner = (liveTvOptions.TunerHosts ?? Array.Empty<TunerHostInfo>())
            .FirstOrDefault(t => string.Equals(t.Type, StreamFinTunerHost.TunerType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.DeviceId, provider.Id, StringComparison.OrdinalIgnoreCase));

        var tunerInfo = StreamFinTunerHost.BuildTunerHostInfo(provider, existingTuner?.Id);

        TunerHostInfo savedTuner;
        try
        {
            savedTuner = await _tunerHostManager.SaveTunerHost(tunerInfo, dataSourceChanged: true).ConfigureAwait(false);
            result.TunersConfigured.Add(provider.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not configure the Live TV tuner for {Provider}", provider.Name);
            result.Warnings.Add($"Live TV tuner for {provider.Name}: {ex.Message}");
            return;
        }

        var guideUrl = Catalog.CatalogService.ResolveEpgUrl(provider);
        if (string.IsNullOrWhiteSpace(guideUrl))
        {
            result.Warnings.Add(
                $"{provider.Name} has no guide URL, so its channels will show without programme data. Add an XMLTV URL to fix that.");
            return;
        }

        var existingGuide = (liveTvOptions.ListingProviders ?? Array.Empty<ListingsProviderInfo>())
            .FirstOrDefault(l => string.Equals(l.Type, StreamFinListingsProvider.ProviderType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(l.ListingsId, provider.Id, StringComparison.OrdinalIgnoreCase));

        var guideInfo = StreamFinListingsProvider.BuildListingsProviderInfo(provider, savedTuner.Id, existingGuide?.Id);

        // Keep any channel mappings the user made by hand.
        if (existingGuide?.ChannelMappings is { Length: > 0 })
        {
            guideInfo.ChannelMappings = existingGuide.ChannelMappings;
        }

        try
        {
            await _listingsManager.SaveListingProvider(guideInfo, validateLogin: false, validateListings: false).ConfigureAwait(false);
            result.GuidesConfigured.Add(provider.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not configure the guide for {Provider}", provider.Name);
            result.Warnings.Add($"Guide for {provider.Name}: {ex.Message}");
        }
    }

    private async Task ProvisionLibraryAsync(
        PluginConfiguration options,
        string libraryName,
        CollectionTypeOptions collectionType,
        string path,
        ProvisionResult result)
    {
        Directory.CreateDirectory(path);

        var existing = _libraryManager.GetVirtualFolders()
            .FirstOrDefault(f => f.Locations is not null
                && f.Locations.Any(l => string.Equals(
                    Path.TrimEndingDirectorySeparator(l),
                    Path.TrimEndingDirectorySeparator(path),
                    StringComparison.OrdinalIgnoreCase)));

        if (existing is not null)
        {
            return;
        }

        var libraryOptions = new LibraryOptions
        {
            Enabled = true,
            PathInfos = new[] { new MediaPathInfo(path) },

            // A .strm library is a set of pointers to a remote server. Watching it for real-time
            // changes is pointless because StreamFin is the only thing that writes to it, and the
            // image and chapter extraction passes would each re-download the remote media.
            EnableRealtimeMonitor = false,
            EnableChapterImageExtraction = false,
            ExtractChapterImagesDuringLibraryScan = false,
            EnableTrickplayImageExtraction = false,
            ExtractTrickplayImagesDuringLibraryScan = false,
            EnablePhotos = false,
            EnableLUFSScan = false,

            SaveLocalMetadata = false,
            EnableEmbeddedTitles = false,
            EnableEmbeddedEpisodeInfos = false,
            EnableAutomaticSeriesGrouping = false,
            AutomaticRefreshIntervalDays = 0,
            MetadataSavers = Array.Empty<string>(),
            DisabledLocalMetadataReaders = Array.Empty<string>(),
            LocalMetadataReaderOrder = new[] { "Nfo" },
            TypeOptions = BuildTypeOptions(collectionType, options.EnableInternetMetadata)
        };

        try
        {
            await _libraryManager
                .AddVirtualFolder(libraryName, collectionType, libraryOptions, refreshLibrary: false)
                .ConfigureAwait(false);

            result.LibrariesCreated.Add(libraryName);
            _logger.LogInformation("Created the {Library} library at {Path}", libraryName, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not create the {Library} library", libraryName);
            result.Warnings.Add($"Library {libraryName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the per-item-type metadata options for a generated library.
    /// </summary>
    /// <remarks>
    /// Jellyfin decides whether a remote provider may run for an item by looking for a
    /// <see cref="TypeOptions"/> entry matching the item's type: when one exists, only the fetchers
    /// it names are allowed, and when none exists the server-wide defaults apply. So leaving this
    /// empty is how internet metadata stays on, and an entry with no fetchers is how it is turned
    /// off. <c>LibraryOptions.EnableInternetProviders</c> looks like the setting for this but is not
    /// read anywhere in the server.
    /// </remarks>
    /// <param name="collectionType">The collection type the library was created as.</param>
    /// <param name="enableInternetMetadata">Whether remote lookups are wanted.</param>
    /// <returns>The type options to store on the library.</returns>
    private static TypeOptions[] BuildTypeOptions(CollectionTypeOptions collectionType, bool enableInternetMetadata)
    {
        if (enableInternetMetadata)
        {
            return Array.Empty<TypeOptions>();
        }

        var itemTypes = collectionType == CollectionTypeOptions.tvshows
            ? new[] { "Series", "Season", "Episode" }
            : new[] { "Movie" };

        return itemTypes
            .Select(type => new TypeOptions
            {
                Type = type,
                MetadataFetchers = Array.Empty<string>(),
                MetadataFetcherOrder = Array.Empty<string>(),
                ImageFetchers = Array.Empty<string>(),
                ImageFetcherOrder = Array.Empty<string>()
            })
            .ToArray();
    }
}
