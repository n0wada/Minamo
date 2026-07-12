using System.Collections.Generic;

namespace Spellkit.Runtime.Types;

public sealed class SpkLabel : SpkObject
{
    private List<SpkTypeInfo>? typeAnnotations;

    public override string TypeName => nameof(Spk.Label);
    
    public string Label { get; }

    public SpkObject Value { get; internal set; }

    internal bool Mutable { get; set; }

    public SpkLabel(string label, SpkObject value, bool mutable = false) : base(Spk.Label) =>
        (Label, Value, Mutable) = (label, value, mutable);

    public SpkLabel(string label, object value, bool mutable = false) : base(Spk.Label) =>
        (Label, Value, Mutable) = (label, TypeConverter.ConvertFrom(value), mutable);

    public override object ToObject() => Value.ToObject();

    internal void AddTypeAnnotation(SpkTypeInfo ti)
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

    public override bool Equals(SpkObject? other)
    {
        if (other is not SpkLabel lab)
        {
            return false;
        }

        if (lab.Label != Label)
        {
            return false;
        }

        return ReferenceEquals(lab.Value, Value) || lab.Value.Equals(Value);
    }

    public override SpkObject Clone() => new SpkLabel(Label, Value.Clone(), Mutable);
}

internal sealed class SpkLabelTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Label);

    public override int ReflectedTypeId => Spk.Label;

    public SpkLabelTypeInfo() => AddMixins(Spk.Container);

    protected override SpkObject InOp(ExecutionContext ctx, SpkObject self, SpkObject field) =>
        field.TypeId is Spk.String or Spk.Char && ((SpkLabel)self).Label == field.ToString() ? True : False;

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        var lab = (SpkLabel)arg;
        return new SpkString(lab.Label + ": " + lab.Value.ToString(ctx).Value);
    }
}
