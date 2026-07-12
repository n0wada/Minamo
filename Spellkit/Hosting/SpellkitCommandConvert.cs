using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Threading.Tasks;
using EditorBrowsableAttribute = System.ComponentModel.EditorBrowsableAttribute;
using EditorBrowsableState = System.ComponentModel.EditorBrowsableState;

namespace Spellkit.Hosting;

public static class SpellkitCommandConvert
{
    public static SpkObject FromObject(object? value) =>
        value is SpkObject dyValue ? dyValue : TypeConverter.ConvertFrom(value);

    public static SpkObject FromObject<T>(T value) =>
        value is SpkObject spkValue
            ? spkValue
            : TypeConverter.ConvertFrom(value, typeof(T));

    internal static SpkObject FromObject(object? value, Type declaredType) =>
        value is SpkObject spkValue
            ? spkValue
            : TypeConverter.ConvertFrom(value, declaredType);

    public static SpkObject FromString(string? value) => SpkString.Get(value);

    public static SpkObject FromBoolean(bool value) => value ? SpkBool.True : SpkBool.False;

    public static SpkObject FromInteger(long value) => new SpkInteger(value);

    public static SpkObject FromFloat(double value) => new SpkFloat(value);

    public static SpkObject FromChar(char value) => new SpkChar(value);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SpkObject FromAwaitable(Task task)
    {
        Task.Run(async () => await task.ConfigureAwait(false)).GetAwaiter().GetResult();
        return SpkNil.Instance;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SpkObject FromAwaitable<T>(Task<T> task) =>
        FromObject(Task.Run(async () => await task.ConfigureAwait(false)).GetAwaiter().GetResult());

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SpkObject FromAwaitable(ValueTask task) => FromAwaitable(task.AsTask());

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SpkObject FromAwaitable<T>(ValueTask<T> task) => FromAwaitable(task.AsTask());

    public static T? ToObject<T>(ExecutionContext context, SpkObject value) =>
        TypeConverter.ConvertTo<T>(context, value);

    public static object? ToObject(ExecutionContext context, SpkObject value) =>
        TypeConverter.ConvertTo(context, value, typeof(object));

    public static SpkObject ToSpkObject(ExecutionContext _, SpkObject value) => value;

    public static string ToString(ExecutionContext context, SpkObject value)
    {
        if (value is SpkString text)
        {
            return text.Value;
        }

        if (value is SpkChar character)
        {
            return character.Value.ToString();
        }

        context.InvalidCast(value.TypeName, typeof(string).FullName!);
        return default!;
    }

    public static bool ToBoolean(ExecutionContext _, SpkObject value) => value.IsTrue();

    public static char ToChar(ExecutionContext context, SpkObject value)
    {
        if (value is SpkString text)
        {
            return string.IsNullOrEmpty(text.Value) ? '\0' : text.Value[0];
        }

        if (value is SpkChar character)
        {
            return character.Value;
        }

        context.InvalidCast(value.TypeName, typeof(char).FullName!);
        return default;
    }

    public static byte ToByte(ExecutionContext context, SpkObject value) =>
        ConvertInteger(context, value, static number => checked((byte)number));

    public static short ToInt16(ExecutionContext context, SpkObject value) =>
        ConvertInteger(context, value, static number => checked((short)number));

    public static int ToInt32(ExecutionContext context, SpkObject value) =>
        ConvertInteger(context, value, static number => checked((int)number));

    public static long ToInt64(ExecutionContext context, SpkObject value) =>
        ConvertInteger(context, value, static number => number);

    public static sbyte ToSByte(ExecutionContext context, SpkObject value) =>
        ConvertInteger(context, value, static number => checked((sbyte)number));

    public static ushort ToUInt16(ExecutionContext context, SpkObject value) =>
        ConvertInteger(context, value, static number => checked((ushort)number));

    public static uint ToUInt32(ExecutionContext context, SpkObject value) =>
        ConvertInteger(context, value, static number => checked((uint)number));

    public static ulong ToUInt64(ExecutionContext context, SpkObject value) =>
        ConvertInteger(context, value, static number => checked((ulong)number));

    public static float ToSingle(ExecutionContext context, SpkObject value)
    {
        var number = ToFloat(context, value, typeof(float));
        if (!context.HasErrors && double.IsFinite(number) && Math.Abs(number) > float.MaxValue)
        {
            context.Overflow();
            return default;
        }
        return (float)number;
    }

    public static double ToDouble(ExecutionContext context, SpkObject value) => ToFloat(context, value, typeof(double));

    public static decimal ToDecimal(ExecutionContext context, SpkObject value)
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
        SpkObject value,
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

    private static long ToInteger(ExecutionContext context, SpkObject value, Type targetType)
    {
        if (value is SpkInteger integer)
        {
            return integer.Value;
        }

        if (value is SpkFloat number)
        {
            return checked((long)number.Value);
        }

        if (value is SpkChar character)
        {
            return character.Value;
        }

        context.InvalidCast(value.TypeName, targetType.FullName!);
        return default;
    }

    private static double ToFloat(ExecutionContext context, SpkObject value, Type targetType)
    {
        if (value is SpkFloat number)
        {
            return number.Value;
        }

        if (value is SpkInteger integer)
        {
            return integer.Value;
        }

        if (value is SpkChar character)
        {
            return character.Value;
        }

        context.InvalidCast(value.TypeName, targetType.FullName!);
        return default;
    }
}
