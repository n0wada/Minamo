using Minamo.Compiler;
using Minamo.Debug;
using System.Collections.Generic;
using Minamo.Codegen;
using Minamo.Runtime.Types.Functions;

namespace Minamo.Runtime.Types;

internal sealed class MinamoMetaTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.TypeInfo);

    public override int ReflectedTypeId => MinamoTypeCodes.TypeInfo;

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        var ret = ctx.RuntimeContext.Types[((MinamoTypeInfo)arg).ReflectedTypeId].GetStaticMember(Builtins.String, ctx);

        if (ctx.HasErrors || ret is null)
        {
            return Nil;
        }

        return ret.Invoke(ctx);
    }

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg)
    {
        var ret = ctx.RuntimeContext.Types[((MinamoTypeInfo)arg).ReflectedTypeId].GetStaticMember(Builtins.Length, ctx);

        if (ctx.HasErrors || ret is null)
        {
            return Nil;
        }

        return ret.Invoke(ctx);
    }
}
