using Spellkit.Debug;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Spellkit.Compiler;

namespace Spellkit.Runtime;

public static class Extensions
{
    public static T Type<T>(this ExecutionContext ctx) where T : SpkTypeInfo =>
        ctx.RuntimeContext.Types.OfType<T>().First();
}

public static class ImplicitConverter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetFloat(this SpkObject self)
    {
        if (self is SpkFloat r8)
        {
            return r8.Value;
        }

        if (self is SpkInteger i8)
        {
            return i8.Value;
        }

        throw new InvalidCastException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char GetChar(this SpkObject self)
    {
        if (self is SpkChar c)
        {
            return c.Value;
        }

        if (self is SpkString str)
        {
            return str.Value.Length > 0 ? str.Value[0] : '\0';
        }

        throw new InvalidCastException();
    }
}

public sealed class RuntimeContext
{
    internal readonly SpkStringTypeInfo String;
    internal readonly SpkCharTypeInfo Char;
    internal readonly SpkNilTypeInfo Nil;
    internal readonly SpkTupleTypeInfo Tuple;
    internal readonly SpkArrayTypeInfo Array;
    internal readonly FastList<SpkTypeInfo> Types;

    internal readonly System.Threading.Lock SyncRoot = new();

    internal SpkObject[][] Units { get; private set; }

    internal MemoryLayout[][] Layouts { get; private set; }

    internal Dictionary<string, object> Variables { get; } = new();

    public UnitComposition Composition { get; private set; }

    internal RuntimeContext(UnitComposition composition)
    {
        Types = Spk.GetAll();
        String = (SpkStringTypeInfo)Types[Spk.String];
        Char = (SpkCharTypeInfo)Types[Spk.Char];
        Nil = (SpkNilTypeInfo)Types[Spk.Nil];
        Tuple = (SpkTupleTypeInfo)Types[Spk.Tuple];
        Array = (SpkArrayTypeInfo)Types[Spk.Array];
        Composition = composition;
        Units = new SpkObject[Composition.Units.Length][];
        Layouts = Composition.Units.Select(u => u.Layouts.UnsafeGetArray()).ToArray();
    }

    public void Refresh(UnitComposition composition)
    {
        Composition = composition;

        //Take into account new modules
        var newUnits = new SpkObject[Composition.Units.Length][];
        for (var i = 0; i < Units.Length; i++)
        {
            newUnits[i] = Units[i];
        }

        Units = newUnits;
        Layouts = Composition.Units.Select(u => u.Layouts.ToArray()).ToArray();
    }
}
