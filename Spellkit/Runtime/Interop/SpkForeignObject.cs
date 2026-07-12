using Spellkit.Linker;

namespace Spellkit.Runtime.Types;

public abstract class SpkForeignObject : SpkObject
{
    public override int TypeId => TypeInfo.ReflectedTypeId;

    public override string TypeName => TypeInfo.ReflectedTypeName;

    public SpkForeignTypeInfo TypeInfo { get; }

    protected SpkForeignObject(SpkForeignTypeInfo typeInfo) : base(-1) => TypeInfo = typeInfo;

    public override int GetHashCode() => HashCode.Combine(TypeId, TypeName);
}

public abstract class SpkForeignTypeInfo : SpkTypeInfo
{
    private int _reflectedTypeCode;
    public override sealed int ReflectedTypeId => _reflectedTypeCode;

    public ForeignUnit DeclaringUnit { get; internal set; } = null!;

    internal void SetReflectedTypeCode(int code) => _reflectedTypeCode = code;
}

public abstract class SpkForeignTypeInfo<T> : SpkForeignTypeInfo where T : ForeignUnit
{
    public new T DeclaringUnit => (T)base.DeclaringUnit;
}
