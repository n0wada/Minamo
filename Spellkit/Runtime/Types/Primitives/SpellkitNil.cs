using Spellkit.Codegen;

namespace Spellkit.Runtime.Types;

public class SpellkitNil : SpellkitObject
{
    public static readonly SpellkitNil Instance = new();
    internal static readonly SpellkitNil Terminator = new SpellkitNilTerminator();

    public override string TypeName => nameof(SpellkitTypeCodes.Nil);

    private sealed class SpellkitNilTerminator : SpellkitNil { }

    private SpellkitNil() : base(SpellkitTypeCodes.Nil) { }

    public override object ToObject() => null!;

    public override string ToString() => "nil";

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override SpellkitObject Clone() => this;

    public override int GetHashCode() => HashCode.Combine(TypeName, TypeId);
}

[SpellkitType]
internal sealed partial class SpellkitNilTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Nil);

    public override int ReflectedTypeId => SpellkitTypeCodes.Nil;


    #region Operations
    protected override SpellkitObject NotOp(ExecutionContext ctx, SpellkitObject arg) => True;

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Bool => False,
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [SpellkitStaticMethod(BuiltinMethodNames.Nil)]
    internal static SpellkitNil GetNil() => Nil;

    [SpellkitStaticProperty(BuiltinMethodNames.Default)]
    internal static SpellkitNil Default() => Nil;
}
