using Spellkit.Runtime.Types;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Spellkit.Runtime;

public static class TypeConverter
{
    public static SpellkitObject ConvertFrom<T>(T obj) => ConvertFrom(obj, typeof(T), false);

    public static SpellkitObject ConvertFrom(object? obj) =>
        ConvertFrom(obj, obj?.GetType()!, true);

    internal static SpellkitObject ConvertFrom(object? obj, Type type) =>
        ConvertFrom(obj, type, false);

    private static SpellkitObject ConvertFrom(object? obj, Type type, bool runtimeInterop)
    {
        if (obj is null)
        {
            return SpellkitNil.Instance;
        }

        if (obj is SpellkitObject retval)
        {
            return retval;
        }

        if (!type.IsInstanceOfType(obj))
        {
            throw new InvalidCastException(
                $"Value of type '{obj.GetType().FullName}' cannot be exposed as '{type.FullName}'.");
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean: return (bool)obj ? True : False;
            case TypeCode.Byte: return new SpellkitInteger((byte)obj);
            case TypeCode.Int16: return new SpellkitInteger((short)obj);
            case TypeCode.Int32: return new SpellkitInteger((int)obj);
            case TypeCode.Int64: return new SpellkitInteger((long)obj);
            case TypeCode.SByte: return new SpellkitInteger((sbyte)obj);
            case TypeCode.UInt16: return new SpellkitInteger((ushort)obj);
            case TypeCode.UInt32: return new SpellkitInteger((uint)obj);
            case TypeCode.UInt64:
                var unsigned = (ulong)obj;
                return unsigned <= long.MaxValue
                    ? new SpellkitInteger((long)unsigned)
                    : new SpellkitInterop(type, obj);
            case TypeCode.String:
            case TypeCode.Char: return SpellkitString.Get(obj.ToString());
            case TypeCode.Single: return new SpellkitFloat((float)obj);
            case TypeCode.Double: return new SpellkitFloat((double)obj);
            case TypeCode.Decimal: return new SpellkitFloat((double)(decimal)obj);
            case TypeCode.Empty: return SpellkitNil.Instance;
            default:
                if (obj is IDictionary map)
                {
                    var dictionaryType = FindGenericInterface(type, typeof(IDictionary<,>))
                        ?? FindGenericInterface(type, typeof(IReadOnlyDictionary<,>));
                    var genericArguments = dictionaryType?.GetGenericArguments();
                    var keyType = genericArguments?[0] ?? typeof(object);
                    var valueType = genericArguments?[1] ?? typeof(object);
                    var dict = new SpellkitDictionary();
                    foreach (DictionaryEntry kv in map)
                    {
                        dict.Dictionary[ConvertNested(kv.Key, keyType, runtimeInterop)] =
                            ConvertNested(kv.Value, valueType, runtimeInterop);
                    }

                    return dict;
                }
                else if (type.IsArray)
                {
                    var arr = (Array)obj;
                    var elementType = type.GetElementType()!;
                    var newArr = new SpellkitObject[arr.Length];
                    for (var i = 0; i < arr.Length; i++)
                    {
                        newArr[i] = ConvertNested(arr.GetValue(i), elementType, runtimeInterop);
                    }

                    return new SpellkitArray(newArr);
                }
                else if (obj is IEnumerable seq)
                {
                    var enumerableType = FindGenericInterface(type, typeof(IEnumerable<>));
                    var elementType = enumerableType?.GetGenericArguments()[0] ?? typeof(object);
                    var values = new List<SpellkitObject>();
                    foreach (var item in seq)
                    {
                        values.Add(ConvertNested(item, elementType, runtimeInterop));
                    }

                    return new SpellkitArray(values.ToArray());
                }
                else if (BCL.Type.IsAssignableFrom(type))
                {
                    return new SpellkitInterop((Type)obj);
                }
                else
                {
                    return new SpellkitInterop(type, obj);
                }
        }
    }

    private static SpellkitObject ConvertNested(object? value, Type declaredType, bool runtimeInterop) =>
        runtimeInterop
            ? ConvertFrom(value)
            : ConvertFrom(value, declaredType, false);

    private static Type? FindGenericInterface(Type type, Type definition)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == definition)
        {
            return type;
        }

        return type.GetInterfaces()
            .FirstOrDefault(candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == definition);
    }

    public static T? ConvertTo<T>(ExecutionContext ctx, SpellkitObject obj) => (T?)ConvertTo(ctx, obj, typeof(T));

    public static object? ConvertTo(ExecutionContext ctx, SpellkitObject obj, Type type)
    {
        if (!TryConvert(obj, type, out var result))
        {
            ctx.InvalidCast(obj.TypeName, type.FullName ?? type.Name);
            return null;
        }

        return result; 
    }

    public static bool TryConvert(SpellkitObject obj, Type type, out object? result)
    {
        result = default;
        long i8; double r8; string str;

        if (obj.TypeId == SpellkitTypeCodes.Interop)
        {
            var interop = (SpellkitInterop)obj;

            if (BCL.Type.IsAssignableFrom(interop.Type)) //We have a type info here
            {
                if (BCL.Type.IsAssignableFrom(type)) //Type info is what we need
                {
                    result = interop.Object;
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                try
                {
                    result = Convert.ChangeType(interop.Object, type);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        if (type == BCL.SpellkitObject)
        {
            result = obj;
            return true;
        }
        else if (type == BCL.Object)
        {
            result = obj.ToObject();
            return true;
        }
        else if (BCL.SpellkitObject.IsAssignableFrom(type))
        {
            result = Convert.ChangeType(obj, type);
            return true;
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean:
                result = obj.IsTrue();
                return true;
            case TypeCode.Byte:
                return TryConvertInteger(obj, static value => checked((byte)value), out result);
            case TypeCode.Int16:
                return TryConvertInteger(obj, static value => checked((short)value), out result);
            case TypeCode.Int32:
                return TryConvertInteger(obj, static value => checked((int)value), out result);
            case TypeCode.Int64:
                if (TryGetInteger(obj, out i8))
                {
                    result = i8;
                    return true;
                }
                return false;
            case TypeCode.SByte:
                return TryConvertInteger(obj, static value => checked((sbyte)value), out result);
            case TypeCode.UInt16:
                return TryConvertInteger(obj, static value => checked((ushort)value), out result);
            case TypeCode.UInt32:
                return TryConvertInteger(obj, static value => checked((uint)value), out result);
            case TypeCode.UInt64:
                return TryConvertInteger(obj, static value => checked((ulong)value), out result);
            case TypeCode.String:
                if (TryGetString(obj, out str))
                {
                    result = str;
                    return true;
                }
                return false;
            case TypeCode.Char:
                if (TryGetString(obj, out str))
                {
                    result = string.IsNullOrEmpty(str) ? '\0' : str[0];
                    return true;
                }
                return false;
            case TypeCode.Single:
                if (TryGetFloat(obj, out r8))
                {
                    if (double.IsFinite(r8) && Math.Abs(r8) > float.MaxValue)
                    {
                        return false;
                    }

                    result = (float)r8;
                    return true;
                }
                return false;
            case TypeCode.Double:
                if (TryGetFloat(obj, out r8))
                {
                    result = r8;
                    return true;
                }
                return false;
            case TypeCode.Decimal:
                if (TryGetFloat(obj, out r8))
                {
                    try
                    {
                        result = checked((decimal)r8);
                        return true;
                    }
                    catch (OverflowException)
                    {
                        return false;
                    }
                }
                return false;
            case TypeCode.Empty:
                result = null;
                return true;
            default:
                if (obj is SpellkitDictionary map)
                {
                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        var genargs = type.GetGenericArguments();
                        var (keyType, valueType) = (genargs[0], genargs[1]);
                        var targetType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                        var ret = (IDictionary)Activator.CreateInstance(targetType)!;
                        foreach (var kv in map.Dictionary)
                        {
                            if (!TryConvert(kv.Key, keyType, out var key)
                                || !TryConvert(kv.Value, valueType, out var value))
                            {
                                return false;
                            }

                            ret[key!] = value;
                        }
                        result = ret;
                        return true;

                    }
                    else if (type == typeof(Hashtable))
                    {
                        var ret = new Hashtable();
                        foreach (var kv in map.Dictionary)
                        {
                            ret[kv.Key.ToObject()] = kv.Value.ToObject();
                        }

                        result = ret;
                        return true;
                    }
                }
                else if (obj is SpellkitEnumerable enu)
                {
                    if (type.IsArray)
                    {
                        var et = type.GetElementType();
                        if (!TryCreateTypedArray(enu.ToArray(), et!, out var res))
                        {
                            return false;
                        }

                        result = res;
                        return true;
                    }

                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        var et = type.GetGenericArguments()[0];
                        if (!TryCreateTypedArray(enu.ToArray(), et!, out var res))
                        {
                            return false;
                        }

                        result = res;
                        return true;
                    }

                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        var et = type.GetGenericArguments()[0];
                        if (!TryCreateTypedArray(enu.ToArray(), et!, out var arr))
                        {
                            return false;
                        }

                        var targetType = typeof(List<>).MakeGenericType(et!);
                        result = Activator.CreateInstance(targetType, arr);
                        return true;
                    }

                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
                    {
                        var et = type.GetGenericArguments()[0];
                        if (!TryCreateTypedArray(enu.ToArray(), et!, out var arr))
                        {
                            return false;
                        }

                        var targetType = typeof(HashSet<>).MakeGenericType(et!);
                        result = Activator.CreateInstance(targetType, arr);
                        return true;
                    }
                }
                break;
        }

        return false;
    }

    private static bool TryGetFloat(SpellkitObject obj, out double result)
    {
        if (obj is SpellkitFloat f)
        {
            result = f.Value;
            return true;
        }
        else if (obj is SpellkitInteger i)
        {
            result = i.Value;
            return true;
        }
        else if (obj is SpellkitChar c)
        {
            result = c.Value;
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryConvertInteger<T>(
        SpellkitObject obj,
        Func<long, T> convert,
        out object? result)
    {
        result = default;
        if (!TryGetInteger(obj, out var integer))
        {
            return false;
        }

        try
        {
            result = convert(integer);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryGetInteger(SpellkitObject obj, out long result)
    {
        if (obj is SpellkitInteger i)
        {
            result = i.Value;
            return true;
        }
        else if (obj is SpellkitFloat f)
        {
            if (!double.IsFinite(f.Value)
                || f.Value < long.MinValue
                || f.Value >= 9_223_372_036_854_775_808d)
            {
                result = default;
                return false;
            }

            result = (long)f.Value;
            return true;
        }
        else if (obj is SpellkitChar c)
        {
            result = c.Value;
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryGetString(SpellkitObject obj, out string result)
    {
        if (obj is SpellkitString s)
        {
            result = s.Value;
            return true;
        }
        else if (obj is SpellkitChar c)
        {
            result = c.Value.ToString();
            return true;
        }

        result = string.Empty;
        return false;
    }

    public static bool TryCreateTypedArray(SpellkitObject[] arr, Type type, out Array? result)
    {
        var xs = Array.CreateInstance(type, arr.Length);
        result = default;

        for (var i = 0; i < arr.Length; i++)
        {
            if (!TryConvert(arr[i], type, out var obj))
            {
                return false;
            }

            xs.SetValue(obj, i);
        }

        result = xs;
        return true;
    }
}
