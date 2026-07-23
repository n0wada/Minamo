using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;

namespace Spellkit.Linker;

internal interface IModuleProvider
{
    bool TryGetUnit(string name, out Unit unit);
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
