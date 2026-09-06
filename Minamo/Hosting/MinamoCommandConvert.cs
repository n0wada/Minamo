using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Threading.Tasks;
using EditorBrowsableAttribute = System.ComponentModel.EditorBrowsableAttribute;
using EditorBrowsableState = System.ComponentModel.EditorBrowsableState;

namespace Minamo.Hosting;

public static class MinamoCommandConvert
{
    public static MinamoObject FromObject(object? value) =>
        value is MinamoObject dyValue ? dyValue : TypeConverter.ConvertFrom(value);

    public static MinamoObject FromObject<T>(T value) =>
        value is MinamoObject MinamoValue
            ? MinamoValue
            : TypeConverter.ConvertFrom(value, typeof(T));

    internal static MinamoObject FromObject(object? value, Type declaredType) =>
        value is MinamoObject MinamoValue
            ? MinamoValue
            : TypeConverter.ConvertFrom(value, declaredType);

    public static MinamoObject FromString(string? value) => MinamoString.Get(value);

    public static MinamoObject FromBoolean(bool value) => value ? MinamoBool.True : MinamoBool.False;

    public static MinamoObject FromInteger(long value) => new MinamoInteger(value);

    public static MinamoObject FromFloat(double value) => new MinamoFloat(value);

    public static MinamoObject FromChar(char value) => new MinamoChar(value);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static MinamoObject FromAwaitable(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return FromAwaitableCore(
            task,
            () =>
            {
                task.GetAwaiter().GetResult();
                return MinamoNil.Instance;
            });
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static MinamoObject FromAwaitable<T>(Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return FromAwaitableCore(task, () => FromObject(task.GetAwaiter().GetResult()));
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static MinamoObject FromAwaitable(ValueTask task)
    {
        if (task.IsCompletedSuccessfully)
        {
            task.GetAwaiter().GetResult();
            return MinamoNil.Instance;
        }

        return FromAwaitable(task.AsTask());
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static MinamoObject FromAwaitable<T>(ValueTask<T> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            return FromObject(task.GetAwaiter().GetResult());
        }

        return FromAwaitable(task.AsTask());
    }

    internal static MinamoObject FromAwaitable(Task task, Type resultType)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(resultType);
        return FromAwaitableCore(
            task,
            () =>
            {
                task.GetAwaiter().GetResult();
                var value = task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task);
                return FromObject(value, resultType);
            });
    }

    private static MinamoObject FromAwaitableCore(
        Task task,
        Func<MinamoObject> getResult) =>
        task.IsCompleted
            ? getResult()
            : new MinamoAwaitable(task, getResult);

    public static T? ToObject<T>(ExecutionContext context, MinamoObject value) =>
        TypeConverter.ConvertTo<T>(context, value);

    public static object? ToObject(ExecutionContext context, MinamoObject value) =>
        TypeConverter.ConvertTo(context, value, typeof(object));

    public static MinamoObject ToMinamoObject(ExecutionContext _, MinamoObject value) => value;

    public static string ToString(ExecutionContext context, MinamoObject value)
    {
        if (value is MinamoString text)
        {
            return text.Value;
        }

        if (value is MinamoChar character)
        {
            return character.Value.ToString();
        }

        context.InvalidCast(value.TypeName, typeof(string).FullName!);
        return default!;
    }

    public static bool ToBoolean(ExecutionContext _, MinamoObject value) => value.IsTrue();

    public static char ToChar(ExecutionContext context, MinamoObject value)
    {
        if (value is MinamoString text)
        {
            return string.IsNullOrEmpty(text.Value) ? '\0' : text.Value[0];
        }

        if (value is MinamoChar character)
        {
            return character.Value;
        }

        context.InvalidCast(value.TypeName, typeof(char).FullName!);
        return default;
    }

    public static byte ToByte(ExecutionContext context, MinamoObject value) =>
        ConvertInteger(context, value, static number => checked((byte)number));

    public static short ToInt16(ExecutionContext context, MinamoObject value) =>
        ConvertInteger(context, value, static number => checked((short)number));

    public static int ToInt32(ExecutionContext context, MinamoObject value) =>
        ConvertInteger(context, value, static number => checked((int)number));

    public static long ToInt64(ExecutionContext context, MinamoObject value) =>
        ConvertInteger(context, value, static number => number);

    public static sbyte ToSByte(ExecutionContext context, MinamoObject value) =>
        ConvertInteger(context, value, static number => checked((sbyte)number));

    public static ushort ToUInt16(ExecutionContext context, MinamoObject value) =>
        ConvertInteger(context, value, static number => checked((ushort)number));

    public static uint ToUInt32(ExecutionContext context, MinamoObject value) =>
        ConvertInteger(context, value, static number => checked((uint)number));

    public static ulong ToUInt64(ExecutionContext context, MinamoObject value) =>
        ConvertInteger(context, value, static number => checked((ulong)number));

    public static float ToSingle(ExecutionContext context, MinamoObject value)
    {
        var number = ToFloat(context, value, typeof(float));
        if (!context.HasErrors && double.IsFinite(number) && Math.Abs(number) > float.MaxValue)
        {
            context.Overflow();
            return default;
        }
        return (float)number;
    }

    public static double ToDouble(ExecutionContext context, MinamoObject value) => ToFloat(context, value, typeof(double));

    public static decimal ToDecimal(ExecutionContext context, MinamoObject value)
    {
        try
        {
            return checked((decimal)ToFloat(context, value, typeof(decimal)));
        }
        catch (OverflowException)
        {
            context.Overflow();
            return default;
        }
    }

    private static T ConvertInteger<T>(
        ExecutionContext context,
        MinamoObject value,
        Func<long, T> convert)
    {
        try
        {
            return convert(ToInteger(context, value, typeof(T)));
        }
        catch (OverflowException)
        {
            context.Overflow();
            return default!;
        }
    }

    private static long ToInteger(ExecutionContext context, MinamoObject value, Type targetType)
    {
        if (value is MinamoInteger integer)
        {
            return integer.Value;
        }

        if (value is MinamoFloat number)
        {
            return checked((long)number.Value);
        }

        if (value is MinamoChar character)
        {
            return character.Value;
        }

        context.InvalidCast(value.TypeName, targetType.FullName!);
        return default;
    }

    private static double ToFloat(ExecutionContext context, MinamoObject value, Type targetType)
    {
        if (value is MinamoFloat number)
        {
            return number.Value;
        }

        if (value is MinamoInteger integer)
        {
            return integer.Value;
        }

        if (value is MinamoChar character)
        {
            return character.Value;
        }

        context.InvalidCast(value.TypeName, targetType.FullName!);
        return default;
    }
}

internal static class MinamoHostValueConverter
{
    internal static bool TryConvert<T>(MinamoObject? value, out T? result)
    {
        if (value is null)
        {
            result = default;
            return false;
        }

        if (value.TypeId == MinamoTypeCodes.Nil)
        {
            result = default;
            return true;
        }

        if (TypeConverter.TryConvert(value, typeof(T), out var converted))
        {
            result = (T?)converted;
            return true;
        }

        result = default;
        return false;
    }

    internal static T? Convert<T>(MinamoObject? value, string valueName)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"{valueName} is not available.");
        }

        if (TryConvert<T>(value, out var result))
        {
            return result;
        }

        throw new InvalidCastException(
            $"{valueName} of type '{value.TypeName}' cannot be converted to '{typeof(T).FullName}'.");
    }
}
