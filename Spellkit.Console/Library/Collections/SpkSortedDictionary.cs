using Spellkit.Runtime.Types;
using System.Collections;

namespace Spellkit.Library.Collections;

public sealed class SpkSortedDictionary : SpkForeignObject, IEnumerable<KeyValuePair<SpkObject, SpkObject>>
{
    internal readonly SortedDictionary<SpkObject, SpkObject> Items;

    internal SpkSortedDictionary(SpkSortedDictionaryTypeInfo typeInfo) : base(typeInfo) =>
        Items = new(new SpkObjectComparer());

    internal SpkSortedDictionary(SpkSortedDictionaryTypeInfo typeInfo, IEnumerable<KeyValuePair<SpkObject, SpkObject>> values)
        : this(typeInfo)
    {
        foreach (var (key, value) in values)
        {
            Items[key] = value;
        }
    }

    public override SpkObject Clone() => new SpkSortedDictionary((SpkSortedDictionaryTypeInfo)TypeInfo, Items);

    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => Items.GetHashCode();

    public override object ToObject() => Items;

    public override string ToString() => $"SortedDictionary({Items.Count})";

    public IEnumerator<KeyValuePair<SpkObject, SpkObject>> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class SpkObjectComparer : IComparer<SpkObject>
    {
        public int Compare(SpkObject? x, SpkObject? y)
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
                SpkString text when y is SpkString other => string.CompareOrdinal(text.Value, other.Value),
                SpkChar character when y is SpkChar other => character.Value.CompareTo(other.Value),
                SpkInteger integer when y is SpkInteger other => integer.Value.CompareTo(other.Value),
                SpkFloat number when y is SpkFloat other => number.Value.CompareTo(other.Value),
                SpkBool boolean when y is SpkBool other => ((bool)boolean).CompareTo((bool)other),
                _ => string.CompareOrdinal(x.ToString(), y.ToString())
            };
        }
    }
}
