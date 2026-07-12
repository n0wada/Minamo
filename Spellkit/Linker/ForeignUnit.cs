using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;

namespace Spellkit.Linker;

internal interface IModuleProvider
{
    bool TryGetUnit(string name, out Unit unit);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class SpkUnitAttribute : Attribute
{
    public SpkUnitAttribute(string name) => Name = name;

    public string Name { get; }
}

public sealed class Reference<T> where T : ForeignUnit
{
    private readonly Reference @ref;

    public Reference(Reference @ref) => this.@ref = @ref;

    public T Value
    {
        get
        {
            if (@ref.Instance is null)
            {
                throw new SpkException($"Reference \"{@ref.ModuleName}\" not initialized.");
            }

            return (T)@ref.Instance;
        }
    }
}

public abstract class ForeignUnit : Unit
{
    protected static readonly SpkObject Nil = SpkNil.Instance;

    private bool initialized;
    private RuntimeContext? initializedTypesContext;
    private readonly Dictionary<Type, SpkForeignTypeInfo> typeInfos = new();

    internal List<SpkForeignTypeInfo> Types { get; }

    internal List<SpkObject> Values { get; } = new();

    protected RuntimeContext RuntimeContext { get; private set; }

    protected ForeignUnit()
    {
        RuntimeContext = null!;
        Types = new();
        InitializeMembers();
        InitializeTypes();
        UnitIds.Add(0); //Self reference, to mimic the behavior of regular units
    }

    internal T GetTypeInfo<T>() where T : SpkForeignTypeInfo => (T)typeInfos[typeof(T)];

    protected void Add(string name, SpkObject obj)
    {
        ExportList.Add(name, new ScopeVar(0 | ExportList.Count << 8, VarFlags.Foreign));
        Values.Add(obj);
    }

    protected T AddType<T>() where T : SpkForeignTypeInfo, new()
    {
        var t = new T();
        typeInfos.Add(typeof(T), t);
        Types.Add(t);
        Add(t.ReflectedTypeName, t);
        t.DeclaringUnit = this;
        return t;
    }

    protected Reference<T> AddReference<T>() where T : ForeignUnit
    {
        var ti = typeof(T);

        if (Attribute.GetCustomAttribute(ti, typeof(SpkUnitAttribute)) is not SpkUnitAttribute attr)
        {
            throw new SpkException("Invalid reference.");
        }

        var asmName = ti.Assembly.GetName().Name + ".dll";
        var rf = new Reference(Guid.NewGuid(), attr.Name, null, asmName, default, null);
        UnitIds.Add(-1); //Real handles are added by a linker
        References.Add(rf);
        return new Reference<T>(rf);
    }

    public void Initialize(ExecutionContext ctx)
    {
        if (!ReferenceEquals(initializedTypesContext, ctx.RuntimeContext))
        {
            foreach (var t in Types)
            {
                t.SetReflectedTypeCode(ctx.RuntimeContext.Types.Count);
                ctx.RuntimeContext.Types.Add(t);
            }

            initializedTypesContext = ctx.RuntimeContext;
        }

        RuntimeContext = ctx.RuntimeContext;

        if (!initialized)
        {
            Execute(ctx);
            initialized = true;
        }
    }

    protected virtual void Execute(ExecutionContext ctx) { }

    protected virtual void InitializeTypes() { }

    protected virtual void InitializeMembers() { }
}
