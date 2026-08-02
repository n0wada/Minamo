using Spellkit.Runtime.Types;

namespace Spellkit.Library.Random;

public sealed class SpellkitRandom : SpellkitForeignObject
{
    internal System.Random Generator { get; }

    internal SpellkitRandom(SpellkitRandomTypeInfo typeInfo, int? seed)
        : base(typeInfo) => Generator = seed is null ? new System.Random() : new System.Random(seed.Value);

    public override object ToObject() => Generator;

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => Generator.GetHashCode();

    public override SpellkitObject Clone() => this;
}
