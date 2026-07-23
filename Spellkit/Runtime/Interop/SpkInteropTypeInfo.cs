using Spellkit.Codegen;
using Spellkit.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Spellkit.Runtime.Types;

[SpkType]
internal partial class SpkInteropTypeInfo : SpkTypeInfo
{
    private readonly Dictionary<(Type Type, string Name, bool TypeObject), SpkFunction> instanceMembers = new();

    private static readonly SpkInterop TypeInt32 = new(BCL.Int32);
    private static readonly SpkInterop TypeInt64 = new(BCL.Int64);
    private static readonly SpkInterop TypeUInt32 = new(BCL.UInt32);
    private static readonly SpkInterop TypeUInt64 = new(BCL.UInt64);
    private static readonly SpkInterop TypeByte = new(BCL.Byte);
    private static readonly SpkInterop TypeSByte = new(BCL.SByte);
    private static readonly SpkInterop TypeChar = new(BCL.Char);
    private static readonly SpkInterop TypeString = new(BCL.String);
    private static readonly SpkInterop TypeBoolean = new(BCL.Boolean);
    private static readonly SpkInterop TypeDouble = new(BCL.Double);
    private static readonly SpkInterop TypeSingle = new(BCL.Single);
    private static readonly SpkInterop TypeSystemArray = new(BCL.Array);
    private static readonly SpkInterop TypeSystemType = new(BCL.Type);

    private const BindingFlags AllBindingFlags = BindingFlags.NonPublic | BindingFlags.Public 
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    public override string ReflectedTypeName => nameof(Spk.Interop);

    public override int ReflectedTypeId => Spk.Interop;

    #region Operations
    internal override void SetStaticMember(ExecutionContext ctx, HashString name, SpkFunction func) => ctx.InvalidOperation();

    internal override void SetInstanceMember(ExecutionContext ctx, HashString name, SpkFunction func) => ctx.InvalidOperation();

    internal override SpkObject GetStaticMember(HashString nameStr, ExecutionContext ctx)
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

    private SpkObject CreateNew(ExecutionContext ctx, SpkObject self, SpkObject args)
    {
        var interop = (SpkInterop)self;
        var values = ((SpkTuple)args).UnsafeAccess();
        var arr = values.Select(o => o.ToObject()).ToArray();
        object instance;
        var type = interop.Object as Type ?? interop.Type;

        try
        {
            instance = Activator.CreateInstance(type, arr)!;
            return new SpkInterop(instance.GetType(), instance);
        }
        catch (Exception ex)
        {
            return ctx.ConstructorFailed(arr, type, ex);
        }
    }

    internal override SpkObject GetInstanceMember(SpkObject self, HashString nameStr, ExecutionContext ctx)
    {
        var interop = (SpkInterop)self;
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

    private SpkFunction? GetInteropFunction(SpkInterop self, string name, ExecutionContext _)
    {
        var typeObject = self.Object is Type;
        if (typeObject && name == "new")
        {
            return new SpkForeignConstructor(CreateNew);
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

        return new SpkInteropFunction(name, type, methods, auto);
    }
    #endregion

    internal static SpkObject CreateInteropObject(ExecutionContext ctx, string typeName)
    {
        var typeInfo = Type.GetType(typeName, throwOnError: false);

        if (typeInfo is null)
        {
            return ctx.InvalidValue(typeName);
        }

        return new SpkInterop(typeInfo);
    }

    internal static SpkObject GetSystemType(ExecutionContext ctx, SpkObject typeName)
    {
        if (typeName is SpkInterop obj)
        {
            return new SpkInterop(BCL.Type, obj.Type);
        }
        else if (typeName.TypeId is not (Spk.String or Spk.Char))
        {
            throw new SpkCodeException(SpkError.InvalidType, typeName);
        }

        var str = typeName.ToString();
        var key = nameof(SpkInteropTypeInfo) + "_x235_" + typeName.ToString();

        if (!ctx.TryGetContextVariable(key, out var ret))
        {
            var typeInfo = Type.GetType(str, throwOnError: false);

            if (typeInfo is null)
            {
                return ctx.InvalidValue(typeName);
            }

            ret = new SpkInterop(BCL.Type, typeInfo);
            ctx.SetContextVariable(key, ret);
        }

        return (SpkObject)ret!;
    }

    internal static SpkObject Wrap(SpkObject value) => new SpkInterop(value.GetType(), value);

    internal static SpkObject ConvertTo(ExecutionContext ctx, SpkInterop type, SpkObject value)
    {
        if (type.Object is not Type typ)
        {
            throw new SpkCodeException(SpkError.InvalidType, type);
        }

        var ret = TypeConverter.ConvertTo(ctx, value, typ);

        if (ret is null)
        {
            return Nil;
        }

        return new SpkInterop(typ, ret);
    }

    internal static SpkObject ConvertFrom(SpkObject value)
    {
        if (value is not SpkInterop interop)
        {
            return value;
        }

        return TypeConverter.ConvertFrom(interop.Object) ?? value;
    }

    internal static SpkObject CreateArray(ExecutionContext _, SpkInterop type, int size)
    {
        if (type.Object is not Type t)
        {
            throw new SpkCodeException(SpkError.InvalidType, type);
        }

        var arr = Array.CreateInstance(t, size);
        return new SpkInterop(arr.GetType(), arr);
    }

    internal static SpkObject GetMethod(ExecutionContext ctx, SpkInterop type, string name, SpkObject[]? parameterTypes = null, int typeArguments = 0)
    {
        if (type.Object is not Type typ)
        {
            throw new SpkCodeException(SpkError.InvalidType, type);
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

                return new SpkInterop(typeof(MethodInfo), mi);
            }
        }

        return ctx.MethodNotFound(name, typ, parameterTypes);
    }

    internal static bool ParametersMatch(ParameterInfo[] parameters, SpkObject[] types)
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

    internal static SpkObject GetField(ExecutionContext _, SpkInterop type, string name)
    {
        if (type.Object is not Type typ)
        {
            throw new SpkCodeException(SpkError.InvalidType, type);
        }

        var ret = typ.GetField(name);
        return ret is not null ? new SpkInterop(typeof(FieldInfo), ret) : Nil;
    }

    internal static SpkObject Int32() => TypeInt32;

    internal static SpkObject Int64() => TypeInt64;

    internal static SpkObject UInt32() => TypeUInt32;

    internal static SpkObject UInt64() => TypeUInt64;

    internal static SpkObject Byte() => TypeByte;

    internal static SpkObject SByte() => TypeSByte;

    internal static SpkObject Char() => TypeChar;

    internal static SpkObject String() => TypeString;

    internal static SpkObject Boolean() => TypeBoolean;

    internal static SpkObject Double() => TypeDouble;

    internal static SpkObject Single() => TypeSingle;
    
    internal static SpkObject SystemArray() => TypeSystemArray;

    internal static SpkObject SystemType() => TypeSystemType;
}
