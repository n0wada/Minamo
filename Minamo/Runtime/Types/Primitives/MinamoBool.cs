using Minamo.Codegen;

namespace Minamo.Runtime.Types;

public abstract class MinamoBool : MinamoObject
{
    public static readonly MinamoBool True = new MinamoBoolTrue();
    public static readonly MinamoBool False = new MinamoBoolFalse();

    public override string TypeName => nameof(MinamoTypeCodes.Bool);

    private sealed class MinamoBoolTrue: MinamoBool
    {
        public override string ToString() => "true";

        public override int GetHashCode() => true.GetHashCode();
    }

    private sealed class MinamoBoolFalse: MinamoBool
    {
        public override string ToString() => "false";

        public override int GetHashCode() => false.GetHashCode();
    }

    private MinamoBool() : base(MinamoTypeCodes.Bool) { }

    public override object ToObject() => this is MinamoBoolTrue;

    public override MinamoObject Clone() => this;

    public static explicit operator bool(MinamoBool v) => v is MinamoBoolTrue;

    public static explicit operator MinamoBool(bool v) => v ? True : False;

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);
}

[MinamoType]
internal sealed partial class MinamoBoolTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Bool);

    public override int ReflectedTypeId => MinamoTypeCodes.Bool;


    #region Operations
    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString(ReferenceEquals(arg, True) ? "true" : "false");

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            MinamoTypeCodes.Integer => ReferenceEquals(self, True) ? MinamoInteger.One : MinamoInteger.Zero,
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [MinamoStaticMethod(BuiltinMethodNames.Bool)]
    internal static bool CreateBool(MinamoObject value) => value.IsTrue();

    [MinamoStaticProperty(BuiltinMethodNames.Default)]
    internal static MinamoBool Default() => False;

    [MinamoStaticProperty(BuiltinMethodNames.Max)]
    internal static MinamoBool Max() => True;

    [MinamoStaticProperty(BuiltinMethodNames.Min)]
    internal static MinamoBool Min() => False;
}
