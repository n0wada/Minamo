using System.Collections.Generic;

namespace Minamo.Runtime.Types;

public sealed class MinamoLabel : MinamoObject
{
    private List<MinamoTypeInfo>? typeAnnotations;

    public override string TypeName => nameof(MinamoTypeCodes.Label);
    
    public string Label { get; }

    public MinamoObject Value { get; internal set; }

    internal bool Mutable { get; set; }

    public MinamoLabel(string label, MinamoObject value, bool mutable = false) : base(MinamoTypeCodes.Label) =>
        (Label, Value, Mutable) = (label, value, mutable);

    public MinamoLabel(string label, object value, bool mutable = false) : base(MinamoTypeCodes.Label) =>
        (Label, Value, Mutable) = (label, TypeConverter.ConvertFrom(value), mutable);

    public override object ToObject() => Value.ToObject();

    internal void AddTypeAnnotation(MinamoTypeInfo ti)
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

    public override bool Equals(MinamoObject? other)
    {
        if (other is not MinamoLabel lab)
        {
            return false;
        }

        if (lab.Label != Label)
        {
            return false;
        }

        return ReferenceEquals(lab.Value, Value) || lab.Value.Equals(Value);
    }

    public override MinamoObject Clone() => new MinamoLabel(Label, Value.Clone(), Mutable);
}

internal sealed class MinamoLabelTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Label);

    public override int ReflectedTypeId => MinamoTypeCodes.Label;

    public MinamoLabelTypeInfo() => AddMixins(MinamoTypeCodes.Container);

    protected override MinamoObject InOp(ExecutionContext ctx, MinamoObject self, MinamoObject field) =>
        field.TypeId is MinamoTypeCodes.String or MinamoTypeCodes.Char && ((MinamoLabel)self).Label == field.ToString() ? True : False;

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        var lab = (MinamoLabel)arg;
        return new MinamoString(lab.Label + ": " + lab.Value.ToString(ctx).Value);
    }
}
