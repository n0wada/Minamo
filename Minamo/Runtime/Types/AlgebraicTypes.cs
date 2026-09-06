using Minamo.Compiler;
using Minamo.Debug;
using System.Collections.Generic;
using Minamo.Codegen;
using Minamo.Runtime.Types.Functions;

namespace Minamo.Runtime.Types;

internal sealed class MinamoOptionTypeInfo : MinamoForeignTypeInfo<Minamo.Linker.Lang>
{
    public override string ReflectedTypeName => "Option";

    public MinamoOptionTypeInfo()
    {
        AddMixins(MinamoTypeCodes.Lookup);
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    protected override MinamoFunction? InitializeStaticMember(string name, ExecutionContext ctx) =>
        name switch
        {
            "Some" => new MinamoExternalFunction("Some", false, CreateSome, new Par("x")),
            "None" => new MinamoExternalFunction("None", true, CreateNone),
            _ => base.InitializeStaticMember(name, ctx)
        };

    private MinamoObject CreateSome(ExecutionContext ctx, MinamoObject? _, MinamoObject[] args)
    {
        var fields = new MinamoTuple(new MinamoObject[] { new MinamoLabel("x", args[0]) });
        return new MinamoClass(this, "Some", fields, MinamoTuple.Empty, DeclaringUnit);
    }

    private MinamoObject CreateNone(ExecutionContext ctx, MinamoObject? _, MinamoObject[] args) =>
        new MinamoClass(this, "None", MinamoTuple.Empty, MinamoTuple.Empty, DeclaringUnit);

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        MinamoInteger.Get(((MinamoClass)arg).Fields.Count);

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index) =>
        ((MinamoClass)self).Fields.GetItem(ctx, index);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        var option = (MinamoClass)arg;

        IEnumerable<MinamoObject> Iterate()
        {
            var values = option.Fields.UnsafeAccess();
            for (var i = 0; i < option.Fields.Count; i++)
            {
                yield return values[i];
            }
        }

        return option.Fields.Count == 0
            ? new MinamoString($"Option.{option.Constructor}()")
            : new MinamoString($"Option.{option.Constructor}({(Iterate().ToLiteral(ctx))})");
    }
}

internal sealed class MinamoResultTypeInfo : MinamoForeignTypeInfo<Minamo.Linker.Lang>
{
    public override string ReflectedTypeName => "Result";

    public MinamoResultTypeInfo()
    {
        AddMixins(MinamoTypeCodes.Lookup);
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    protected override MinamoFunction? InitializeStaticMember(string name, ExecutionContext ctx) =>
        name switch
        {
            "Ok" => new MinamoExternalFunction("Ok", false, CreateOk, new Par("x")),
            "Err" => new MinamoExternalFunction("Err", false, CreateErr, new Par("y")),
            _ => base.InitializeStaticMember(name, ctx)
        };

    private MinamoObject CreateOk(ExecutionContext ctx, MinamoObject? _, MinamoObject[] args)
    {
        var fields = new MinamoTuple(new MinamoObject[] { new MinamoLabel("x", args[0]) });
        return new MinamoClass(this, "Ok", fields, MinamoTuple.Empty, DeclaringUnit);
    }

    private MinamoObject CreateErr(ExecutionContext ctx, MinamoObject? _, MinamoObject[] args)
    {
        var fields = new MinamoTuple(new MinamoObject[] { new MinamoLabel("y", args[0]) });
        return new MinamoClass(this, "Err", fields, MinamoTuple.Empty, DeclaringUnit);
    }

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        MinamoInteger.Get(((MinamoClass)arg).Fields.Count);

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index) =>
        ((MinamoClass)self).Fields.GetItem(ctx, index);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        var result = (MinamoClass)arg;

        IEnumerable<MinamoObject> Iterate()
        {
            var values = result.Fields.UnsafeAccess();
            for (var i = 0; i < result.Fields.Count; i++)
            {
                yield return values[i];
            }
        }

        return result.Fields.Count == 0
            ? new MinamoString($"Result.{result.Constructor}()")
            : new MinamoString($"Result.{result.Constructor}({(Iterate().ToLiteral(ctx))})");
    }
}
