using Minamo.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Minamo.Runtime.Types;

public sealed class MinamoModule : MinamoObject, IEnumerable<MinamoObject>
{
    private readonly MinamoObject[] globals;

    internal Unit Unit { get; }

    public override string TypeName => nameof(MinamoTypeCodes.Module);

    public MinamoModule(Unit unit, MinamoObject[] globals) : base(MinamoTypeCodes.Module) =>
        (Unit, this.globals) = (unit, globals);

    public override object ToObject() => Unit;

    public override bool Equals(MinamoObject? other) => other is MinamoModule m && ReferenceEquals(m.Unit, Unit);

    internal MinamoObject GetMember(ExecutionContext ctx, MinamoObject index)
    {
        if (index.TypeId is not MinamoTypeCodes.String and not MinamoTypeCodes.Char
            || !TryGetMember(ctx, index.ToString(), out var value))
        {
            ctx.Error = ErrorGenerators.RuntimeException(MinamoError.IndexOutOfRange, index);
            return Nil;
        }

        return value!;
    }

    internal bool TryGetMember(ExecutionContext ctx, string name, out MinamoObject? value)
    {
        value = null;

        if (Unit.ExportList.TryGetValue(name, out var sv))
        {
            if ((sv.Data & VarFlags.Private) == VarFlags.Private)
            {
                ctx.PrivateNameAccess(name);
            }

            value = globals[sv.Address >> 8];
            if (value is MinamoFunction function && function.Auto)
            {
                value = function.TryInvokeProperty(ctx, this);
            }

            return true;
        }

        return false;
    }

    public IEnumerator<MinamoObject> GetEnumerator()
    {
        foreach (var (key, sv) in Unit.ExportList)
        {
            if ((sv.Data & VarFlags.Private) != VarFlags.Private)
            {
                yield return new MinamoTuple(new MinamoLabel[] {
                    new("key", new MinamoString(key)),
                    new("value", globals[sv.Address >> 8])
                    });
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override int GetHashCode() => HashCode.Combine(TypeId, Unit.Id);
}

internal sealed class MinamoModuleTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Module);

    public override int ReflectedTypeId => MinamoTypeCodes.Module;

    public MinamoModuleTypeInfo() => AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Sequence, MinamoTypeCodes.Container);

    #region Operations
    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString("<" + GetModuleName((MinamoModule)arg) + ">");

    private string GetModuleName(MinamoModule arg)
    {
        if (arg.Unit is Linker.Lang)
        {
            return arg.Unit.FileName!;
        }
        else if (arg.Unit is Linker.ForeignUnit)
        {
            var type = arg.Unit.GetType();
            return "foreign." + type.Name + "," + Path.GetFileNameWithoutExtension(arg.Unit.FileName);
        }
        else
        {
            return "minamo." + (arg.Unit.FileName is null ? "#memory#"
                : Path.GetFileNameWithoutExtension(arg.Unit.FileName));
        }
    }

    protected override MinamoObject IterateOp(ExecutionContext ctx, MinamoObject self) =>
        MinamoIterator.Create((IEnumerable<MinamoObject>)self);

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg)
    {
        var count = 0;

        foreach (var g in ((MinamoModule)arg).Unit.ExportList)
        {
            if ((g.Value.Data & VarFlags.Private) != VarFlags.Private)
            {
                count++;
            }
        }

        return MinamoInteger.Get(count);
    }

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoModule mod)
        {
            return ((MinamoModule)left).Unit.Id == mod.Unit.Id ? True : False;
        }

        return False;
    }

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index) => ((MinamoModule)self).GetMember(ctx, index);

    protected override MinamoObject InOp(ExecutionContext ctx, MinamoObject self, MinamoObject field)
    {
        if (field.TypeId is not MinamoTypeCodes.String and not MinamoTypeCodes.Char)
        {
            return Nil;
        }

        var mod = (MinamoModule)self;

        if (!mod.Unit.ExportList.TryGetValue(field.ToString(), out var sv))
        {
            return False;
        }

        return (sv.Data & VarFlags.Private) != VarFlags.Private ? True : False;
    }

    internal override MinamoObject GetInstanceMember(MinamoObject self, HashString name, ExecutionContext ctx)
    {
        var mod = (MinamoModule)self;

        if (!mod.TryGetMember(ctx, (string)name, out var value))
        {
            return base.GetInstanceMember(self, name, ctx);
        }

        return value!;
    }
    #endregion
}
