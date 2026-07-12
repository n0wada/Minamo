using Spellkit.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Spellkit.Runtime.Types;

public sealed class SpkModule : SpkObject, IEnumerable<SpkObject>
{
    private readonly SpkObject[] globals;

    internal Unit Unit { get; }

    public override string TypeName => nameof(Spk.Module);

    public SpkModule(Unit unit, SpkObject[] globals) : base(Spk.Module) =>
        (Unit, this.globals) = (unit, globals);

    public override object ToObject() => Unit;

    public override bool Equals(SpkObject? other) => other is SpkModule m && ReferenceEquals(m.Unit, Unit);

    internal SpkObject GetMember(ExecutionContext ctx, SpkObject index)
    {
        if (index.TypeId is not Spk.String and not Spk.Char
            || !TryGetMember(ctx, index.ToString(), out var value))
        {
            ctx.Error = ErrorGenerators.RuntimeException(SpkError.IndexOutOfRange, index);
            return Nil;
        }

        return value!;
    }

    internal bool TryGetMember(ExecutionContext ctx, string name, out SpkObject? value)
    {
        value = null;

        if (Unit.ExportList.TryGetValue(name, out var sv))
        {
            if ((sv.Data & VarFlags.Private) == VarFlags.Private)
            {
                ctx.PrivateNameAccess(name);
            }

            value = globals[sv.Address >> 8];
            if (value is SpkFunction function && function.Auto)
            {
                value = function.TryInvokeProperty(ctx, this);
            }

            return true;
        }

        return false;
    }

    public IEnumerator<SpkObject> GetEnumerator()
    {
        foreach (var (key, sv) in Unit.ExportList)
        {
            if ((sv.Data & VarFlags.Private) != VarFlags.Private)
            {
                yield return new SpkTuple(new SpkLabel[] {
                    new("key", new SpkString(key)),
                    new("value", globals[sv.Address >> 8])
                    });
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override int GetHashCode() => HashCode.Combine(TypeId, Unit.Id);
}

internal sealed class SpkModuleTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Module);

    public override int ReflectedTypeId => Spk.Module;

    public SpkModuleTypeInfo() => AddMixins(Spk.Lookup, Spk.Sequence, Spk.Container);

    #region Operations
    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format) =>
        new SpkString("<" + GetModuleName((SpkModule)arg) + ">");

    private string GetModuleName(SpkModule arg)
    {
        if (arg.Unit is Linker.Lang)
        {
            return arg.Unit.FileName!;
        }
        else if (arg.Unit is Linker.ForeignUnit)
        {
            var type = arg.Unit.GetType();
            var nam = Attribute.GetCustomAttribute(type,
                typeof(Linker.SpkUnitAttribute)) is not Linker.SpkUnitAttribute attr ? type.Name : attr.Name;
            return "foreign." + nam + "," + Path.GetFileNameWithoutExtension(arg.Unit.FileName);
        }
        else
        {
            return "spellkit." + (arg.Unit.FileName is null ? "#memory#"
                : Path.GetFileNameWithoutExtension(arg.Unit.FileName));
        }
    }

    protected override SpkObject IterateOp(ExecutionContext ctx, SpkObject self) =>
        SpkIterator.Create((IEnumerable<SpkObject>)self);

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg)
    {
        var count = 0;

        foreach (var g in ((SpkModule)arg).Unit.ExportList)
        {
            if ((g.Value.Data & VarFlags.Private) != VarFlags.Private)
            {
                count++;
            }
        }

        return SpkInteger.Get(count);
    }

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkModule mod)
        {
            return ((SpkModule)left).Unit.Id == mod.Unit.Id ? True : False;
        }

        return False;
    }

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index) => ((SpkModule)self).GetMember(ctx, index);

    protected override SpkObject InOp(ExecutionContext ctx, SpkObject self, SpkObject field)
    {
        if (field.TypeId is not Spk.String and not Spk.Char)
        {
            return Nil;
        }

        var mod = (SpkModule)self;

        if (!mod.Unit.ExportList.TryGetValue(field.ToString(), out var sv))
        {
            return False;
        }

        return (sv.Data & VarFlags.Private) != VarFlags.Private ? True : False;
    }

    internal override SpkObject GetInstanceMember(SpkObject self, HashString name, ExecutionContext ctx)
    {
        var mod = (SpkModule)self;

        if (!mod.TryGetMember(ctx, (string)name, out var value))
        {
            return base.GetInstanceMember(self, name, ctx);
        }

        return value!;
    }
    #endregion
}
