using Spellkit.Runtime.Types;
using System.Collections;

namespace Spellkit.Library.Collections;

public sealed class SpellkitSortedDictionary : SpellkitForeignObject, IEnumerable<KeyValuePair<SpellkitObject, SpellkitObject>>
{
    internal readonly SortedDictionary<SpellkitObject, SpellkitObject> Items;

    internal SpellkitSortedDictionary(SpellkitSortedDictionaryTypeInfo typeInfo) : base(typeInfo) =>
        Items = new(new SpellkitObjectComparer());

    internal SpellkitSortedDictionary(SpellkitSortedDictionaryTypeInfo typeInfo, IEnumerable<KeyValuePair<SpellkitObject, SpellkitObject>> values)
        : this(typeInfo)
    {
        foreach (var (key, value) in values)
        {
            Items[key] = value;
        }
    }

    public override SpellkitObject Clone() => new SpellkitSortedDictionary((SpellkitSortedDictionaryTypeInfo)TypeInfo, Items);

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => Items.GetHashCode();

    public override object ToObject() => Items;

    public override string ToString() => $"SortedDictionary({Items.Count})";

    public IEnumerator<KeyValuePair<SpellkitObject, SpellkitObject>> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class SpellkitObjectComparer : IComparer<SpellkitObject>
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
}
