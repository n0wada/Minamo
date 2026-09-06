using Minamo.Runtime.Types;
using System.Text;

namespace Minamo.Library.Text;

public sealed class MinamoStringBuilder : MinamoForeignObject
{
    internal StringBuilder Builder;

    public MinamoStringBuilder(MinamoForeignTypeInfo typeInfo, StringBuilder builder) : base(typeInfo) => Builder = builder;

    public override bool Equals(MinamoObject? other) =>
        other is MinamoString || other is MinamoStringBuilder && Builder.ToString() == other.ToString();

    public override object ToObject() => Builder.ToString();

    public override string ToString() => Builder.ToString();

    public override int GetHashCode() => Builder.GetHashCode();

    public override MinamoObject Clone()
    {
        var clone = (MinamoStringBuilder)MemberwiseClone();
        clone.Builder = new StringBuilder(Builder.ToString());
        return clone;
    }
}
