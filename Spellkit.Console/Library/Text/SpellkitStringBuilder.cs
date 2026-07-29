using Spellkit.Runtime.Types;
using System.Text;

namespace Spellkit.Library.Text;

public sealed class SpellkitStringBuilder : SpellkitForeignObject
{
    internal StringBuilder Builder;

    public SpellkitStringBuilder(SpellkitForeignTypeInfo typeInfo, StringBuilder builder) : base(typeInfo) => Builder = builder;

    public override bool Equals(SpellkitObject? other) =>
        other is SpellkitString || other is SpellkitStringBuilder && Builder.ToString() == other.ToString();

    public override object ToObject() => Builder.ToString();

    public override string ToString() => Builder.ToString();

    public override int GetHashCode() => Builder.GetHashCode();

    public override SpellkitObject Clone()
    {
        var clone = (SpellkitStringBuilder)MemberwiseClone();
        clone.Builder = new StringBuilder(Builder.ToString());
        return clone;
    }
}
