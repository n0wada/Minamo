using System.Collections.Generic;

namespace Spellkit.Runtime.Types;

public sealed class SpellkitLabel : SpellkitObject
{
    private List<SpellkitTypeInfo>? typeAnnotations;

    public override string TypeName => nameof(SpellkitTypeCodes.Label);
    
    public string Label { get; }

    public SpellkitObject Value { get; internal set; }

    internal bool Mutable { get; set; }

    public SpellkitLabel(string label, SpellkitObject value, bool mutable = false) : base(SpellkitTypeCodes.Label) =>
        (Label, Value, Mutable) = (label, value, mutable);

    public SpellkitLabel(string label, object value, bool mutable = false) : base(SpellkitTypeCodes.Label) =>
        (Label, Value, Mutable) = (label, TypeConverter.ConvertFrom(value), mutable);

    public override object ToObject() => Value.ToObject();

    internal void AddTypeAnnotation(SpellkitTypeInfo ti)
    {
        typeAnnotations ??= new();
        typeAnnotations.Add(ti);
    }

    internal bool VerifyType(int tid)
    {
        if (typeAnnotations is null)
        {
            return true;
        }

        foreach (var t in typeAnnotations)
        {
            if (t.ReflectedTypeId == tid)
            {
                return true;
            }
        }

        return false;
    }

    public override int GetHashCode() => HashCode.Combine(Label, Value);

    public override bool Equals(SpellkitObject? other)
    {
        if (other is not SpellkitLabel lab)
        {
            return false;
        }

        if (lab.Label != Label)
        {
            return false;
        }

        return ReferenceEquals(lab.Value, Value) || lab.Value.Equals(Value);
    }

    public override SpellkitObject Clone() => new SpellkitLabel(Label, Value.Clone(), Mutable);
}

internal sealed class SpellkitLabelTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Label);

    public override int ReflectedTypeId => SpellkitTypeCodes.Label;

    public SpellkitLabelTypeInfo() => AddMixins(SpellkitTypeCodes.Container);

    protected override SpellkitObject InOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject field) =>
        field.TypeId is SpellkitTypeCodes.String or SpellkitTypeCodes.Char && ((SpellkitLabel)self).Label == field.ToString() ? True : False;

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        var lab = (SpellkitLabel)arg;
        return new SpellkitString(lab.Label + ": " + lab.Value.ToString(ctx).Value);
    }
}
