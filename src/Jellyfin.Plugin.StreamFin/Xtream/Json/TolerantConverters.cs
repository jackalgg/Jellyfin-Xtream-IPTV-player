using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.StreamFin.Xtream.Json;

/// <summary>
/// Reads a number that a panel might have sent as a JSON number, a quoted string, an empty string,
/// a boolean, or null.
/// </summary>
/// <remarks>
/// Xtream panels are PHP applications with no schema. The same field comes back as <c>1234</c> on
/// one panel, <c>"1234"</c> on the next, and <c>""</c> or <c>"N/A"</c> when the value is missing.
/// A strict deserializer throws on the whole response, which is why plugins that use one appear to
/// "load nothing" against some providers.
/// </remarks>
public sealed class TolerantInt64Converter : JsonConverter<long?>
{
    /// <inheritdoc />
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonValueReader.ReadInt64(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>
/// <see cref="TolerantInt64Converter"/> for 32-bit values.
/// </summary>
public sealed class TolerantInt32Converter : JsonConverter<int?>
{
    /// <inheritdoc />
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = JsonValueReader.ReadInt64(ref reader);
        if (value is null || value > int.MaxValue || value < int.MinValue)
        {
            return null;
        }

        return (int)value.Value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>
/// <see cref="TolerantInt64Converter"/> for floating point values.
/// </summary>
public sealed class TolerantDoubleConverter : JsonConverter<double?>
{
    /// <inheritdoc />
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonValueReader.ReadDouble(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>
/// Reads a flag sent as <c>true</c>, <c>1</c>, <c>"1"</c>, <c>"true"</c>, <c>"yes"</c>, or their negatives.
/// </summary>
public sealed class TolerantBooleanConverter : JsonConverter<bool>
{
    /// <inheritdoc />
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonValueReader.ReadBoolean(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);
}

/// <summary>
/// Reads a string field that a panel might have sent as a number or a boolean.
/// </summary>
public sealed class TolerantStringConverter : JsonConverter<string?>
{
    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonValueReader.ReadString(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

/// <summary>
/// Reads a field that is sometimes a single string and sometimes an array of them, which is how
/// panels report <c>backdrop_path</c>.
/// </summary>
public sealed class StringOrArrayConverter : JsonConverter<IReadOnlyList<string>>
{
    /// <inheritdoc />
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return Array.Empty<string>();

            case JsonTokenType.String:
            case JsonTokenType.Number:
            {
                var single = JsonValueReader.ReadString(ref reader);
                return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
            }

            case JsonTokenType.StartArray:
            {
                var items = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.StartArray || reader.TokenType == JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    var item = JsonValueReader.ReadString(ref reader);
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        items.Add(item);
                    }
                }

                return items;
            }

            default:
                reader.Skip();
                return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }
}

/// <summary>
/// Reads a map that a panel serializes as <c>[]</c> when it is empty, which PHP's
/// <c>json_encode</c> does for an empty array.
/// </summary>
/// <typeparam name="TValue">The map's value type.</typeparam>
public sealed class LooseDictionaryConverter<TValue> : JsonConverter<IReadOnlyDictionary<string, TValue>>
{
    /// <inheritdoc />
    public override IReadOnlyDictionary<string, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new Dictionary<string, TValue>();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            // PHP renders both an empty array and an empty map as `[]`. A populated array keyed by
            // position is still meaningful for episode maps, so index it.
            var byIndex = new Dictionary<string, TValue>();
            var index = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var element = JsonSerializer.Deserialize<TValue>(ref reader, options);
                if (element is not null)
                {
                    byIndex[index.ToString(CultureInfo.InvariantCulture)] = element;
                }

                index++;
            }

            return byIndex;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return new Dictionary<string, TValue>();
        }

        var result = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString() ?? string.Empty;
            reader.Read();
            var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
            if (value is not null)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, TValue> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value)
        {
            writer.WritePropertyName(pair.Key);
            JsonSerializer.Serialize(writer, pair.Value, options);
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// Reads a list that a panel serializes as a single object when it holds exactly one entry.
/// </summary>
/// <typeparam name="TValue">The list's element type.</typeparam>
public sealed class LooseListConverter<TValue> : JsonConverter<IReadOnlyList<TValue>>
{
    /// <inheritdoc />
    public override IReadOnlyList<TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return Array.Empty<TValue>();
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Either a single element, or a map keyed by index. Both appear in the wild.
            var items = new List<TValue>();
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            var single = root.Deserialize<TValue>(options);
            if (single is not null && LooksLikeElement(root))
            {
                items.Add(single);
                return items;
            }

            foreach (var property in root.EnumerateObject())
            {
                var value = property.Value.Deserialize<TValue>(options);
                if (value is not null)
                {
                    items.Add(value);
                }
            }

            return items;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return Array.Empty<TValue>();
        }

        var list = new List<TValue>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var element = JsonSerializer.Deserialize<TValue>(ref reader, options);
            if (element is not null)
            {
                list.Add(element);
            }
        }

        return list;
    }

    private static bool LooksLikeElement(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            // A map keyed by index or season number has purely numeric keys.
            if (!long.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return true;
            }

            return false;
        }

        return false;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<TValue> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }

        writer.WriteEndArray();
    }
}
