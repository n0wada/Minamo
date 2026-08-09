using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

internal sealed class SpellkitCollectionObjectComparer : IComparer<SpellkitObject>
{
    public int Compare(SpellkitObject? x, SpellkitObject? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        if (x.TypeId != y.TypeId)
        {
            return x.TypeName.CompareTo(y.TypeName);
        }

        return x switch
        {
            SpellkitString text when y is SpellkitString other => string.CompareOrdinal(text.Value, other.Value),
            SpellkitChar character when y is SpellkitChar other => character.Value.CompareTo(other.Value),
            SpellkitInteger integer when y is SpellkitInteger other => integer.Value.CompareTo(other.Value),
            SpellkitFloat number when y is SpellkitFloat other => number.Value.CompareTo(other.Value),
            SpellkitBool boolean when y is SpellkitBool other => ((bool)boolean).CompareTo((bool)other),
            _ => string.CompareOrdinal(x.ToString(), y.ToString())
        };
    }
}
