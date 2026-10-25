using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.StreamFin.State;

/// <summary>
/// What the last library sync produced.
/// </summary>
public class SyncState
{
    /// <summary>
    /// Gets or sets when the last sync finished.
    /// </summary>
    public DateTime? LastSyncUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the last sync completed without error.
    /// </summary>
    public bool LastSyncSucceeded { get; set; }

    /// <summary>
    /// Gets or sets the error from the last sync, when it failed.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets the per-provider results of the last sync.
    /// </summary>
    public IReadOnlyList<ProviderSyncResult> Providers { get; set; } = Array.Empty<ProviderSyncResult>();
}

/// <summary>
/// What one provider contributed to the last sync.
/// </summary>
public class ProviderSyncResult
{
    /// <summary>
    /// Gets or sets the provider identifier.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider display name.
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this provider synced without error.
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Gets or sets the error, when this provider failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets how many live channels were published.
    /// </summary>
    public int ChannelCount { get; set; }

    /// <summary>
    /// Gets or sets how many movies were written.
    /// </summary>
    public int MovieCount { get; set; }

    /// <summary>
    /// Gets or sets how many series were written.
    /// </summary>
    public int SeriesCount { get; set; }

    /// <summary>
    /// Gets or sets how many episodes were written.
    /// </summary>
    public int EpisodeCount { get; set; }

    /// <summary>
    /// Gets or sets how many files the sync created or updated.
    /// </summary>
    public int FilesWritten { get; set; }

    /// <summary>
    /// Gets or sets how many files the sync removed because they are no longer offered.
    /// </summary>
    public int FilesRemoved { get; set; }

    /// <summary>
    /// Gets or sets how long this provider's sync took.
    /// </summary>
    public TimeSpan Duration { get; set; }
}
