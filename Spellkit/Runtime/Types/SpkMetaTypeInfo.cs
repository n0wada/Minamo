using Spellkit.Compiler;
using Spellkit.Debug;
using System.Collections.Generic;
using Spellkit.Codegen;
using Spellkit.Runtime.Types.Functions;

namespace Spellkit.Runtime.Types;

internal sealed class SpkMetaTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.TypeInfo);

    public override int ReflectedTypeId => Spk.TypeInfo;

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        var ret = ctx.RuntimeContext.Types[((SpkTypeInfo)arg).ReflectedTypeId].GetStaticMember(Builtins.String, ctx);

        if (ctx.HasErrors || ret is null)
        {
            return Nil;
        }

        return ret.Invoke(ctx);
    }

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg)
    {
        var ret = ctx.RuntimeContext.Types[((SpkTypeInfo)arg).ReflectedTypeId].GetStaticMember(Builtins.Length, ctx);

        if (ctx.HasErrors || ret is null)
        {
            return Nil;
        }

        return ret.Invoke(ctx);
    }
}
