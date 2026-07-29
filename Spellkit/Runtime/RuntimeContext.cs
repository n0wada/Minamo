using Spellkit.Debug;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Spellkit.Compiler;

namespace Spellkit.Runtime;

public static class Extensions
{
    public static T Type<T>(this ExecutionContext ctx) where T : SpellkitTypeInfo =>
        ctx.RuntimeContext.Types.OfType<T>().First();
}

public static class ImplicitConverter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetFloat(this SpellkitObject self)
    {
        if (self is SpellkitFloat r8)
        {
            return r8.Value;
        }

        if (self is SpellkitInteger i8)
        {
            return i8.Value;
        }

        throw new InvalidCastException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char GetChar(this SpellkitObject self)
    {
        if (self is SpellkitChar c)
        {
            return c.Value;
        }

        if (self is SpellkitString str)
        {
            return str.Value.Length > 0 ? str.Value[0] : '\0';
        }

        throw new InvalidCastException();
    }
}

public sealed class RuntimeContext
{
    internal readonly SpellkitStringTypeInfo String;
    internal readonly SpellkitCharTypeInfo Char;
    internal readonly SpellkitNilTypeInfo Nil;
    internal readonly SpellkitTupleTypeInfo Tuple;
    internal readonly SpellkitArrayTypeInfo Array;
    internal readonly FastList<SpellkitTypeInfo> Types;

    internal readonly System.Threading.Lock SyncRoot = new();

    internal SpellkitObject[][] Units { get; private set; }

    internal MemoryLayout[][] Layouts { get; private set; }

    internal Dictionary<string, object> Variables { get; } = new();

    public UnitComposition Composition { get; private set; }

    internal RuntimeContext(UnitComposition composition)
    {
        Types = SpellkitTypeCodes.GetAll();
        String = (SpellkitStringTypeInfo)Types[SpellkitTypeCodes.String];
        Char = (SpellkitCharTypeInfo)Types[SpellkitTypeCodes.Char];
        Nil = (SpellkitNilTypeInfo)Types[SpellkitTypeCodes.Nil];
        Tuple = (SpellkitTupleTypeInfo)Types[SpellkitTypeCodes.Tuple];
        Array = (SpellkitArrayTypeInfo)Types[SpellkitTypeCodes.Array];
        Composition = composition;
        Units = new SpellkitObject[Composition.Units.Length][];
        Layouts = Composition.Units.Select(u => u.Layouts.UnsafeGetArray()).ToArray();
    }

    public void Refresh(UnitComposition composition)
    {
        Composition = composition;

        //Take into account new modules
        var newUnits = new SpellkitObject[Composition.Units.Length][];
        for (var i = 0; i < Units.Length; i++)
        {
            newUnits[i] = Units[i];
        }

        Units = newUnits;
        Layouts = Composition.Units.Select(u => u.Layouts.ToArray()).ToArray();
    }
}
