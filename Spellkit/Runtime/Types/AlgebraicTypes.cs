using Spellkit.Compiler;
using Spellkit.Debug;
using System.Collections.Generic;
using Spellkit.Codegen;
using Spellkit.Runtime.Types.Functions;

namespace Spellkit.Runtime.Types;

internal sealed class SpellkitOptionTypeInfo : SpellkitForeignTypeInfo<Spellkit.Linker.Lang>
{
    public override string ReflectedTypeName => "Option";

    public SpellkitOptionTypeInfo()
    {
        AddMixins(SpellkitTypeCodes.Lookup);
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    protected override SpellkitFunction? InitializeStaticMember(string name, ExecutionContext ctx) =>
        name switch
        {
            "Some" => new SpellkitExternalFunction("Some", false, CreateSome, new Par("x")),
            "None" => new SpellkitExternalFunction("None", true, CreateNone),
            _ => base.InitializeStaticMember(name, ctx)
        };

    private SpellkitObject CreateSome(ExecutionContext ctx, SpellkitObject? _, SpellkitObject[] args)
    {
        var fields = new SpellkitTuple(new SpellkitObject[] { new SpellkitLabel("x", args[0]) });
        return new SpellkitClass(this, "Some", fields, SpellkitTuple.Empty, DeclaringUnit);
    }

    private SpellkitObject CreateNone(ExecutionContext ctx, SpellkitObject? _, SpellkitObject[] args) =>
        new SpellkitClass(this, "None", SpellkitTuple.Empty, SpellkitTuple.Empty, DeclaringUnit);

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitClass)arg).Fields.Count);

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index) =>
        ((SpellkitClass)self).Fields.GetItem(ctx, index);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        var option = (SpellkitClass)arg;

        IEnumerable<SpellkitObject> Iterate()
        {
            var values = option.Fields.UnsafeAccess();
            for (var i = 0; i < option.Fields.Count; i++)
            {
                yield return values[i];
            }
        }

        return option.Fields.Count == 0
            ? new SpellkitString($"Option.{option.Constructor}()")
            : new SpellkitString($"Option.{option.Constructor}({(Iterate().ToLiteral(ctx))})");
    }
}

internal sealed class SpellkitResultTypeInfo : SpellkitForeignTypeInfo<Spellkit.Linker.Lang>
{
    public override string ReflectedTypeName => "Result";

    public SpellkitResultTypeInfo()
    {
        AddMixins(SpellkitTypeCodes.Lookup);
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    protected override SpellkitFunction? InitializeStaticMember(string name, ExecutionContext ctx) =>
        name switch
        {
            "Ok" => new SpellkitExternalFunction("Ok", false, CreateOk, new Par("x")),
            "Err" => new SpellkitExternalFunction("Err", false, CreateErr, new Par("y")),
            _ => base.InitializeStaticMember(name, ctx)
        };

    private SpellkitObject CreateOk(ExecutionContext ctx, SpellkitObject? _, SpellkitObject[] args)
    {
        var fields = new SpellkitTuple(new SpellkitObject[] { new SpellkitLabel("x", args[0]) });
        return new SpellkitClass(this, "Ok", fields, SpellkitTuple.Empty, DeclaringUnit);
    }

    private SpellkitObject CreateErr(ExecutionContext ctx, SpellkitObject? _, SpellkitObject[] args)
    {
        var fields = new SpellkitTuple(new SpellkitObject[] { new SpellkitLabel("y", args[0]) });
        return new SpellkitClass(this, "Err", fields, SpellkitTuple.Empty, DeclaringUnit);
    }

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitClass)arg).Fields.Count);

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index) =>
        ((SpellkitClass)self).Fields.GetItem(ctx, index);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        var result = (SpellkitClass)arg;

        IEnumerable<SpellkitObject> Iterate()
        {
            var values = result.Fields.UnsafeAccess();
            for (var i = 0; i < result.Fields.Count; i++)
            {
                yield return values[i];
            }
        }

        return result.Fields.Count == 0
            ? new SpellkitString($"Result.{result.Constructor}()")
            : new SpellkitString($"Result.{result.Constructor}({(Iterate().ToLiteral(ctx))})");
    }
}
