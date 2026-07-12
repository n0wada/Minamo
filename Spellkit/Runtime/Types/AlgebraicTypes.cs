using Spellkit.Compiler;
using Spellkit.Debug;
using System.Collections.Generic;
using Spellkit.Codegen;
using Spellkit.Runtime.Types.Functions;

namespace Spellkit.Runtime.Types;

internal sealed class SpkOptionTypeInfo : SpkForeignTypeInfo<Spellkit.Linker.Lang>
{
    public override string ReflectedTypeName => "Option";

    public SpkOptionTypeInfo()
    {
        AddMixins(Spk.Lookup);
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    protected override SpkFunction? InitializeStaticMember(string name, ExecutionContext ctx) =>
        name switch
        {
            "Some" => new SpkExternalFunction("Some", false, CreateSome, new Par("x")),
            "None" => new SpkExternalFunction("None", true, CreateNone),
            _ => base.InitializeStaticMember(name, ctx)
        };

    private SpkObject CreateSome(ExecutionContext ctx, SpkObject? _, SpkObject[] args)
    {
        var fields = new SpkTuple(new SpkObject[] { new SpkLabel("x", args[0]) });
        return new SpkClass(this, "Some", fields, SpkTuple.Empty, DeclaringUnit);
    }

    private SpkObject CreateNone(ExecutionContext ctx, SpkObject? _, SpkObject[] args) =>
        new SpkClass(this, "None", SpkTuple.Empty, SpkTuple.Empty, DeclaringUnit);

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg) =>
        SpkInteger.Get(((SpkClass)arg).Fields.Count);

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index) =>
        ((SpkClass)self).Fields.GetItem(ctx, index);

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        var option = (SpkClass)arg;

        IEnumerable<SpkObject> Iterate()
        {
            var values = option.Fields.UnsafeAccess();
            for (var i = 0; i < option.Fields.Count; i++)
            {
                yield return values[i];
            }
        }

        return option.Fields.Count == 0
            ? new SpkString($"Option.{option.Constructor}()")
            : new SpkString($"Option.{option.Constructor}({(Iterate().ToLiteral(ctx))})");
    }
}

internal sealed class SpkResultTypeInfo : SpkForeignTypeInfo<Spellkit.Linker.Lang>
{
    public override string ReflectedTypeName => "Result";

    public SpkResultTypeInfo()
    {
        AddMixins(Spk.Lookup);
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    protected override SpkFunction? InitializeStaticMember(string name, ExecutionContext ctx) =>
        name switch
        {
            "Ok" => new SpkExternalFunction("Ok", false, CreateOk, new Par("x")),
            "Err" => new SpkExternalFunction("Err", false, CreateErr, new Par("y")),
            _ => base.InitializeStaticMember(name, ctx)
        };

    private SpkObject CreateOk(ExecutionContext ctx, SpkObject? _, SpkObject[] args)
    {
        var fields = new SpkTuple(new SpkObject[] { new SpkLabel("x", args[0]) });
        return new SpkClass(this, "Ok", fields, SpkTuple.Empty, DeclaringUnit);
    }

    private SpkObject CreateErr(ExecutionContext ctx, SpkObject? _, SpkObject[] args)
    {
        var fields = new SpkTuple(new SpkObject[] { new SpkLabel("y", args[0]) });
        return new SpkClass(this, "Err", fields, SpkTuple.Empty, DeclaringUnit);
    }

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg) =>
        SpkInteger.Get(((SpkClass)arg).Fields.Count);

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index) =>
        ((SpkClass)self).Fields.GetItem(ctx, index);

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        var result = (SpkClass)arg;

        IEnumerable<SpkObject> Iterate()
        {
            var values = result.Fields.UnsafeAccess();
            for (var i = 0; i < result.Fields.Count; i++)
            {
                yield return values[i];
            }
        }

        return result.Fields.Count == 0
            ? new SpkString($"Result.{result.Constructor}()")
            : new SpkString($"Result.{result.Constructor}({(Iterate().ToLiteral(ctx))})");
    }
}
