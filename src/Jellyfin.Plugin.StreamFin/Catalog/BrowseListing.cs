using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.StreamFin.Playback;

namespace Jellyfin.Plugin.StreamFin.Catalog;

/// <summary>
/// Pure helpers for the Watch page: category filtering, paging, and poster checks.
/// </summary>
internal static class BrowseListing
{
    /// <summary>
    /// Returns whether a panel ignored <c>category_id</c> and sent titles from other categories.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="items">The rows the panel returned.</param>
    /// <param name="categoryId">The category that was requested.</param>
    /// <param name="categoryOf">Reads a row's category identifier.</param>
    /// <returns><c>true</c> when another category is present.</returns>
    public static bool PanelReturnedOtherCategories<T>(
        IReadOnlyList<T> items,
        string categoryId,
        Func<T, string?> categoryOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(categoryOf);

        foreach (var item in items)
        {
            var id = categoryOf(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!string.Equals(id.Trim(), categoryId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Keeps the rows whose category matches <paramref name="categoryId"/>.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="items">The rows to filter.</param>
    /// <param name="categoryId">The category to keep. Empty keeps everything.</param>
    /// <param name="categoryOf">Reads a row's category identifier.</param>
    /// <returns>The matching rows.</returns>
    public static List<T> Matching<T>(IReadOnlyList<T> items, string? categoryId, Func<T, string?> categoryOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(categoryOf);

        if (string.IsNullOrWhiteSpace(categoryId))
        {
            var copy = new List<T>(items.Count);
            foreach (var item in items)
            {
                copy.Add(item);
            }

            return copy;
        }

        var matched = new List<T>();
        foreach (var item in items)
        {
            var id = categoryOf(item);
            if (id is not null && string.Equals(id.Trim(), categoryId, StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(item);
            }
        }

        return matched;
    }

    /// <summary>
    /// Returns one page of rows whose names contain <paramref name="query"/>.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="items">The rows to page.</param>
    /// <param name="query">The optional name filter.</param>
    /// <param name="nameOf">Reads a row's display name.</param>
    /// <param name="skip">How many matching rows to skip.</param>
    /// <param name="take">How many matching rows to return.</param>
    /// <returns>The page and the number of rows that matched before paging.</returns>
    public static (IReadOnlyList<T> Items, int Total) Page<T>(
        IReadOnlyList<T> items,
        string? query,
        Func<T, string?> nameOf,
        int skip,
        int take)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(nameOf);

        if (skip < 0)
        {
            skip = 0;
        }

        if (take < 0)
        {
            take = 0;
        }

        IReadOnlyList<T> matched;
        if (string.IsNullOrWhiteSpace(query))
        {
            matched = items;
        }
        else
        {
            var needle = query.Trim();
            var filtered = new List<T>();
            foreach (var item in items)
            {
                var name = nameOf(item);
                if (name is not null && name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(item);
                }
            }

            matched = filtered;
        }

        var total = matched.Count;
        if (skip >= total || take == 0)
        {
            return (Array.Empty<T>(), total);
        }

        var count = Math.Min(take, total - skip);
        var page = new T[count];
        for (var i = 0; i < count; i++)
        {
            page[i] = matched[skip + i];
        }

        return (page, total);
    }

    /// <summary>
    /// Returns an http or https poster address, or <c>null</c> for anything else.
    /// </summary>
    /// <param name="url">The address the provider sent.</param>
    /// <returns>The address, or <c>null</c>.</returns>
    public static string? SafePoster(string? url)
    {
        if (!IsAbsoluteHttp(url))
        {
            return null;
        }

        return url!.Trim();
    }

    /// <summary>
    /// Returns whether <paramref name="url"/> is an absolute http or https address.
    /// </summary>
    /// <param name="url">The address to check.</param>
    /// <returns><c>true</c> when the browser can request it directly.</returns>
    public static bool IsAbsoluteHttp(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmed = url.Trim();
        return trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a short stable identifier for a stream URL.
    /// </summary>
    /// <param name="value">The value to hash.</param>
    /// <returns>A 16 character hex identifier.</returns>
    public static string ShortId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    /// <summary>
    /// Decides how the browser should open one film or episode.
    /// </summary>
    /// <param name="advertised">The container the panel advertised, such as <c>mkv</c> or <c>mp4</c>.</param>
    /// <returns>Which addresses are worth opening.</returns>
    /// <remarks>
    /// A browser can start HLS and MP4. It cannot start the MKV files most panels use. Trying those
    /// other extensions on the panel is what makes a click sit there: the panel often never answers
    /// a format it does not have. HLS is attempted only as its own address, and MKV is remuxed
    /// locally instead of being downloaded into a library.
    /// </remarks>
    public static OnDemandPlayback ForOnDemand(string? advertised)
    {
        var extension = StreamUrlCandidates.NormalizeExtension(advertised);
        if (string.IsNullOrEmpty(extension))
        {
            extension = "mkv";
        }

        return extension switch
        {
            "m3u8" or "m3u" => new OnDemandPlayback("m3u8", false, null, false),
            "mp4" or "m4v" or "webm" => new OnDemandPlayback(extension, true, extension, true),
            "ts" => new OnDemandPlayback("ts", true, "ts", false),
            _ => new OnDemandPlayback(extension, true, null, true)
        };
    }

    /// <summary>
    /// Chooses the addresses Watch should sign for one film or episode.
    /// </summary>
    /// <param name="advertised">The container the panel advertised.</param>
    /// <param name="alternateExtensions">Whether a sibling <c>.m3u8</c> is worth a short try. Playlist files are not rewritten.</param>
    /// <returns>The containers to sign. A missing play container means the remux address is the only one.</returns>
    public static OnDemandTargets Targets(string? advertised, bool alternateExtensions)
    {
        var plan = ForOnDemand(advertised);
        if (plan.SourceExtension is "m3u8")
        {
            return new OnDemandTargets("m3u8", "m3u8", null, null, null);
        }

        string? hls = plan.TryHls && alternateExtensions ? "m3u8" : null;
        var native = plan.NativeExtension;
        string? remux = plan.Remux ? plan.SourceExtension : null;

        if (native is "mp4" or "m4v" or "webm")
        {
            return new OnDemandTargets(native, native, hls, hls is null ? null : "m3u8", remux);
        }

        if (hls is not null)
        {
            var fallback = native == "ts" ? "ts" : null;
            return new OnDemandTargets("m3u8", "m3u8", fallback, fallback, remux);
        }

        if (native == "ts")
        {
            return new OnDemandTargets("ts", "ts", null, null, null);
        }

        return new OnDemandTargets(null, "ts", null, null, remux);
    }

    /// <summary>
    /// Resolves one on-demand container against a direct file address.
    /// </summary>
    /// <param name="directUrl">The absolute file address.</param>
    /// <param name="extension">The container being requested.</param>
    /// <returns>The address to open. An extensionless file is left alone unless a playlist sibling was asked for.</returns>
    public static string DirectAddress(string directUrl, string extension)
    {
        ArgumentNullException.ThrowIfNull(directUrl);
        ArgumentNullException.ThrowIfNull(extension);

        var direct = directUrl.Trim();
        var current = StreamUrlCandidates.NormalizeExtension(StreamUrlCandidates.GetExtension(direct));
        var wanted = StreamUrlCandidates.NormalizeExtension(extension) ?? extension;

        if (current is null)
        {
            if (wanted is "m3u8" or "m3u")
            {
                return StreamUrlCandidates.ReplaceExtension(direct, wanted);
            }

            return direct;
        }

        if ((current is "m3u" or "m3u8") && (wanted is "m3u" or "m3u8"))
        {
            return direct;
        }

        if (string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
        {
            return direct;
        }

        return StreamUrlCandidates.ReplaceExtension(direct, wanted);
    }

    /// <summary>
    /// Picks the container the browser should try first for an address.
    /// </summary>
    /// <param name="url">The stream address.</param>
    /// <param name="live">Whether the stream is live.</param>
    /// <returns>A container extension without a leading dot.</returns>
    public static string PlaybackContainer(string url, bool live)
    {
        var extension = StreamUrlCandidates.NormalizeExtension(StreamUrlCandidates.GetExtension(url));
        if (extension is "m3u8" or "m3u")
        {
            return "m3u8";
        }

        if (extension is "mp4" or "m4v" or "webm")
        {
            return extension;
        }

        if (live)
        {
            return "ts";
        }

        return string.IsNullOrEmpty(extension) ? "mp4" : extension;
    }
}

/// <summary>
/// How Watch should open one on-demand title.
/// </summary>
/// <param name="SourceExtension">The container the panel actually has.</param>
/// <param name="TryHls">Whether a sibling <c>.m3u8</c> address is worth a short try.</param>
/// <param name="NativeExtension">A container the browser can play directly, or <c>null</c>.</param>
/// <param name="Remux">Whether Jellyfin should remux the real file when the browser cannot open it.</param>
internal readonly record struct OnDemandPlayback(
    string SourceExtension,
    bool TryHls,
    string? NativeExtension,
    bool Remux);

/// <summary>
/// The containers Watch should sign for one film or episode.
/// </summary>
/// <param name="PlayExtension">The file extension of the first address, or <c>null</c> when only a remux can be played.</param>
/// <param name="PlayContainer">The container the browser should use for the first address.</param>
/// <param name="FallbackExtension">The file extension of the second address, or <c>null</c>.</param>
/// <param name="FallbackContainer">The container the browser should use for the second address, or <c>null</c>.</param>
/// <param name="RemuxExtension">The real file to remux, or <c>null</c> when no remux is needed.</param>
internal readonly record struct OnDemandTargets(
    string? PlayExtension,
    string PlayContainer,
    string? FallbackExtension,
    string? FallbackContainer,
    string? RemuxExtension);
