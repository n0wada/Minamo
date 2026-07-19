using Spellkit.Codegen;

namespace Spellkit.Runtime.Types;

public abstract class SpkBool : SpkObject
{
    public static readonly SpkBool True = new SpkBoolTrue();
    public static readonly SpkBool False = new SpkBoolFalse();

    public override string TypeName => nameof(Spk.Bool);

    private sealed class SpkBoolTrue: SpkBool
    {
        public override string ToString() => "true";

        public override int GetHashCode() => true.GetHashCode();
    }

    private sealed class SpkBoolFalse: SpkBool
    {
        public override string ToString() => "false";

        public override int GetHashCode() => false.GetHashCode();
    }

    private SpkBool() : base(Spk.Bool) { }

    public override object ToObject() => this is SpkBoolTrue;

    public override SpkObject Clone() => this;

    public static explicit operator bool(SpkBool v) => v is SpkBoolTrue;

    public static explicit operator SpkBool(bool v) => v ? True : False;

    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);
}

[SpkType]
internal sealed partial class SpkBoolTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Bool);

    public override int ReflectedTypeId => Spk.Bool;


    #region Operations
    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format) =>
        new SpkString(ReferenceEquals(arg, True) ? "true" : "false");

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            Spk.Integer => ReferenceEquals(self, True) ? SpkInteger.One : SpkInteger.Zero,
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [SpkStaticMethod(BuiltinMethodNames.Bool)]
    internal static bool CreateBool(SpkObject value) => value.IsTrue();

    [SpkStaticProperty(BuiltinMethodNames.Default)]
    internal static SpkBool Default() => False;

    [SpkStaticProperty(BuiltinMethodNames.Max)]
    internal static SpkBool Max() => True;

    [SpkStaticProperty(BuiltinMethodNames.Min)]
    internal static SpkBool Min() => False;
}
