using System;
using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Plugin.StreamFin.Xtream.Json;

/// <summary>
/// Shared scalar coercion used by the tolerant converters.
/// </summary>
internal static class JsonValueReader
{
    private static readonly string[] _nullishStrings = { string.Empty, "n/a", "na", "null", "none", "-", "undefined" };

    internal static long? ReadInt64(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var number))
                {
                    return number;
                }

                return reader.TryGetDouble(out var asDouble) ? (long)asDouble : null;

            case JsonTokenType.String:
            {
                var text = reader.GetString();
                if (IsNullish(text))
                {
                    return null;
                }

                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble)
                    ? (long)parsedDouble
                    : null;
            }

            case JsonTokenType.True:
                return 1;

            case JsonTokenType.False:
                return 0;

            case JsonTokenType.Null:
                return null;

            default:
                reader.Skip();
                return null;
        }
    }

    internal static double? ReadDouble(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetDouble(out var number) ? number : null;

            case JsonTokenType.String:
            {
                var text = reader.GetString();
                if (IsNullish(text))
                {
                    return null;
                }

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            }

            case JsonTokenType.True:
                return 1d;

            case JsonTokenType.False:
                return 0d;

            case JsonTokenType.Null:
                return null;

            default:
                reader.Skip();
                return null;
        }
    }

    internal static bool ReadBoolean(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;

            case JsonTokenType.False:
            case JsonTokenType.Null:
                return false;

            case JsonTokenType.Number:
                return reader.TryGetDouble(out var number) && Math.Abs(number) > double.Epsilon;

            case JsonTokenType.String:
            {
                var text = reader.GetString()?.Trim();
                if (IsNullish(text))
                {
                    return false;
                }

                if (bool.TryParse(text, out var parsed))
                {
                    return parsed;
                }

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
                {
                    return Math.Abs(numeric) > double.Epsilon;
                }

                return string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "on", StringComparison.OrdinalIgnoreCase);
            }

            default:
                reader.Skip();
                return false;
        }
    }

    internal static string? ReadString(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            {
                var text = reader.GetString();
                return IsNullish(text) ? null : text;
            }

            case JsonTokenType.Number:
                return reader.TryGetInt64(out var number)
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);

            case JsonTokenType.True:
                return "true";

            case JsonTokenType.False:
                return "false";

            case JsonTokenType.Null:
                return null;

            default:
                reader.Skip();
                return null;
        }
    }

    private static bool IsNullish(string? value)
    {
        if (value is null)
        {
            return true;
        }

        var trimmed = value.Trim();
        foreach (var candidate in _nullishStrings)
        {
            if (string.Equals(trimmed, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
