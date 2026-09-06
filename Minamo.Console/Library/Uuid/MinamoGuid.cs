using Minamo.Runtime.Types;
using System;

namespace Minamo.Library.Uuid;

public sealed class MinamoGuid : MinamoForeignObject
{
    internal readonly Guid Value;

    public MinamoGuid(MinamoGuidTypeInfo typeInfo, Guid value) : base(typeInfo) => Value = value;

    public override int GetHashCode() => Value.GetHashCode();

    public override bool HasStableValueEquality => true;

    public override object ToObject() => Value;

    public override string ToString() => Value.ToString();

    public override MinamoObject Clone() => this;

    public override bool Equals(MinamoObject? other) => other is MinamoGuid g && g.Value == Value;
}
