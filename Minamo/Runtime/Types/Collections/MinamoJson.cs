using Minamo.Codegen;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Minamo.Runtime;

[MinamoType]
public sealed partial class MinamoJsonTypeInfo : MinamoForeignTypeInfo
{
    public override string ReflectedTypeName => "Json";

    [MinamoStaticMethod]
    internal static MinamoObject Parse(ExecutionContext ctx, string value) =>
        MinamoJson.Parse(ctx, value);

    [MinamoStaticMethod]
    internal static MinamoObject Stringify(
        ExecutionContext ctx,
        MinamoObject value,
        bool indented = false) =>
        MinamoJson.Stringify(ctx, value, indented) is { } result
            ? MinamoString.Get(result)
            : Nil;
}

public static class MinamoJson
{
    private const int MaxDepth = 64;

    public static MinamoObject Parse(ExecutionContext ctx, string value) =>
        Parse(ctx, Encoding.UTF8.GetBytes(value));

    public static MinamoObject Parse(ExecutionContext ctx, ReadOnlyMemory<byte> value)
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

    public static string? Stringify(ExecutionContext ctx, MinamoObject value, bool indented = false)
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
                var active = new HashSet<MinamoObject>(ReferenceEqualityComparer.Instance);
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

    private static MinamoObject ConvertFrom(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Object => ConvertObject(value),
            JsonValueKind.Array => new MinamoArray(value.EnumerateArray().Select(ConvertFrom).ToArray()),
            JsonValueKind.String => MinamoString.Get(value.GetString()),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => MinamoInteger.Get(integer),
            JsonValueKind.Number => new MinamoFloat(value.GetDouble()),
            JsonValueKind.True => True,
            JsonValueKind.False => False,
            JsonValueKind.Null => Nil,
            _ => Nil
        };

    private static MinamoObject ConvertObject(JsonElement value)
    {
        var result = new MinamoDictionary();
        foreach (var property in value.EnumerateObject())
        {
            result[MinamoString.Get(property.Name)] = ConvertFrom(property.Value);
        }
        return result;
    }

    private static bool Write(
        ExecutionContext ctx,
        Utf8JsonWriter writer,
        MinamoObject value,
        HashSet<MinamoObject> active)
    {
        if (value.TypeId == MinamoTypeCodes.Nil)
        {
            writer.WriteNullValue();
            return true;
        }

        switch (value)
        {
            case MinamoString text:
                writer.WriteStringValue(text.Value);
                return true;
            case MinamoChar character:
                writer.WriteStringValue(character.Value.ToString());
                return true;
            case MinamoInteger integer:
                writer.WriteNumberValue(integer.Value);
                return true;
            case MinamoFloat number when double.IsFinite(number.Value):
                writer.WriteNumberValue(number.Value);
                return true;
            case MinamoFloat:
                ctx.InvalidValue(value);
                return false;
            case MinamoBool boolean:
                writer.WriteBooleanValue((bool)boolean);
                return true;
            case MinamoDictionary dictionary:
                return WriteDictionary(ctx, writer, dictionary, active);
            case MinamoTuple tuple:
                return WriteTuple(ctx, writer, tuple, active);
            case MinamoArray array:
                return WriteArray(ctx, writer, array, active);
            default:
                ctx.InvalidType(value);
                return false;
        }
    }

    private static bool WriteDictionary(ExecutionContext ctx, Utf8JsonWriter writer, MinamoDictionary dictionary, HashSet<MinamoObject> active)
    {
        if (!Enter(ctx, dictionary, active)) return false;
        writer.WriteStartObject();
        foreach (var item in dictionary)
        {
            if (item is not MinamoTuple pair || pair.Count < 2 || !TryGetKey(ctx, pair[0], out var key))
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

    private static bool WriteTuple(ExecutionContext ctx, Utf8JsonWriter writer, MinamoTuple tuple, HashSet<MinamoObject> active)
    {
        if (!Enter(ctx, tuple, active)) return false;
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

    private static bool WriteArray(ExecutionContext ctx, Utf8JsonWriter writer, MinamoArray array, HashSet<MinamoObject> active)
    {
        if (!Enter(ctx, array, active)) return false;
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

    private static bool Enter(ExecutionContext ctx, MinamoObject value, HashSet<MinamoObject> active)
    {
        if (active.Add(value)) return true;
        ctx.InvalidValue("Cyclic JSON value");
        return false;
    }

    private static bool TryGetKey(ExecutionContext ctx, MinamoObject value, out string key)
    {
        if (value is MinamoString text)
        {
            key = text.Value;
            return true;
        }
        if (value is MinamoChar character)
        {
            key = character.Value.ToString();
            return true;
        }
        ctx.InvalidValue(value);
        key = string.Empty;
        return false;
    }
}
