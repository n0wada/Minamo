using Spellkit.Codegen;

namespace Spellkit.Runtime.Types;

public class SpkNil : SpkObject
{
    public static readonly SpkNil Instance = new();
    internal static readonly SpkNil Terminator = new SpkNilTerminator();

    public override string TypeName => nameof(Spk.Nil);

    private sealed class SpkNilTerminator : SpkNil { }

    private SpkNil() : base(Spk.Nil) { }

    public override object ToObject() => null!;

    public override string ToString() => "nil";

    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);

    public override SpkObject Clone() => this;

    public override int GetHashCode() => HashCode.Combine(TypeName, TypeId);
}

[SpkType]
internal sealed partial class SpkNilTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Nil);

    public override int ReflectedTypeId => Spk.Nil;


    #region Operations
    protected override SpkObject NotOp(ExecutionContext ctx, SpkObject arg) => True;

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            Spk.Bool => False,
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [SpkStaticMethod(BuiltinMethodNames.Nil)]
    internal static SpkNil GetNil() => Nil;

    [SpkStaticProperty(BuiltinMethodNames.Default)]
    internal static SpkNil Default() => Nil;
}
