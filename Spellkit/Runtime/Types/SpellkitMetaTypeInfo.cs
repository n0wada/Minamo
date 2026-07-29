using Spellkit.Compiler;
using Spellkit.Debug;
using System.Collections.Generic;
using Spellkit.Codegen;
using Spellkit.Runtime.Types.Functions;

namespace Spellkit.Runtime.Types;

internal sealed class SpellkitMetaTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.TypeInfo);

    public override int ReflectedTypeId => SpellkitTypeCodes.TypeInfo;

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        var ret = ctx.RuntimeContext.Types[((SpellkitTypeInfo)arg).ReflectedTypeId].GetStaticMember(Builtins.String, ctx);

        if (ctx.HasErrors || ret is null)
        {
            return Nil;
        }

        return ret.Invoke(ctx);
    }

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg)
    {
        var ret = ctx.RuntimeContext.Types[((SpellkitTypeInfo)arg).ReflectedTypeId].GetStaticMember(Builtins.Length, ctx);

        if (ctx.HasErrors || ret is null)
        {
            return Nil;
        }

        return ret.Invoke(ctx);
    }
}
