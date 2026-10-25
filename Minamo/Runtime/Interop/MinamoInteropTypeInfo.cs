using Minamo.Codegen;
using Minamo.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Minamo.Runtime.Types;

[MinamoType]
internal partial class MinamoInteropTypeInfo : MinamoTypeInfo
{
    private readonly Dictionary<(Type Type, string Name, bool TypeObject), MinamoFunction> instanceMembers = new();

    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Interop);

    public override int ReflectedTypeId => MinamoTypeCodes.Interop;

    #region Operations
    internal override void SetStaticMember(ExecutionContext ctx, HashString name, MinamoFunction func) => ctx.InvalidOperation();

    internal override void SetInstanceMember(ExecutionContext ctx, HashString name, MinamoFunction func) => ctx.InvalidOperation();

    internal override MinamoObject GetStaticMember(HashString nameStr, ExecutionContext ctx)
    {
        var name = (string)nameStr;
        if (!StaticMembers.TryGetValue(name, out var func))
        {
            func = InitializeStaticMember(name, ctx);

            if (func is not null)
            {
                StaticMembers.Add(name, func);
            }
        }

        if (func is null)
        {
            return ctx.StaticOperationNotSupported(name, ReflectedTypeId);
        }

        if (func.Auto)
        {
            return func.TryInvokeProperty(ctx, this);
        }

        return func;
    }

    private MinamoObject CreateNew(ExecutionContext ctx, MinamoObject self, MinamoObject args)
    {
        var interop = (MinamoInterop)self;
        var values = ((MinamoTuple)args).UnsafeAccess();
        var arr = values.Select(o => o.ToObject()).ToArray();
        object instance;
        var type = interop.Object as Type ?? interop.Type;

        try
        {
            instance = Activator.CreateInstance(type, arr)!;
            return new MinamoInterop(instance.GetType(), instance);
        }
        catch (Exception ex)
        {
            return ctx.ConstructorFailed(arr, type, ex);
        }
    }

    internal override MinamoObject GetInstanceMember(MinamoObject self, HashString nameStr, ExecutionContext ctx)
    {
        var interop = (MinamoInterop)self;
        var name = (string)nameStr;
        var typeObject = interop.Object is Type;

        var key = (interop.Type, name, typeObject);
        if (!instanceMembers.TryGetValue(key, out var func))
        {
            func = GetInteropFunction(interop, name, ctx);

            if (func is not null)
            {
                instanceMembers.Add(key, func);
            }
        }

        if (func is not null)
        {
            return func.TryInvokeProperty(ctx, self);
        }

        return ctx.OperationNotSupported(name, self);
    }

    private MinamoFunction? GetInteropFunction(MinamoInterop self, string name, ExecutionContext _)
    {
        var typeObject = self.Object is Type;
        if (typeObject && name == "new")
        {
            return new MinamoForeignConstructor(CreateNew);
        }

        var type = self.Type;
        var flags = BindingFlags.Public
            | (typeObject ? BindingFlags.Static : BindingFlags.Instance);
        var methods = type.GetOverloadedMethod(name, flags);
        var auto = false;

        if (methods is null)
        {
            name = Builtins.Getter(name);
            (methods, auto) = (type.GetOverloadedMethod(name, flags), true);
        }

        if (methods is null)
        {
            return null;
        }

        return new MinamoInteropFunction(name, type, methods, auto);
    }
    #endregion
}
