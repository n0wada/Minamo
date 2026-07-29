using Spellkit.Codegen;

namespace Spellkit.Runtime.Types;

public abstract class SpellkitBool : SpellkitObject
{
    public static readonly SpellkitBool True = new SpellkitBoolTrue();
    public static readonly SpellkitBool False = new SpellkitBoolFalse();

    public override string TypeName => nameof(SpellkitTypeCodes.Bool);

    private sealed class SpellkitBoolTrue: SpellkitBool
    {
        public override string ToString() => "true";

        public override int GetHashCode() => true.GetHashCode();
    }

    private sealed class SpellkitBoolFalse: SpellkitBool
    {
        public override string ToString() => "false";

        public override int GetHashCode() => false.GetHashCode();
    }

    private SpellkitBool() : base(SpellkitTypeCodes.Bool) { }

    public override object ToObject() => this is SpellkitBoolTrue;

    public override SpellkitObject Clone() => this;

    public static explicit operator bool(SpellkitBool v) => v is SpellkitBoolTrue;

    public static explicit operator SpellkitBool(bool v) => v ? True : False;

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);
}

[SpellkitType]
internal sealed partial class SpellkitBoolTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Bool);

    public override int ReflectedTypeId => SpellkitTypeCodes.Bool;


    #region Operations
    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString(ReferenceEquals(arg, True) ? "true" : "false");

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Integer => ReferenceEquals(self, True) ? SpellkitInteger.One : SpellkitInteger.Zero,
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [SpellkitStaticMethod(BuiltinMethodNames.Bool)]
    internal static bool CreateBool(SpellkitObject value) => value.IsTrue();

    [SpellkitStaticProperty(BuiltinMethodNames.Default)]
    internal static SpellkitBool Default() => False;

    [SpellkitStaticProperty(BuiltinMethodNames.Max)]
    internal static SpellkitBool Max() => True;

    [SpellkitStaticProperty(BuiltinMethodNames.Min)]
    internal static SpellkitBool Min() => False;
}
