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
