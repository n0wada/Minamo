using Minamo.Runtime.Types;

namespace Minamo.Library.Collections;

internal sealed class MinamoCollectionObjectComparer : IComparer<MinamoObject>
{
    public int Compare(MinamoObject? x, MinamoObject? y)
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
            MinamoString text when y is MinamoString other => string.CompareOrdinal(text.Value, other.Value),
            MinamoChar character when y is MinamoChar other => character.Value.CompareTo(other.Value),
            MinamoInteger integer when y is MinamoInteger other => integer.Value.CompareTo(other.Value),
            MinamoFloat number when y is MinamoFloat other => number.Value.CompareTo(other.Value),
            MinamoBool boolean when y is MinamoBool other => ((bool)boolean).CompareTo((bool)other),
            _ => string.CompareOrdinal(x.ToString(), y.ToString())
        };
    }
}
