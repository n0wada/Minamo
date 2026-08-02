using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Text;
using System.Text.Json;

namespace Spellkit.Library.Json;

internal static class SpellkitJson
{
    private const int MaxDepth = 64;

    internal static SpellkitObject Parse(ExecutionContext ctx, string value) =>
        Parse(ctx, Encoding.UTF8.GetBytes(value));

    internal static SpellkitObject Parse(ExecutionContext ctx, ReadOnlyMemory<byte> value)
    {
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = MaxDepth });
            return ConvertFrom(document.RootElement);
        }
        catch (JsonException ex)
        {
            return ctx.ParsingFailed(ex.Message);
        }
    }

    internal static string? Stringify(ExecutionContext ctx, SpellkitObject value, bool indented = false)
    {
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = indented,
                MaxDepth = MaxDepth
            }))
            {
                var active = new HashSet<SpellkitObject>(ReferenceEqualityComparer.Instance);
                if (!Write(ctx, writer, value, active))
                {
                    return null;
                }
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (ArgumentException)
        {
            if (!ctx.HasErrors)
            {
                ctx.InvalidValue("JSON value");
            }
            return null;
        }
        catch (InvalidOperationException)
        {
            if (!ctx.HasErrors)
            {
                ctx.InvalidValue("JSON value");
            }
            return null;
        }
    }

    private static SpellkitObject ConvertFrom(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Object => ConvertObject(value),
            JsonValueKind.Array => new SpellkitArray(value.EnumerateArray().Select(ConvertFrom).ToArray()),
            JsonValueKind.String => SpellkitString.Get(value.GetString()),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => SpellkitInteger.Get(integer),
            JsonValueKind.Number => new SpellkitFloat(value.GetDouble()),
            JsonValueKind.True => True,
            JsonValueKind.False => False,
            JsonValueKind.Null => Nil,
            _ => Nil
        };

    private static SpellkitObject ConvertObject(JsonElement value)
    {
        var result = new SpellkitDictionary();
        foreach (var property in value.EnumerateObject())
        {
            result[SpellkitString.Get(property.Name)] = ConvertFrom(property.Value);
        }
        return result;
    }

    private static bool Write(
        ExecutionContext ctx,
        Utf8JsonWriter writer,
        SpellkitObject value,
        HashSet<SpellkitObject> active)
    {
        if (value.TypeId == SpellkitTypeCodes.Nil)
        {
            writer.WriteNullValue();
            return true;
        }

        switch (value)
        {
            case SpellkitString text:
                writer.WriteStringValue(text.Value);
                return true;
            case SpellkitChar character:
                writer.WriteStringValue(character.Value.ToString());
                return true;
            case SpellkitInteger integer:
                writer.WriteNumberValue(integer.Value);
                return true;
            case SpellkitFloat number when double.IsFinite(number.Value):
                writer.WriteNumberValue(number.Value);
                return true;
            case SpellkitFloat:
                ctx.InvalidValue(value);
                return false;
            case SpellkitBool boolean:
                writer.WriteBooleanValue((bool)boolean);
                return true;
            case SpellkitDictionary dictionary:
                return WriteDictionary(ctx, writer, dictionary, active);
            case SpellkitTuple tuple:
                return WriteTuple(ctx, writer, tuple, active);
            case SpellkitArray array:
                return WriteArray(ctx, writer, array, active);
            default:
                ctx.InvalidType(value);
                return false;
        }
    }

    private static bool WriteDictionary(
        ExecutionContext ctx,
        Utf8JsonWriter writer,
        SpellkitDictionary dictionary,
        HashSet<SpellkitObject> active)
    {
        if (!Enter(ctx, dictionary, active))
        {
            return false;
        }

        writer.WriteStartObject();
        foreach (var item in dictionary)
        {
            if (item is not SpellkitTuple pair || pair.Count < 2 || !TryGetKey(ctx, pair[0], out var key))
            {
                active.Remove(dictionary);
                return false;
            }
            writer.WritePropertyName(key);
            if (!Write(ctx, writer, pair[1], active))
            {
                active.Remove(dictionary);
                return false;
            }
        }
        writer.WriteEndObject();
        active.Remove(dictionary);
        return true;
    }

    private static bool WriteTuple(
        ExecutionContext ctx,
        Utf8JsonWriter writer,
        SpellkitTuple tuple,
        HashSet<SpellkitObject> active)
    {
        if (!Enter(ctx, tuple, active))
        {
            return false;
        }

        var objectShape = tuple.Count > 0 && Enumerable.Range(0, tuple.Count).All(index => tuple.GetKey(index) is not null);
        if (objectShape)
        {
            writer.WriteStartObject();
            for (var i = 0; i < tuple.Count; i++)
            {
                writer.WritePropertyName(tuple.GetKey(i)!);
                if (!Write(ctx, writer, tuple[i], active))
                {
                    active.Remove(tuple);
                    return false;
                }
            }
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteStartArray();
            for (var i = 0; i < tuple.Count; i++)
            {
                if (!Write(ctx, writer, tuple[i], active))
                {
                    active.Remove(tuple);
                    return false;
                }
            }
            writer.WriteEndArray();
        }
        active.Remove(tuple);
        return true;
    }

    private static bool WriteArray(
        ExecutionContext ctx,
        Utf8JsonWriter writer,
        SpellkitArray array,
        HashSet<SpellkitObject> active)
    {
        if (!Enter(ctx, array, active))
        {
            return false;
        }

        writer.WriteStartArray();
        foreach (var item in array)
        {
            if (!Write(ctx, writer, item, active))
            {
                active.Remove(array);
                return false;
            }
        }
        writer.WriteEndArray();
        active.Remove(array);
        return true;
    }

    private static bool Enter(
        ExecutionContext ctx,
        SpellkitObject value,
        HashSet<SpellkitObject> active)
    {
        if (active.Add(value))
        {
            return true;
        }
        ctx.InvalidValue("Cyclic JSON value");
        return false;
    }

    private static bool TryGetKey(ExecutionContext ctx, SpellkitObject value, out string key)
    {
        if (value is SpellkitString text)
        {
            key = text.Value;
            return true;
        }
        if (value is SpellkitChar character)
        {
            key = character.Value.ToString();
            return true;
        }
        ctx.InvalidValue(value);
        key = string.Empty;
        return false;
    }
}
