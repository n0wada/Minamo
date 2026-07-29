using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Threading.Tasks;
using EditorBrowsableAttribute = System.ComponentModel.EditorBrowsableAttribute;
using EditorBrowsableState = System.ComponentModel.EditorBrowsableState;

namespace Spellkit.Hosting;

public static class SpellkitCommandConvert
{
    public static SpellkitObject FromObject(object? value) =>
        value is SpellkitObject dyValue ? dyValue : TypeConverter.ConvertFrom(value);

    public static SpellkitObject FromObject<T>(T value) =>
        value is SpellkitObject SpellkitValue
            ? SpellkitValue
            : TypeConverter.ConvertFrom(value, typeof(T));

    internal static SpellkitObject FromObject(object? value, Type declaredType) =>
        value is SpellkitObject SpellkitValue
            ? SpellkitValue
            : TypeConverter.ConvertFrom(value, declaredType);

    public static SpellkitObject FromString(string? value) => SpellkitString.Get(value);

    public static SpellkitObject FromBoolean(bool value) => value ? SpellkitBool.True : SpellkitBool.False;

    public static SpellkitObject FromInteger(long value) => new SpellkitInteger(value);

    public static SpellkitObject FromFloat(double value) => new SpellkitFloat(value);

    public static SpellkitObject FromChar(char value) => new SpellkitChar(value);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SpellkitObject FromAwaitable(Task task)
    {
        Task.Run(async () => await task.ConfigureAwait(false)).GetAwaiter().GetResult();
        return SpellkitNil.Instance;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SpellkitObject FromAwaitable<T>(Task<T> task) =>
        FromObject(Task.Run(async () => await task.ConfigureAwait(false)).GetAwaiter().GetResult());

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SpellkitObject FromAwaitable(ValueTask task) => FromAwaitable(task.AsTask());

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SpellkitObject FromAwaitable<T>(ValueTask<T> task) => FromAwaitable(task.AsTask());

    public static T? ToObject<T>(ExecutionContext context, SpellkitObject value) =>
        TypeConverter.ConvertTo<T>(context, value);

    public static object? ToObject(ExecutionContext context, SpellkitObject value) =>
        TypeConverter.ConvertTo(context, value, typeof(object));

    public static SpellkitObject ToSpellkitObject(ExecutionContext _, SpellkitObject value) => value;

    public static string ToString(ExecutionContext context, SpellkitObject value)
    {
        if (value is SpellkitString text)
        {
            return text.Value;
        }

        if (value is SpellkitChar character)
        {
            return character.Value.ToString();
        }

        context.InvalidCast(value.TypeName, typeof(string).FullName!);
        return default!;
    }

    public static bool ToBoolean(ExecutionContext _, SpellkitObject value) => value.IsTrue();

    public static char ToChar(ExecutionContext context, SpellkitObject value)
    {
        if (value is SpellkitString text)
        {
            return string.IsNullOrEmpty(text.Value) ? '\0' : text.Value[0];
        }

        if (value is SpellkitChar character)
        {
            return character.Value;
        }

        context.InvalidCast(value.TypeName, typeof(char).FullName!);
        return default;
    }

    public static byte ToByte(ExecutionContext context, SpellkitObject value) =>
        ConvertInteger(context, value, static number => checked((byte)number));

    public static short ToInt16(ExecutionContext context, SpellkitObject value) =>
        ConvertInteger(context, value, static number => checked((short)number));

    public static int ToInt32(ExecutionContext context, SpellkitObject value) =>
        ConvertInteger(context, value, static number => checked((int)number));

    public static long ToInt64(ExecutionContext context, SpellkitObject value) =>
        ConvertInteger(context, value, static number => number);

    public static sbyte ToSByte(ExecutionContext context, SpellkitObject value) =>
        ConvertInteger(context, value, static number => checked((sbyte)number));

    public static ushort ToUInt16(ExecutionContext context, SpellkitObject value) =>
        ConvertInteger(context, value, static number => checked((ushort)number));

    public static uint ToUInt32(ExecutionContext context, SpellkitObject value) =>
        ConvertInteger(context, value, static number => checked((uint)number));

    public static ulong ToUInt64(ExecutionContext context, SpellkitObject value) =>
        ConvertInteger(context, value, static number => checked((ulong)number));

    public static float ToSingle(ExecutionContext context, SpellkitObject value)
    {
        var number = ToFloat(context, value, typeof(float));
        if (!context.HasErrors && double.IsFinite(number) && Math.Abs(number) > float.MaxValue)
        {
            context.Overflow();
            return default;
        }
        return (float)number;
    }

    public static double ToDouble(ExecutionContext context, SpellkitObject value) => ToFloat(context, value, typeof(double));

    public static decimal ToDecimal(ExecutionContext context, SpellkitObject value)
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
        SpellkitObject value,
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

    private static long ToInteger(ExecutionContext context, SpellkitObject value, Type targetType)
    {
        if (value is SpellkitInteger integer)
        {
            return integer.Value;
        }

        if (value is SpellkitFloat number)
        {
            return checked((long)number.Value);
        }

        if (value is SpellkitChar character)
        {
            return character.Value;
        }

        context.InvalidCast(value.TypeName, targetType.FullName!);
        return default;
    }

    private static double ToFloat(ExecutionContext context, SpellkitObject value, Type targetType)
    {
        if (value is SpellkitFloat number)
        {
            return number.Value;
        }

        if (value is SpellkitInteger integer)
        {
            return integer.Value;
        }

        if (value is SpellkitChar character)
        {
            return character.Value;
        }

        context.InvalidCast(value.TypeName, targetType.FullName!);
        return default;
    }
}
