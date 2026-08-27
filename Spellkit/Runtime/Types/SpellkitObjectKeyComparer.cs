using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Spellkit.Runtime.Types;

// Hash-based collections compare immutable values structurally and mutable values by identity.
// A mutable value's structural hash can change after insertion, which would make the entry
// unreachable in a Dictionary or Set.
internal sealed class SpellkitObjectKeyComparer : IEqualityComparer<SpellkitObject>
{
    public static readonly SpellkitObjectKeyComparer Instance = new();

    private SpellkitObjectKeyComparer() { }

    public bool Equals(SpellkitObject? left, SpellkitObject? right)
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

    public int GetHashCode(SpellkitObject value) =>
        IsValueKey(value) ? value.GetHashCode() : RuntimeHelpers.GetHashCode(value);

    private static bool IsValueKey(SpellkitObject value) =>
        value is SpellkitNil
            or SpellkitBool
            or SpellkitInteger
            or SpellkitFloat
            or SpellkitChar
            or SpellkitString
            or SpellkitForeignObject { HasStableValueEquality: true }
            || IsCompoundValueKey(value, new HashSet<SpellkitObject>(ReferenceEqualityComparer.Instance));

    private static bool IsCompoundValueKey(SpellkitObject value, HashSet<SpellkitObject> visiting) =>
        value switch
        {
            SpellkitTuple tuple => IsTupleValueKey(tuple, visiting),
            SpellkitClass instance => IsClassValueKey(instance, visiting),
            SpellkitExceptionObject exception => IsTupleValueKey(exception.Data, visiting),
            _ => false
        };

    private static bool IsClassValueKey(SpellkitClass value, HashSet<SpellkitObject> visiting)
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

    private static bool IsTupleValueKey(SpellkitTuple value, HashSet<SpellkitObject> visiting)
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
                if (item is SpellkitLabel label)
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

    private static bool IsValueKey(SpellkitObject value, HashSet<SpellkitObject> visiting) =>
        value is SpellkitNil
            or SpellkitBool
            or SpellkitInteger
            or SpellkitFloat
            or SpellkitChar
            or SpellkitString
            or SpellkitForeignObject { HasStableValueEquality: true }
            || IsCompoundValueKey(value, visiting);
}
