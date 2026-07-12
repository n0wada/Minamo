using Spellkit.Runtime.Types;
using System;

namespace Spellkit.Library.Uuid;

public sealed class SpkGuid : SpkForeignObject
{
    internal readonly Guid Value;

    public SpkGuid(SpkGuidTypeInfo typeInfo, Guid value) : base(typeInfo) => Value = value;

    public override int GetHashCode() => Value.GetHashCode();

    public override object ToObject() => Value;

    public override string ToString() => Value.ToString();

    public override SpkObject Clone() => this;

    public override bool Equals(SpkObject? other) => other is SpkGuid g && g.Value == Value;
}
