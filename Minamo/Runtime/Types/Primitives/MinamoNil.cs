using Minamo.Codegen;

namespace Minamo.Runtime.Types;

public class MinamoNil : MinamoObject
{
    public static readonly MinamoNil Instance = new();
    internal static readonly MinamoNil Terminator = new MinamoNilTerminator();

    public override string TypeName => nameof(MinamoTypeCodes.Nil);

    private sealed class MinamoNilTerminator : MinamoNil { }

    private MinamoNil() : base(MinamoTypeCodes.Nil) { }

    public override object ToObject() => null!;

    public override string ToString() => "nil";

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override MinamoObject Clone() => this;

    public override int GetHashCode() => HashCode.Combine(TypeName, TypeId);
}

[MinamoType]
internal sealed partial class MinamoNilTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Nil);

    public override int ReflectedTypeId => MinamoTypeCodes.Nil;


    #region Operations
    protected override MinamoObject NotOp(ExecutionContext ctx, MinamoObject arg) => True;

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            MinamoTypeCodes.Bool => False,
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [MinamoStaticMethod(BuiltinMethodNames.Nil)]
    internal static MinamoNil GetNil() => Nil;

    [MinamoStaticProperty(BuiltinMethodNames.Default)]
    internal static MinamoNil Default() => Nil;
}
