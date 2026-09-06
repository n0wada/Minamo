using Minamo.Debug;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Minamo.Compiler;

namespace Minamo.Runtime;

public static class Extensions
{
    public static T Type<T>(this ExecutionContext ctx) where T : MinamoTypeInfo =>
        ctx.RuntimeContext.Types.OfType<T>().First();
}

public static class ImplicitConverter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetFloat(this MinamoObject self)
    {
        if (self is MinamoFloat r8)
        {
            return r8.Value;
        }

        if (self is MinamoInteger i8)
        {
            return i8.Value;
        }

        throw new InvalidCastException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char GetChar(this MinamoObject self)
    {
        if (self is MinamoChar c)
        {
            return c.Value;
        }

        if (self is MinamoString str)
        {
            return str.Value.Length > 0 ? str.Value[0] : '\0';
        }

        throw new InvalidCastException();
    }
}

public sealed class RuntimeContext
{
    internal readonly MinamoStringTypeInfo String;
    internal readonly MinamoCharTypeInfo Char;
    internal readonly MinamoNilTypeInfo Nil;
    internal readonly MinamoTupleTypeInfo Tuple;
    internal readonly MinamoArrayTypeInfo Array;
    internal readonly FastList<MinamoTypeInfo> Types;

    internal readonly System.Threading.Lock SyncRoot = new();

    internal MinamoObject[][] Units { get; private set; }

    internal MemoryLayout[][] Layouts { get; private set; }

    internal Dictionary<string, object> Variables { get; } = new();

    public UnitComposition Composition { get; private set; }

    internal RuntimeContext(UnitComposition composition)
    {
        Types = MinamoTypeCodes.GetAll();
        String = (MinamoStringTypeInfo)Types[MinamoTypeCodes.String];
        Char = (MinamoCharTypeInfo)Types[MinamoTypeCodes.Char];
        Nil = (MinamoNilTypeInfo)Types[MinamoTypeCodes.Nil];
        Tuple = (MinamoTupleTypeInfo)Types[MinamoTypeCodes.Tuple];
        Array = (MinamoArrayTypeInfo)Types[MinamoTypeCodes.Array];
        Composition = composition;
        Units = new MinamoObject[Composition.Units.Length][];
        Layouts = Composition.Units.Select(u => u.Layouts.UnsafeGetArray()).ToArray();
    }

    public void Refresh(UnitComposition composition)
    {
        Composition = composition;

        //Take into account new modules
        var newUnits = new MinamoObject[Composition.Units.Length][];
        for (var i = 0; i < Units.Length; i++)
        {
            newUnits[i] = Units[i];
        }

        Units = newUnits;
        Layouts = Composition.Units.Select(u => u.Layouts.ToArray()).ToArray();
    }
}
