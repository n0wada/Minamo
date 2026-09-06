using Minamo.Runtime.Types;

namespace Minamo.Library.Random;

public sealed class MinamoRandom : MinamoForeignObject
{
    internal System.Random Generator { get; }

    internal MinamoRandom(MinamoRandomTypeInfo typeInfo, int? seed)
        : base(typeInfo) => Generator = seed is null ? new System.Random() : new System.Random(seed.Value);

    public override object ToObject() => Generator;

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => Generator.GetHashCode();

    public override MinamoObject Clone() => this;
}
