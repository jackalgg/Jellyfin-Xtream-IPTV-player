using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Xml;

namespace Jellyfin.Plugin.StreamFin.Epg;

/// <summary>
/// What one pass over an XMLTV document found.
/// </summary>
/// <param name="Channel">A channel declaration, or <c>null</c> when this is a programme.</param>
/// <param name="Programme">A programme, or <c>null</c> when this is a channel declaration.</param>
public readonly record struct XmltvItem(XmltvChannel? Channel, XmltvProgramme? Programme);

/// <summary>
/// Streams an XMLTV document.
/// </summary>
/// <remarks>
/// Guides from IPTV panels regularly run to hundreds of megabytes, so this never materializes the
/// document. It yields each channel and programme as it is read, and tolerates the malformed
/// markup real guides contain rather than aborting the whole file.
/// </remarks>
public static class XmltvReader
{
    private static readonly XmlReaderSettings _settings = new()
    {
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        CheckCharacters = false,
        CloseInput = false,
        ConformanceLevel = ConformanceLevel.Fragment
    };

    private static readonly SearchValues<char> OffsetSigns = SearchValues.Create(['+', '-']);

    /// <summary>
    /// Reads every channel and programme in a document.
    /// </summary>
    /// <param name="stream">The XMLTV content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The items, in document order.</returns>
    public static IEnumerable<XmltvItem> Read(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = XmlReader.Create(stream, _settings);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool advanced;
            try
            {
                advanced = reader.Read();
            }
            catch (XmlException)
            {
                // A malformed tail should not discard everything already parsed.
                break;
            }

            if (!advanced)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (string.Equals(reader.Name, "channel", StringComparison.OrdinalIgnoreCase))
            {
                var channel = ReadChannel(reader);
                if (channel is not null)
                {
                    yield return new XmltvItem(channel, null);
                }
            }
            else if (string.Equals(reader.Name, "programme", StringComparison.OrdinalIgnoreCase))
            {
                var programme = ReadProgramme(reader);
                if (programme is not null)
                {
                    yield return new XmltvItem(null, programme);
                }
            }
        }
    }

    /// <summary>
    /// Parses an XMLTV timestamp.
    /// </summary>
    /// <param name="value">The raw value, such as <c>20261024060000 +0000</c>.</param>
    /// <returns>The parsed instant in UTC, or <c>null</c> when it is unusable.</returns>
    public static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        // An offset may be separated by a space or run straight on, and may be omitted entirely.
        var offset = TimeSpan.Zero;
        var hasOffset = false;

        var signIndex = text.AsSpan().LastIndexOfAny(OffsetSigns);
        if (signIndex > 0 && text.Length - signIndex >= 5)
        {
            var offsetText = text[signIndex..].Replace(":", string.Empty, StringComparison.Ordinal);
            if (offsetText.Length >= 5
                && int.TryParse(offsetText.AsSpan(1, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
                && int.TryParse(offsetText.AsSpan(3, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
            {
                offset = new TimeSpan(hours, minutes, 0);
                if (offsetText[0] == '-')
                {
                    offset = offset.Negate();
                }

                hasOffset = true;
                text = text[..signIndex].Trim();
            }
        }

        var digits = text.Replace(" ", string.Empty, StringComparison.Ordinal);

        // Guides pad to anywhere between year-only and full seconds precision.
        var formats = new[] { "yyyyMMddHHmmss", "yyyyMMddHHmm", "yyyyMMddHH", "yyyyMMdd", "yyyyMM", "yyyy" };

        if (!DateTime.TryParseExact(digits, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var fallback)
                ? fallback
                : null;
        }

        return hasOffset
            ? new DateTimeOffset(parsed, offset).UtcDateTime
            : DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    /// <summary>
    /// Parses an <c>episode-num</c> value.
    /// </summary>
    /// <param name="system">The numbering system the guide declared.</param>
    /// <param name="value">The raw value.</param>
    /// <returns>The season and episode numbers, either of which may be <c>null</c>.</returns>
    public static (int? Season, int? Episode) ParseEpisodeNumber(string? system, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var text = value.Trim();

        if (string.Equals(system, "xmltv_ns", StringComparison.OrdinalIgnoreCase))
        {
            // "season . episode . part", zero-based, any component possibly blank, and each
            // optionally written as "index/total".
            var parts = text.Split('.');
            var season = ParseZeroBased(parts.Length > 0 ? parts[0] : null);
            var episode = ParseZeroBased(parts.Length > 1 ? parts[1] : null);
            return (season, episode);
        }

        if (string.Equals(system, "onscreen", StringComparison.OrdinalIgnoreCase) || system is null)
        {
            var parsed = Catalog.TitleParser.ParseEpisode("Placeholder " + text);
            if (parsed is not null)
            {
                return (parsed.Value.SeasonNumber, parsed.Value.EpisodeNumber);
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var onlyEpisode))
            {
                return (null, onlyEpisode);
            }
        }

        return (null, null);

        static int? ParseZeroBased(string? component)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                return null;
            }

            var text = component.Trim();
            var slash = text.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                text = text[..slash];
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value + 1
                : null;
        }
    }

    private static XmltvChannel? ReadChannel(XmlReader reader)
    {
        var id = reader.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            reader.Skip();
            return null;
        }

        var channel = new XmltvChannel { Id = id.Trim() };

        if (reader.IsEmptyElement)
        {
            return channel;
        }

        using var subtree = reader.ReadSubtree();
        subtree.Read();

        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (subtree.Name.ToLowerInvariant())
            {
                case "display-name":
                    var displayName = subtree.ReadElementContentAsString();
                    if (string.IsNullOrWhiteSpace(channel.Name))
                    {
                        channel.Name = displayName.Trim();
                    }
                    else if (channel.Number is null && IsChannelNumber(displayName))
                    {
                        // Guides commonly emit a second display-name holding just the number.
                        channel.Number = displayName.Trim();
                    }

                    break;

                case "lcn":
                    channel.Number = subtree.ReadElementContentAsString().Trim();
                    break;

                case "icon":
                    channel.IconUrl ??= subtree.GetAttribute("src");
                    subtree.Skip();
                    break;

                default:
                    subtree.Skip();
                    break;
            }
        }

        return channel;
    }

    private static XmltvProgramme? ReadProgramme(XmlReader reader)
    {
        var channelId = reader.GetAttribute("channel");
        var start = ParseTimestamp(reader.GetAttribute("start"));
        var stop = ParseTimestamp(reader.GetAttribute("stop"));

        if (string.IsNullOrWhiteSpace(channelId) || start is null)
        {
            reader.Skip();
            return null;
        }

        // A missing stop is common on the last entry of a channel; assume a one-hour slot.
        var end = stop ?? start.Value.AddHours(1);

        var programme = new XmltvProgramme
        {
            ChannelId = channelId.Trim(),
            StartUnix = new DateTimeOffset(start.Value, TimeSpan.Zero).ToUnixTimeSeconds(),
            EndUnix = new DateTimeOffset(end, TimeSpan.Zero).ToUnixTimeSeconds()
        };

        if (reader.IsEmptyElement)
        {
            return programme;
        }

        // A list for order, because the first category is the one clients treat as the genre, and a
        // set alongside it because guides routinely repeat a category in several languages.
        var categories = new List<string>();
        var seenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var subtree = reader.ReadSubtree();
        subtree.Read();

        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (subtree.Name.ToLowerInvariant())
            {
                case "title":
                    programme.Title ??= Trim(subtree.ReadElementContentAsString());
                    break;

                case "sub-title":
                    programme.SubTitle ??= Trim(subtree.ReadElementContentAsString());
                    break;

                case "desc":
                    programme.Description ??= Trim(subtree.ReadElementContentAsString());
                    break;

                case "category":
                    var category = Trim(subtree.ReadElementContentAsString());
                    if (category is not null && seenCategories.Add(category))
                    {
                        categories.Add(category);
                    }

                    break;

                case "icon":
                    programme.IconUrl ??= subtree.GetAttribute("src");
                    subtree.Skip();
                    break;

                case "episode-num":
                {
                    var system = subtree.GetAttribute("system");
                    var value = subtree.ReadElementContentAsString();
                    var (season, episode) = ParseEpisodeNumber(system, value);
                    programme.SeasonNumber ??= season;
                    programme.EpisodeNumber ??= episode;
                    break;
                }

                case "date":
                {
                    var date = subtree.ReadElementContentAsString();
                    if (date.Length >= 4
                        && int.TryParse(date.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
                    {
                        programme.Year ??= year;
                    }

                    break;
                }

                case "rating":
                {
                    var value = ReadNestedValue(subtree);
                    programme.Rating ??= value;
                    break;
                }

                case "star-rating":
                {
                    var value = ReadNestedValue(subtree);
                    programme.StarRating ??= ParseStarRating(value);
                    break;
                }

                case "previously-shown":
                    programme.IsRepeat = true;
                    programme.OriginalAirDate ??= ParseTimestamp(subtree.GetAttribute("start"));
                    subtree.Skip();
                    break;

                case "new":
                    programme.IsNew = true;
                    subtree.Skip();
                    break;

                case "live":
                    programme.IsLive = true;
                    subtree.Skip();
                    break;

                case "premiere":
                    programme.IsPremiere = true;
                    subtree.Skip();
                    break;

                default:
                    subtree.Skip();
                    break;
            }
        }

        if (categories.Count > 0)
        {
            programme.Categories = categories;
        }

        return programme;
    }

    private static string? ReadNestedValue(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Skip();
            return null;
        }

        using var subtree = reader.ReadSubtree();
        subtree.Read();

        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.Element
                && string.Equals(subtree.Name, "value", StringComparison.OrdinalIgnoreCase))
            {
                return Trim(subtree.ReadElementContentAsString());
            }
        }

        return null;
    }

    private static double? ParseStarRating(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Written as "7/10", "3.5/5", or a bare number.
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
        {
            return null;
        }

        if (parts.Length > 1
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var outOf)
            && outOf > 0)
        {
            return Math.Round(score / outOf * 10d, 1);
        }

        return score;
    }

    private static bool IsChannelNumber(string value)
    {
        var text = value.Trim();
        return text.Length > 0 && text.Length <= 6 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static string? Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
