using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.StreamFin.Configuration;

/// <summary>
/// How a <see cref="CategoryFilter"/> treats its category list.
/// </summary>
public enum FilterMode
{
    /// <summary>
    /// Everything the provider offers is imported.
    /// </summary>
    All = 0,

    /// <summary>
    /// Only the listed categories are imported.
    /// </summary>
    Include = 1,

    /// <summary>
    /// Everything except the listed categories is imported.
    /// </summary>
    Exclude = 2
}

/// <summary>
/// Selects which categories and titles of one content type get imported.
/// </summary>
/// <remarks>
/// IPTV resellers routinely expose tens of thousands of streams across hundreds of categories.
/// Importing all of it produces an unusable library and a very slow sync, so every content type
/// gets its own filter.
/// </remarks>
public class CategoryFilter
{
    /// <summary>
    /// Gets or sets how <see cref="Categories"/> is interpreted.
    /// </summary>
    public FilterMode Mode { get; set; } = FilterMode.All;

    /// <summary>
    /// Gets or sets the provider category identifiers this filter applies to.
    /// </summary>
    /// <remarks>
    /// An array, rather than <c>IReadOnlyList&lt;string&gt;</c>, because this object is stored in
    /// the plugin's XML configuration and <c>XmlSerializer</c> cannot write interface types.
    /// </remarks>
    public string[] Categories { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets regular expressions matched against item titles. A title matching any of them
    /// is dropped regardless of its category.
    /// </summary>
    public string[] TitleExcludePatterns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the maximum number of items imported per category, or 0 for no limit.
    /// </summary>
    public int MaxItemsPerCategory { get; set; }

    /// <summary>
    /// Determines whether a category passes this filter.
    /// </summary>
    /// <param name="categoryId">The provider's category identifier.</param>
    /// <param name="categoryName">The category's display name.</param>
    /// <returns><c>true</c> when the category should be imported.</returns>
    public bool AllowsCategory(string? categoryId, string? categoryName)
    {
        var categories = Categories ?? Array.Empty<string>();
        if (Mode == FilterMode.All || categories.Length == 0)
        {
            return Mode != FilterMode.Include;
        }

        var listed = categories.Any(c =>
            string.Equals(c, categoryId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c, categoryName, StringComparison.OrdinalIgnoreCase));

        return Mode == FilterMode.Include ? listed : !listed;
    }

    /// <summary>
    /// Determines whether a title passes the exclusion patterns.
    /// </summary>
    /// <param name="title">The item title.</param>
    /// <returns><c>true</c> when the title should be imported.</returns>
    public bool AllowsTitle(string? title)
    {
        var patterns = TitleExcludePatterns ?? Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(title) || patterns.Length == 0)
        {
            return true;
        }

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            try
            {
                if (Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                // A user typo in the pattern must not take the whole sync down.
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        return true;
    }
}
