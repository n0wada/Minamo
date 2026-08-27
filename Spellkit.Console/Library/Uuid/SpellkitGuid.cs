using Spellkit.Runtime.Types;
using System;

namespace Spellkit.Library.Uuid;

public sealed class SpellkitGuid : SpellkitForeignObject
{
    internal readonly Guid Value;

    public SpellkitGuid(SpellkitGuidTypeInfo typeInfo, Guid value) : base(typeInfo) => Value = value;

    public override int GetHashCode() => Value.GetHashCode();

    public override bool HasStableValueEquality => true;

    public override object ToObject() => Value;

    public override string ToString() => Value.ToString();

    public override SpellkitObject Clone() => this;

    public override bool Equals(SpellkitObject? other) => other is SpellkitGuid g && g.Value == Value;
}
