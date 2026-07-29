global using System;
global using static Spellkit.SpellkitValues;
global using static Spellkit.CultureInfoSettings;

using Spellkit.Codegen;
using Spellkit.Runtime.Types;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Spellkit;

[Flags]
public enum Ops
{
    None = 0xFF,
    Add = 0x01,
    Sub = 0x02,
    Mul = 0x04,
    Div = 0x08,
    Rem = 0x10,
    Gt = 0x400,
    Lt = 0x800,
    Gte = 0x1000,
    Lte = 0x2000,
    Neg = 0x4000,
    Plus = 0x20000,
    Get = 0x40000,
    Set = 0x80000,
    Len = 0x100000,
    Iter = 0x200000,
    In = 0x400000
}

internal static class BCL
{
    public static readonly Type Boolean = typeof(bool);
    public static readonly Type SByte = typeof(sbyte);
    public static readonly Type Int16 = typeof(short);
    public static readonly Type Int32 = typeof(int);
    public static readonly Type Int64 = typeof(long);
    public static readonly Type Byte = typeof(byte);
    public static readonly Type UInt16 = typeof(ushort);
    public static readonly Type UInt32 = typeof(uint);
    public static readonly Type UInt64 = typeof(ulong);
    public static readonly Type Single = typeof(float);
    public static readonly Type Double = typeof(double);
    public static readonly Type Float = typeof(float);
    public static readonly Type Decimal = typeof(decimal);
    public static readonly Type Char = typeof(char);
    public static readonly Type String = typeof(string);
    public static readonly Type Object = typeof(object);
    public static readonly Type IEnumerable = typeof(IEnumerable);
    public static readonly Type IEnumerableObject = typeof(IEnumerable<object>);
    public static readonly Type IDictionary = typeof(IDictionary);
    public static readonly Type IDictionaryStringObject = typeof(IDictionary<string, object>);
    public static readonly Type Array = typeof(Array);
    public static readonly Type ArrayObject = typeof(object[]);
    public static readonly Type ListObject = typeof(List<object>);
    public static readonly Type Type = typeof(Type);
    public static readonly Type SpellkitObject = typeof(SpellkitObject);

    public static List<MethodInfo>? GetOverloadedMethod(this Type type, string name, BindingFlags flags)
    {
        List<MethodInfo>? xs = default;

        foreach (var mi in type.GetMethods(flags))
        {
            if (mi.Name == name)
            {
                xs ??= new();
                xs.Add(mi);
            }
        }

        return xs;
    }
}

internal static class CultureInfoSettings
{
    public static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    public static readonly CultureInfo SystemCulture = CultureInfo.InstalledUICulture;
}

public static partial class SpellkitTypeCodes
{
    public const int Nil = 1;
    public const int Integer = 2;
    public const int Float = 3;
    public const int Bool = 4;
    public const int Char = 5;
    public const int String = 6;
    public const int Function = 7;
    public const int Label = 8;
    public const int TypeInfo = 9;
    public const int Module = 10;
    public const int Array = 11;
    public const int Iterator = 12;
    public const int Tuple = 13;
    public const int Dictionary = 14;
    public const int Set = 15;
    public const int Interop = 16;
    public const int Exception = 28;

    [Mixin] public const int Object = 17;
    [Mixin] public const int Number = 18;
    [Mixin] public const int Order = 19;
    [Mixin] public const int Lookup = 20;
    [Mixin] public const int Collection = 21;
    [Mixin] public const int Equatable = 22;
    [Mixin] public const int Sequence = 23;
    [Mixin] public const int Identity = 24;
    [Mixin] public const int Functor = 25;
    [Mixin] public const int Disposable = 26;
    [Mixin] public const int Container = 27;

    internal static FastList<SpellkitTypeInfo> GetAll()
    {
        var xs = new FastList<SpellkitTypeInfo>();
        GetAllGenerated(xs);
        return xs;
    }
    static partial void GetAllGenerated(FastList<SpellkitTypeInfo> types);

    public static int GetTypeCodeByName(string name)
    {
        int code = 0;
        GetTypeCodeByNameGenerated(name, ref code);
        return code;
    }
    static partial void GetTypeCodeByNameGenerated(string name, ref int code);

    public static string GetTypeNameByCode(int code)
    {
        string name = "";
        GetTypeNameByCodeGenerated(code, ref name);
        return name;
    }
    static partial void GetTypeNameByCodeGenerated(int code, ref string name);

    public static SpellkitTypeInfo GetMixinByCode(int code)
    {
        SpellkitTypeInfo mixin = null!;
        GetMixinByCodeGenerated(code, ref mixin);
        return mixin;
    }
    static partial void GetMixinByCodeGenerated(int code, ref SpellkitTypeInfo name);

}

internal static class FileProbe
{
    public static string GetExecutablePath() => Assembly.GetExecutingAssembly().Location;

    public static string GetExecutableDirectory() =>
        Path.GetDirectoryName(GetExecutablePath()) ?? string.Empty;

    public static DateTime GetAssembyTimeStamp() => File.GetLastWriteTime(GetExecutablePath());
}

public struct HashString : IEquatable<HashString>
{
    private readonly string value;
    private int hashCode;

    public HashString(string value) => (this.value, this.hashCode) = (value, 0);

    public override bool Equals(object? obj) => obj is HashString str && Equals(str);

    public bool Equals(HashString other) => other.value.Equals(value);

    internal int LookupHash() => hashCode;

    public override int GetHashCode()
    {
        if (hashCode == 0)
        {
            hashCode = value.GetHashCode();
        }

        return hashCode;
    }

    public override string ToString() => value;

    public static explicit operator string(HashString str) => str.value;

    public static implicit operator HashString(string str) => new(str);

    public static bool operator ==(HashString left, HashString right) => left.Equals(right);

    public static bool operator !=(HashString left, HashString right) => !left.Equals(right);
}

public class SpellkitValues
{
    public readonly static SpellkitNil Nil = SpellkitNil.Instance;
    public readonly static SpellkitBool True = SpellkitBool.True;
    public readonly static SpellkitBool False = SpellkitBool.False;
}
