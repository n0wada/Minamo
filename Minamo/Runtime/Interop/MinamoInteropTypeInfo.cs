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

    private static readonly MinamoInterop TypeInt32 = new(BCL.Int32);
    private static readonly MinamoInterop TypeInt64 = new(BCL.Int64);
    private static readonly MinamoInterop TypeUInt32 = new(BCL.UInt32);
    private static readonly MinamoInterop TypeUInt64 = new(BCL.UInt64);
    private static readonly MinamoInterop TypeByte = new(BCL.Byte);
    private static readonly MinamoInterop TypeSByte = new(BCL.SByte);
    private static readonly MinamoInterop TypeChar = new(BCL.Char);
    private static readonly MinamoInterop TypeString = new(BCL.String);
    private static readonly MinamoInterop TypeBoolean = new(BCL.Boolean);
    private static readonly MinamoInterop TypeDouble = new(BCL.Double);
    private static readonly MinamoInterop TypeSingle = new(BCL.Single);
    private static readonly MinamoInterop TypeSystemArray = new(BCL.Array);
    private static readonly MinamoInterop TypeSystemType = new(BCL.Type);

    private const BindingFlags AllBindingFlags = BindingFlags.NonPublic | BindingFlags.Public 
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

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

    internal static MinamoObject CreateInteropObject(ExecutionContext ctx, string typeName)
    {
        var typeInfo = Type.GetType(typeName, throwOnError: false);

        if (typeInfo is null)
        {
            return ctx.InvalidValue(typeName);
        }

        return new MinamoInterop(typeInfo);
    }

    internal static MinamoObject GetSystemType(ExecutionContext ctx, MinamoObject typeName)
    {
        if (typeName is MinamoInterop obj)
        {
            return new MinamoInterop(BCL.Type, obj.Type);
        }
        else if (typeName.TypeId is not (MinamoTypeCodes.String or MinamoTypeCodes.Char))
        {
            throw new MinamoCodeException(MinamoError.InvalidType, typeName);
        }

        var str = typeName.ToString();
        var key = nameof(MinamoInteropTypeInfo) + "_x235_" + typeName.ToString();

        if (!ctx.TryGetContextVariable(key, out var ret))
        {
            var typeInfo = Type.GetType(str, throwOnError: false);

            if (typeInfo is null)
            {
                return ctx.InvalidValue(typeName);
            }

            ret = new MinamoInterop(BCL.Type, typeInfo);
            ctx.SetContextVariable(key, ret);
        }

        return (MinamoObject)ret!;
    }

    internal static MinamoObject Wrap(MinamoObject value) => new MinamoInterop(value.GetType(), value);

    internal static MinamoObject ConvertTo(ExecutionContext ctx, MinamoInterop type, MinamoObject value)
    {
        if (type.Object is not Type typ)
        {
            throw new MinamoCodeException(MinamoError.InvalidType, type);
        }

        var ret = TypeConverter.ConvertTo(ctx, value, typ);

        if (ret is null)
        {
            return Nil;
        }

        return new MinamoInterop(typ, ret);
    }

    internal static MinamoObject ConvertFrom(MinamoObject value)
    {
        if (value is not MinamoInterop interop)
        {
            return value;
        }

        return TypeConverter.ConvertFrom(interop.Object) ?? value;
    }

    internal static MinamoObject CreateArray(ExecutionContext _, MinamoInterop type, int size)
    {
        if (type.Object is not Type t)
        {
            throw new MinamoCodeException(MinamoError.InvalidType, type);
        }

        var arr = Array.CreateInstance(t, size);
        return new MinamoInterop(arr.GetType(), arr);
    }

    internal static MinamoObject GetMethod(ExecutionContext ctx, MinamoInterop type, string name, MinamoObject[]? parameterTypes = null, int typeArguments = 0)
    {
        if (type.Object is not Type typ)
        {
            throw new MinamoCodeException(MinamoError.InvalidType, type);
        }

        foreach (var mi in typ.GetMethods(AllBindingFlags))
        {
            if (mi.Name == name && mi.GetGenericArguments().Length == typeArguments)
            {
                if (parameterTypes is not null)
                {
                    var mpars = mi.GetParameters();

                    if (parameterTypes.Length != mpars.Length
                        || !ParametersMatch(mpars, parameterTypes))
                    {
                        continue;
                    }
                }

                return new MinamoInterop(typeof(MethodInfo), mi);
            }
        }

        return ctx.MethodNotFound(name, typ, parameterTypes);
    }

    internal static bool ParametersMatch(ParameterInfo[] parameters, MinamoObject[] types)
    {
        if (parameters.Length != types.Length)
        {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var t = types[i].ToObject();

            if (t is not Type type || !parameters[i].ParameterType.IsAssignableFrom(type))
            {
                return false;
            }
        }

        return true;
    }

    internal static MinamoObject GetField(ExecutionContext _, MinamoInterop type, string name)
    {
        if (type.Object is not Type typ)
        {
            throw new MinamoCodeException(MinamoError.InvalidType, type);
        }

        var ret = typ.GetField(name);
        return ret is not null ? new MinamoInterop(typeof(FieldInfo), ret) : Nil;
    }

    internal static MinamoObject Int32() => TypeInt32;

    internal static MinamoObject Int64() => TypeInt64;

    internal static MinamoObject UInt32() => TypeUInt32;

    internal static MinamoObject UInt64() => TypeUInt64;

    internal static MinamoObject Byte() => TypeByte;

    internal static MinamoObject SByte() => TypeSByte;

    internal static MinamoObject Char() => TypeChar;

    internal static MinamoObject String() => TypeString;

    internal static MinamoObject Boolean() => TypeBoolean;

    internal static MinamoObject Double() => TypeDouble;

    internal static MinamoObject Single() => TypeSingle;
    
    internal static MinamoObject SystemArray() => TypeSystemArray;

    internal static MinamoObject SystemType() => TypeSystemType;
}
