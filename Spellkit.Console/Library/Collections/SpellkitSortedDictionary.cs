using Spellkit.Runtime.Types;
using System.Collections;

namespace Spellkit.Library.Collections;

public sealed class SpellkitSortedDictionary : SpellkitForeignObject, IEnumerable<KeyValuePair<SpellkitObject, SpellkitObject>>
{
    internal readonly SortedDictionary<SpellkitObject, SpellkitObject> Items;

    internal SpellkitSortedDictionary(SpellkitSortedDictionaryTypeInfo typeInfo) : base(typeInfo) =>
        Items = new(new SpellkitCollectionObjectComparer());

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
}
