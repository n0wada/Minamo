using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Minamo.Runtime.Types;

// Hash-based collections compare immutable values structurally and mutable values by identity.
// A mutable value's structural hash can change after insertion, which would make the entry
// unreachable in a Dictionary or Set.
internal sealed class MinamoObjectKeyComparer : IEqualityComparer<MinamoObject>
{
    public static readonly MinamoObjectKeyComparer Instance = new();

    private MinamoObjectKeyComparer() { }

    public bool Equals(MinamoObject? left, MinamoObject? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null
            && right is not null
            && IsValueKey(left)
            && IsValueKey(right)
            && left.Equals(right);
    }

    public int GetHashCode(MinamoObject value) =>
        IsValueKey(value) ? value.GetHashCode() : RuntimeHelpers.GetHashCode(value);

    private static bool IsValueKey(MinamoObject value) =>
        value is MinamoNil
            or MinamoBool
            or MinamoInteger
            or MinamoFloat
            or MinamoChar
            or MinamoString
            or MinamoForeignObject { HasStableValueEquality: true }
            || IsCompoundValueKey(value, new HashSet<MinamoObject>(ReferenceEqualityComparer.Instance));

    private static bool IsCompoundValueKey(MinamoObject value, HashSet<MinamoObject> visiting) =>
        value switch
        {
            MinamoTuple tuple => IsTupleValueKey(tuple, visiting),
            MinamoClass instance => IsClassValueKey(instance, visiting),
            MinamoExceptionObject exception => IsTupleValueKey(exception.Data, visiting),
            _ => false
        };

    private static bool IsClassValueKey(MinamoClass value, HashSet<MinamoObject> visiting)
    {
        if (!visiting.Add(value))
        {
            return false;
        }

        try
        {
            return IsTupleValueKey(value.Fields, visiting)
                && IsTupleValueKey(value.Inits, visiting);
        }
        finally
        {
            visiting.Remove(value);
        }
    }

    private static bool IsTupleValueKey(MinamoTuple value, HashSet<MinamoObject> visiting)
    {
        if (!visiting.Add(value))
        {
            return false;
        }

        try
        {
            var values = value.UnsafeAccess();
            for (var i = 0; i < value.Count; i++)
            {
                var item = values[i];
                if (item is MinamoLabel label)
                {
                    if (label.Mutable)
                    {
                        return false;
                    }

                    item = label.Value;
                }

                if (!IsValueKey(item, visiting))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            visiting.Remove(value);
        }
    }

    private static bool IsValueKey(MinamoObject value, HashSet<MinamoObject> visiting) =>
        value is MinamoNil
            or MinamoBool
            or MinamoInteger
            or MinamoFloat
            or MinamoChar
            or MinamoString
            or MinamoForeignObject { HasStableValueEquality: true }
            || IsCompoundValueKey(value, visiting);
}
