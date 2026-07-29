using Spellkit.Codegen;
using Spellkit.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Spellkit.Runtime.Types;

[SpellkitType]
internal partial class SpellkitInteropTypeInfo : SpellkitTypeInfo
{
    private readonly Dictionary<(Type Type, string Name, bool TypeObject), SpellkitFunction> instanceMembers = new();

    private static readonly SpellkitInterop TypeInt32 = new(BCL.Int32);
    private static readonly SpellkitInterop TypeInt64 = new(BCL.Int64);
    private static readonly SpellkitInterop TypeUInt32 = new(BCL.UInt32);
    private static readonly SpellkitInterop TypeUInt64 = new(BCL.UInt64);
    private static readonly SpellkitInterop TypeByte = new(BCL.Byte);
    private static readonly SpellkitInterop TypeSByte = new(BCL.SByte);
    private static readonly SpellkitInterop TypeChar = new(BCL.Char);
    private static readonly SpellkitInterop TypeString = new(BCL.String);
    private static readonly SpellkitInterop TypeBoolean = new(BCL.Boolean);
    private static readonly SpellkitInterop TypeDouble = new(BCL.Double);
    private static readonly SpellkitInterop TypeSingle = new(BCL.Single);
    private static readonly SpellkitInterop TypeSystemArray = new(BCL.Array);
    private static readonly SpellkitInterop TypeSystemType = new(BCL.Type);

    private const BindingFlags AllBindingFlags = BindingFlags.NonPublic | BindingFlags.Public 
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Interop);

    public override int ReflectedTypeId => SpellkitTypeCodes.Interop;

    #region Operations
    internal override void SetStaticMember(ExecutionContext ctx, HashString name, SpellkitFunction func) => ctx.InvalidOperation();

    internal override void SetInstanceMember(ExecutionContext ctx, HashString name, SpellkitFunction func) => ctx.InvalidOperation();

    internal override SpellkitObject GetStaticMember(HashString nameStr, ExecutionContext ctx)
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

    private SpellkitObject CreateNew(ExecutionContext ctx, SpellkitObject self, SpellkitObject args)
    {
        var interop = (SpellkitInterop)self;
        var values = ((SpellkitTuple)args).UnsafeAccess();
        var arr = values.Select(o => o.ToObject()).ToArray();
        object instance;
        var type = interop.Object as Type ?? interop.Type;

        try
        {
            instance = Activator.CreateInstance(type, arr)!;
            return new SpellkitInterop(instance.GetType(), instance);
        }
        catch (Exception ex)
        {
            return ctx.ConstructorFailed(arr, type, ex);
        }
    }

    internal override SpellkitObject GetInstanceMember(SpellkitObject self, HashString nameStr, ExecutionContext ctx)
    {
        var interop = (SpellkitInterop)self;
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

    private SpellkitFunction? GetInteropFunction(SpellkitInterop self, string name, ExecutionContext _)
    {
        var typeObject = self.Object is Type;
        if (typeObject && name == "new")
        {
            return new SpellkitForeignConstructor(CreateNew);
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

        return new SpellkitInteropFunction(name, type, methods, auto);
    }
    #endregion

    internal static SpellkitObject CreateInteropObject(ExecutionContext ctx, string typeName)
    {
        var typeInfo = Type.GetType(typeName, throwOnError: false);

        if (typeInfo is null)
        {
            return ctx.InvalidValue(typeName);
        }

        return new SpellkitInterop(typeInfo);
    }

    internal static SpellkitObject GetSystemType(ExecutionContext ctx, SpellkitObject typeName)
    {
        if (typeName is SpellkitInterop obj)
        {
            return new SpellkitInterop(BCL.Type, obj.Type);
        }
        else if (typeName.TypeId is not (SpellkitTypeCodes.String or SpellkitTypeCodes.Char))
        {
            throw new SpellkitCodeException(SpellkitError.InvalidType, typeName);
        }

        var str = typeName.ToString();
        var key = nameof(SpellkitInteropTypeInfo) + "_x235_" + typeName.ToString();

        if (!ctx.TryGetContextVariable(key, out var ret))
        {
            var typeInfo = Type.GetType(str, throwOnError: false);

            if (typeInfo is null)
            {
                return ctx.InvalidValue(typeName);
            }

            ret = new SpellkitInterop(BCL.Type, typeInfo);
            ctx.SetContextVariable(key, ret);
        }

        return (SpellkitObject)ret!;
    }

    internal static SpellkitObject Wrap(SpellkitObject value) => new SpellkitInterop(value.GetType(), value);

    internal static SpellkitObject ConvertTo(ExecutionContext ctx, SpellkitInterop type, SpellkitObject value)
    {
        if (type.Object is not Type typ)
        {
            throw new SpellkitCodeException(SpellkitError.InvalidType, type);
        }

        var ret = TypeConverter.ConvertTo(ctx, value, typ);

        if (ret is null)
        {
            return Nil;
        }

        return new SpellkitInterop(typ, ret);
    }

    internal static SpellkitObject ConvertFrom(SpellkitObject value)
    {
        if (value is not SpellkitInterop interop)
        {
            return value;
        }

        return TypeConverter.ConvertFrom(interop.Object) ?? value;
    }

    internal static SpellkitObject CreateArray(ExecutionContext _, SpellkitInterop type, int size)
    {
        if (type.Object is not Type t)
        {
            throw new SpellkitCodeException(SpellkitError.InvalidType, type);
        }

        var arr = Array.CreateInstance(t, size);
        return new SpellkitInterop(arr.GetType(), arr);
    }

    internal static SpellkitObject GetMethod(ExecutionContext ctx, SpellkitInterop type, string name, SpellkitObject[]? parameterTypes = null, int typeArguments = 0)
    {
        if (type.Object is not Type typ)
        {
            throw new SpellkitCodeException(SpellkitError.InvalidType, type);
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

                return new SpellkitInterop(typeof(MethodInfo), mi);
            }
        }

        return ctx.MethodNotFound(name, typ, parameterTypes);
    }

    internal static bool ParametersMatch(ParameterInfo[] parameters, SpellkitObject[] types)
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

    internal static SpellkitObject GetField(ExecutionContext _, SpellkitInterop type, string name)
    {
        if (type.Object is not Type typ)
        {
            throw new SpellkitCodeException(SpellkitError.InvalidType, type);
        }

        var ret = typ.GetField(name);
        return ret is not null ? new SpellkitInterop(typeof(FieldInfo), ret) : Nil;
    }

    internal static SpellkitObject Int32() => TypeInt32;

    internal static SpellkitObject Int64() => TypeInt64;

    internal static SpellkitObject UInt32() => TypeUInt32;

    internal static SpellkitObject UInt64() => TypeUInt64;

    internal static SpellkitObject Byte() => TypeByte;

    internal static SpellkitObject SByte() => TypeSByte;

    internal static SpellkitObject Char() => TypeChar;

    internal static SpellkitObject String() => TypeString;

    internal static SpellkitObject Boolean() => TypeBoolean;

    internal static SpellkitObject Double() => TypeDouble;

    internal static SpellkitObject Single() => TypeSingle;
    
    internal static SpellkitObject SystemArray() => TypeSystemArray;

    internal static SpellkitObject SystemType() => TypeSystemType;
}
