using Spellkit.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Spellkit.Runtime.Types;

public sealed class SpellkitModule : SpellkitObject, IEnumerable<SpellkitObject>
{
    private readonly SpellkitObject[] globals;

    internal Unit Unit { get; }

    public override string TypeName => nameof(SpellkitTypeCodes.Module);

    public SpellkitModule(Unit unit, SpellkitObject[] globals) : base(SpellkitTypeCodes.Module) =>
        (Unit, this.globals) = (unit, globals);

    public override object ToObject() => Unit;

    public override bool Equals(SpellkitObject? other) => other is SpellkitModule m && ReferenceEquals(m.Unit, Unit);

    internal SpellkitObject GetMember(ExecutionContext ctx, SpellkitObject index)
    {
        if (index.TypeId is not SpellkitTypeCodes.String and not SpellkitTypeCodes.Char
            || !TryGetMember(ctx, index.ToString(), out var value))
        {
            ctx.Error = ErrorGenerators.RuntimeException(SpellkitError.IndexOutOfRange, index);
            return Nil;
        }

        return value!;
    }

    internal bool TryGetMember(ExecutionContext ctx, string name, out SpellkitObject? value)
    {
        value = null;

        if (Unit.ExportList.TryGetValue(name, out var sv))
        {
            if ((sv.Data & VarFlags.Private) == VarFlags.Private)
            {
                ctx.PrivateNameAccess(name);
            }

            value = globals[sv.Address >> 8];
            if (value is SpellkitFunction function && function.Auto)
            {
                value = function.TryInvokeProperty(ctx, this);
            }

            return true;
        }

        return false;
    }

    public IEnumerator<SpellkitObject> GetEnumerator()
    {
        foreach (var (key, sv) in Unit.ExportList)
        {
            if ((sv.Data & VarFlags.Private) != VarFlags.Private)
            {
                yield return new SpellkitTuple(new SpellkitLabel[] {
                    new("key", new SpellkitString(key)),
                    new("value", globals[sv.Address >> 8])
                    });
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override int GetHashCode() => HashCode.Combine(TypeId, Unit.Id);
}

internal sealed class SpellkitModuleTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Module);

    public override int ReflectedTypeId => SpellkitTypeCodes.Module;

    public SpellkitModuleTypeInfo() => AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Sequence, SpellkitTypeCodes.Container);

    #region Operations
    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString("<" + GetModuleName((SpellkitModule)arg) + ">");

    private string GetModuleName(SpellkitModule arg)
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
            return "spellkit." + (arg.Unit.FileName is null ? "#memory#"
                : Path.GetFileNameWithoutExtension(arg.Unit.FileName));
        }
    }

    protected override SpellkitObject IterateOp(ExecutionContext ctx, SpellkitObject self) =>
        SpellkitIterator.Create((IEnumerable<SpellkitObject>)self);

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg)
    {
        var count = 0;

        foreach (var g in ((SpellkitModule)arg).Unit.ExportList)
        {
            if ((g.Value.Data & VarFlags.Private) != VarFlags.Private)
            {
                count++;
            }
        }

        return SpellkitInteger.Get(count);
    }

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitModule mod)
        {
            return ((SpellkitModule)left).Unit.Id == mod.Unit.Id ? True : False;
        }

        return False;
    }

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index) => ((SpellkitModule)self).GetMember(ctx, index);

    protected override SpellkitObject InOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject field)
    {
        if (field.TypeId is not SpellkitTypeCodes.String and not SpellkitTypeCodes.Char)
        {
            return Nil;
        }

        var mod = (SpellkitModule)self;

        if (!mod.Unit.ExportList.TryGetValue(field.ToString(), out var sv))
        {
            return False;
        }

        return (sv.Data & VarFlags.Private) != VarFlags.Private ? True : False;
    }

    internal override SpellkitObject GetInstanceMember(SpellkitObject self, HashString name, ExecutionContext ctx)
    {
        var mod = (SpellkitModule)self;

        if (!mod.TryGetMember(ctx, (string)name, out var value))
        {
            return base.GetInstanceMember(self, name, ctx);
        }

        return value!;
    }
    #endregion
}
