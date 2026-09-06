using Minamo.Runtime.Types;
using System.Collections;

namespace Minamo.Library.Collections;

public sealed class MinamoSortedDictionary : MinamoForeignObject, IEnumerable<KeyValuePair<MinamoObject, MinamoObject>>
{
    internal readonly SortedDictionary<MinamoObject, MinamoObject> Items;

    internal MinamoSortedDictionary(MinamoSortedDictionaryTypeInfo typeInfo) : base(typeInfo) =>
        Items = new(new MinamoCollectionObjectComparer());

    internal MinamoSortedDictionary(MinamoSortedDictionaryTypeInfo typeInfo, IEnumerable<KeyValuePair<MinamoObject, MinamoObject>> values)
        : this(typeInfo)
    {
        foreach (var (key, value) in values)
        {
            Items[key] = value;
        }
    }

    public override MinamoObject Clone() => new MinamoSortedDictionary((MinamoSortedDictionaryTypeInfo)TypeInfo, Items);

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => Items.GetHashCode();

    public override object ToObject() => Items;

    public override string ToString() => $"SortedDictionary({Items.Count})";

    public IEnumerator<KeyValuePair<MinamoObject, MinamoObject>> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
