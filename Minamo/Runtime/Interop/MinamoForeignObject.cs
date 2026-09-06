using Minamo.Linker;

namespace Minamo.Runtime.Types;

public abstract class MinamoForeignObject : MinamoObject
{
    public override int TypeId => TypeInfo.ReflectedTypeId;

    public override string TypeName => TypeInfo.ReflectedTypeName;

    public MinamoForeignTypeInfo TypeInfo { get; }

    protected MinamoForeignObject(MinamoForeignTypeInfo typeInfo) : base(-1) => TypeInfo = typeInfo;

    // Hash-based collections normally use foreign objects by identity. Foreign values whose
    // Equals and GetHashCode are stable for their lifetime can opt into value-key semantics.
    public virtual bool HasStableValueEquality => false;

    public override int GetHashCode() => HashCode.Combine(TypeId, TypeName);
}

public abstract class MinamoForeignTypeInfo : MinamoTypeInfo
{
    private int _reflectedTypeCode;
    public override sealed int ReflectedTypeId => _reflectedTypeCode;

    public ForeignUnit DeclaringUnit { get; internal set; } = null!;

    internal void SetReflectedTypeCode(int code) => _reflectedTypeCode = code;
}

public abstract class MinamoForeignTypeInfo<T> : MinamoForeignTypeInfo where T : ForeignUnit
{
    public new T DeclaringUnit => (T)base.DeclaringUnit;
}
