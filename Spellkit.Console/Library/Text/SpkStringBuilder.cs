using Spellkit.Runtime.Types;
using System.Text;

namespace Spellkit.Library.Text;

public sealed class SpkStringBuilder : SpkForeignObject
{
    internal StringBuilder Builder;

    public SpkStringBuilder(SpkForeignTypeInfo typeInfo, StringBuilder builder) : base(typeInfo) => Builder = builder;

    public override bool Equals(SpkObject? other) =>
        other is SpkString || other is SpkStringBuilder && Builder.ToString() == other.ToString();

    public override object ToObject() => Builder.ToString();

    public override string ToString() => Builder.ToString();

    public override int GetHashCode() => Builder.GetHashCode();

    public override SpkObject Clone()
    {
        var clone = (SpkStringBuilder)MemberwiseClone();
        clone.Builder = new StringBuilder(Builder.ToString());
        return clone;
    }
}
