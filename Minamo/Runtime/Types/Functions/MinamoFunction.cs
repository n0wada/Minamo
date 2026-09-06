using Minamo.Compiler;
using Minamo.Debug;
using Minamo.Parser;
using System.Runtime.CompilerServices;
using System.Text;
using Minamo.Codegen;
using System.Collections.Generic;

namespace Minamo.Runtime.Types;

public abstract class MinamoFunction : MinamoObject
{
    internal const string DefaultName = "<func>";
    internal static readonly MinamoObject CallbackPending = new MinamoFunctionCallbackMarker();
    internal protected MinamoObject? Self;
    internal Par[] Parameters;
    internal protected int VarArgIndex;
    internal protected int Attr;

    public override string TypeName => nameof(MinamoTypeCodes.Function);

    public abstract string FunctionName { get; }

    public abstract bool IsExternal { get; }

    internal bool Auto => (Attr & FunAttr.Auto) == FunAttr.Auto;

    protected MinamoFunction(Par[] pars, int varArgIndex) : base(MinamoTypeCodes.Function) =>
        (Parameters, VarArgIndex) = (pars, varArgIndex);

    public override object ToObject() => (Func<ExecutionContext, MinamoObject[], MinamoObject>)Call;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MinamoObject PrepareFunction(ExecutionContext ctx, MinamoObject self)
    {
        if (IsExternal)
        {
            return ((MinamoUnaryFunction)this).CallUnary(ctx, self);
        }

        var func = BindToInstance(ctx, self);
        ctx.CallBackFunction = func;
        ctx.Error = CallbackPending;
        return Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MinamoObject PrepareFunction(ExecutionContext ctx, MinamoObject self, MinamoObject arg)
    {
        if (IsExternal)
        {
            return ((MinamoBinaryFunction)this).CallBinary(ctx, self, arg);
        }

        var func = BindToInstance(ctx, self);
        ctx.CallBackFunction = func;
        ctx.Error = CallbackPending;
        return Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MinamoObject PrepareFunction(ExecutionContext ctx, MinamoObject self, MinamoObject arg1, MinamoObject arg2)
    {
        if (IsExternal)
        {
            return ((MinamoTernaryFunction)this).CallTernary(ctx, self, arg1, arg2);
        }

        var func = BindToInstance(ctx, self);
        ctx.CallBackFunction = func;
        ctx.Error = CallbackPending;
        return Nil;
    }

    internal abstract MinamoFunction BindToInstance(ExecutionContext ctx, MinamoObject arg);

    internal MinamoObject TryInvokeProperty(ExecutionContext ctx, MinamoObject arg) => BindOrRun(ctx, arg);

    protected virtual MinamoObject BindOrRun(ExecutionContext ctx, MinamoObject arg) => BindToInstance(ctx, arg);

    internal MinamoObject FastCall(ExecutionContext ctx, MinamoObject[] args) => CallWithMemoryLayout(ctx, args);

    protected abstract MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args);

    public MinamoObject Call(ExecutionContext ctx)
    {
        var newArgs = PrepareMemoryLayout(ctx, Array.Empty<MinamoObject>());

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return CallWithMemoryLayout(ctx, newArgs);
    }

    public MinamoObject Call(ExecutionContext ctx, MinamoObject arg)
    {
        if (CanCallWithSingleArgumentDirectly)
        {
            return CallWithSingleArgument(ctx, arg);
        }

        var newArgs = PrepareMemoryLayout(ctx, arg);

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return CallWithMemoryLayout(ctx, newArgs);
    }

    public MinamoObject Call(ExecutionContext ctx, MinamoObject arg1, MinamoObject arg2)
    {
        if (CanCallWithTwoArgumentsDirectly)
        {
            return CallWithTwoArguments(ctx, arg1, arg2);
        }

        var newArgs = PrepareMemoryLayout(ctx, arg1, arg2);

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return CallWithMemoryLayout(ctx, newArgs);
    }

    public MinamoObject Call(ExecutionContext ctx, params MinamoObject[] args)
    {
        var newArgs = PrepareMemoryLayout(ctx, args);

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return CallWithMemoryLayout(ctx, newArgs);
    }

    protected virtual bool CanCallWithSingleArgumentDirectly => false;

    protected virtual bool CanCallWithTwoArgumentsDirectly => false;

    protected virtual MinamoObject CallWithSingleArgument(ExecutionContext ctx, MinamoObject arg) =>
        CallWithMemoryLayout(ctx, new[] { arg });

    protected virtual MinamoObject CallWithTwoArguments(ExecutionContext ctx, MinamoObject arg1, MinamoObject arg2) =>
        CallWithMemoryLayout(ctx, new[] { arg1, arg2 });

    protected MinamoObject[] PrepareMemoryLayout(ExecutionContext ctx, MinamoObject arg)
    {
        if (VarArgIndex > -1)
        {
            return PrepareMemoryLayout(ctx, new[] { arg });
        }

        if (Parameters.Length < 1)
        {
            ctx.TooManyArguments(FunctionName, Parameters.Length, 1);
            return Array.Empty<MinamoObject>();
        }

        var memorySize = GetMemoryCells(ctx);
        var newLocals = memorySize == 1 ? new[] { arg } : new MinamoObject[memorySize];
        if (memorySize != 1)
        {
            newLocals[0] = arg;
            MinamoMachine.FillDefaults(newLocals, this, ctx);
        }

        return newLocals;
    }

    protected MinamoObject[] PrepareMemoryLayout(ExecutionContext ctx, MinamoObject arg1, MinamoObject arg2)
    {
        if (VarArgIndex > -1)
        {
            return PrepareMemoryLayout(ctx, new[] { arg1, arg2 });
        }

        if (Parameters.Length < 2)
        {
            ctx.TooManyArguments(FunctionName, Parameters.Length, 2);
            return Array.Empty<MinamoObject>();
        }

        var memorySize = GetMemoryCells(ctx);
        var newLocals = memorySize == 2 ? new[] { arg1, arg2 } : new MinamoObject[memorySize];
        if (memorySize != 2)
        {
            newLocals[0] = arg1;
            newLocals[1] = arg2;
            MinamoMachine.FillDefaults(newLocals, this, ctx);
        }

        return newLocals;
    }

    protected MinamoObject[] PrepareMemoryLayout(ExecutionContext ctx, MinamoObject[] args)
    {
        if (args.Length > Parameters.Length)
        {
            ctx.TooManyArguments(FunctionName, Parameters.Length, args.Length);
            return args;
        }

        MinamoObject[] newLocals;
        var needDefaults = false;
        var memorySize = GetMemoryCells(ctx);

        if (args.Length == memorySize)
        {
            newLocals = args;
        }
        else
        {
            needDefaults = true;
            newLocals = new MinamoObject[memorySize];
            if (args.Length > 0)
            {
                Array.Copy(args, newLocals, args.Length);
            }
        }

        if (VarArgIndex > -1)
        {
            var o = newLocals[VarArgIndex];
            if (o.TypeId == MinamoTypeCodes.Nil)
            {
                newLocals[VarArgIndex] = MinamoTuple.Empty;
            }
            else if (o.TypeId == MinamoTypeCodes.Array)
            {
                var arr = (MinamoArray)o;
                arr.Compact();
                newLocals[VarArgIndex] = new MinamoTuple(arr.UnsafeAccess(), arr.Count);
            }
            else if (o.TypeId != MinamoTypeCodes.Tuple)
            {
                newLocals[VarArgIndex] = new MinamoTuple(new[] { o });
            }
        }

        if (needDefaults)
        {
            MinamoMachine.FillDefaults(newLocals, this, ctx);
        }

        return newLocals;
    }

    internal int GetParameterIndex(string name)
    {
        for (var i = 0; i < Parameters.Length; i++)
        {
            if (Parameters[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        if (FunctionName is null)
        {
            sb.Append(DefaultName);
        }
        else
        {
            sb.Append(FunctionName);
        }

        sb.Append('(');
        var c = 0;

        foreach (var p in Parameters)
        {
            if (c != 0)
            {
                sb.Append(", ");
            }

            sb.Append(p.Name);

            if (p.IsVarArg)
            {
                sb.Append("...");
            }

            if (p.Value is not null)
            {
                sb.Append(" = ");
                if (p.Value is MinamoString)
                {
                    sb.Append(StringUtil.Escape(p.Value.ToString()!));
                }
                else if (p.Value is MinamoChar)
                {
                    sb.Append(StringUtil.Escape(p.Value.ToString()!, "'"));
                }
                else
                {
                    sb.Append(p.Value.ToString());
                }
            }

            c++;
        }

        sb.Append(')');
        var ret = sb.ToString();
        return ret;
    }

    //Checks if two functions are members of the same instance
    public static bool IsSameInstance(MinamoFunction first, MinamoFunction second) =>
        ReferenceEquals(first.Self, second.Self) || (first.Self is not null && first.Self.Equals(second.Self));

    internal abstract int GetMemoryCells(ExecutionContext ctx);

    internal abstract MinamoObject[] CreateLocals(ExecutionContext ctx);

    protected abstract bool Equals(MinamoFunction func);

    public sealed override bool Equals(MinamoObject? other) => other is MinamoFunction func && Equals(func);

    public override int GetHashCode() => HashCode.Combine(TypeId, FunctionName ?? DefaultName, Parameters, Self);

    private sealed class MinamoFunctionCallbackMarker : MinamoObject
    {
        public MinamoFunctionCallbackMarker() : base(MinamoTypeCodes.Nil) { }

        public override string TypeName => "<callback>";

        public override object ToObject() => this;

        public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

        public override int GetHashCode() => typeof(MinamoFunctionCallbackMarker).GetHashCode();
    }
}

[MinamoType]
internal sealed partial class MinamoFunctionTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Function);

    public override int ReflectedTypeId => MinamoTypeCodes.Function;

    public MinamoFunctionTypeInfo() => AddMixins(MinamoTypeCodes.Functor, MinamoTypeCodes.Equatable);

    #region Operations
    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        left.TypeId == right.TypeId && ((MinamoFunction)left).Equals((MinamoFunction)right) ? True : False;

    protected override MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.String)
        {
            return base.AddOp(ctx, left, right);
        }

        var f1 = left.ToFunction(ctx);
        if (ctx.HasErrors)
        {
            return Nil;
        }

        var f2 = right.ToFunction(ctx);
        if (ctx.HasErrors)
        {
            return Nil;
        }

        return new CompositionContainer(f1!, f2!);
    }

    internal override MinamoObject GetInstanceMember(MinamoObject self, HashString name, ExecutionContext ctx) =>
        name == "Call" ? self : base.GetInstanceMember(self, name, ctx);
    #endregion

    [MinamoMethod(BuiltinMethodNames.Apply)]
    internal static MinamoObject Apply(MinamoFunction self, [VarArg]MinamoTuple parameters)
    {
        var tv = parameters.UnsafeAccess();
        var fn = (MinamoFunction)self.Clone();
        var pars = new Par[fn.Parameters.Length];

        for (var i = 0; i < fn.Parameters.Length; i++)
        {
            var p = fn.Parameters[i];

            if (p.IsVarArg)
            {
                continue;
            }

            var val = p.Value;

            for (var j = 0; j < parameters.Count; j++)
            {
                if (tv[j] is MinamoLabel la && la.Label == p.Name)
                {
                    val = la.Value;
                }
            }

            pars[i] = new Par(p.Name, val, p.IsVarArg, p.TypeAnnotation);
        }

        fn.Parameters = pars;
        return fn;
    }

    [MinamoMethod(BuiltinMethodNames.Compose)]
    internal static MinamoObject Compose(MinamoFunction self, MinamoFunction other) => new CompositionContainer(self, other);

    [MinamoProperty("Object")]
    internal static MinamoObject GetObject(MinamoFunction self) => self.Self ?? Nil;

    [MinamoProperty("Name")]
    internal static string GetName(MinamoFunction self) => self.FunctionName;

    [MinamoProperty("Parameters")]
    internal static MinamoObject GetParameters(MinamoFunction self)
    {
        var arr = new MinamoObject[self.Parameters.Length];

        for (var i = 0; i < self.Parameters.Length; i++)
        {
            var p = self.Parameters[i];
            arr[i] = new MinamoTuple(
                    new MinamoLabel[] {
                        new("name", new MinamoString(p.Name)),
                        new("hasDefault", p.Value is not null ? True : False),
                        new("default", p.Value ?? Nil),
                        new("varArg", self.VarArgIndex == i ? True : False)
                    }
                );
        }

        return new MinamoArray(arr);
    }

    [MinamoStaticMethod("Compose")]
    internal static MinamoObject StaticCompose(MinamoFunction first, MinamoFunction second) => new CompositionContainer(first, second);
}

internal class MinamoNativeFunction : MinamoFunction
{
    private readonly FunSym? sym;
    internal readonly FastList<MinamoObject[]> Captures;
    internal MinamoObject[]? Locals;
    internal Stack<CatchMark> CatchMarks = null!;
    internal int PreviousOffset;
    internal readonly int UnitId;
    internal readonly int FunctionId;

    public override string FunctionName => sym?.Name != null ? sym.Name : DefaultName;

    public override bool IsExternal => false;

    internal MinamoNativeFunction(FunSym? sym, int unitId, int funcId, FastList<MinamoObject[]> captures, int varArgIndex) :
        base(sym?.Parameters ?? Array.Empty<Par>(), varArgIndex)
    {
        this.sym = sym;
        UnitId = unitId;
        FunctionId = funcId;
        Captures = captures;
    }

    public static MinamoNativeFunction Create(FunSym sym, int unitId, int funcId, FastList<MinamoObject[]> captures, MinamoObject[] locals, int varArgIndex = -1)
    {
        var vars = new FastList<MinamoObject[]>(captures) { locals };
        return new(sym, unitId, funcId, vars, varArgIndex);
    }

    internal override MinamoFunction BindToInstance(ExecutionContext ctx, MinamoObject arg) =>
        new MinamoNativeFunction(sym, UnitId, FunctionId, Captures, VarArgIndex)
        {
            Self = arg
        };

    protected override MinamoObject BindOrRun(ExecutionContext ctx, MinamoObject arg)
    {
        if (Auto)
        {
            try
            {
                var size = GetMemoryCells(ctx);
                var locals = size == 0 ? Array.Empty<MinamoObject>() : new MinamoObject[size];
                ctx.CallStack.Push(Caller.External);
                return MinamoMachine.ExecuteWithData((MinamoNativeFunction)BindToInstance(ctx, arg), locals, ctx);
            }
            catch (MinamoCodeException ex)
            {
                ctx.Error = ex.Error;
                return Nil;
            }
        }

        return BindToInstance(ctx, arg);
    }

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] locals)
    {
        ctx.CallStack.Push(Caller.External);
        return MinamoMachine.ExecuteWithData(this, locals, ctx);
    }

    internal override int GetMemoryCells(ExecutionContext ctx) => ctx.RuntimeContext.Layouts[UnitId][FunctionId].Size;

    internal override MinamoObject[] CreateLocals(ExecutionContext ctx)
    {
        var size = ctx.RuntimeContext.Layouts[UnitId][FunctionId].Size;
        return size == 0 ? Array.Empty<MinamoObject>() : new MinamoObject[size];
    }

    protected override bool Equals(MinamoFunction func) =>
           func is MinamoNativeFunction m && m.UnitId == UnitId && m.FunctionId == FunctionId
        && IsSameInstance(this, func);
}

public sealed class MinamoExternalFunction : MinamoForeignFunction
{
    private readonly Func<ExecutionContext, MinamoObject?, MinamoObject[], MinamoObject> func;

    public MinamoExternalFunction(string name, bool isPropertyGetter, Func<ExecutionContext, MinamoObject?, MinamoObject[], MinamoObject> func, params Par[] pars)
        : base(name, pars)
    {
        this.func = func;

        if (isPropertyGetter)
        {
            Attr |= FunAttr.Auto;
        }
    }

    public override MinamoObject Clone() => new MinamoExternalFunction(FunctionName, (Attr & FunAttr.Auto) == FunAttr.Auto, func, Parameters);

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) =>
        func(ctx, Self, args);

    protected override MinamoObject BindOrRun(ExecutionContext ctx, MinamoObject arg)
    {
        if (!Auto)
        {
            return BindToInstance(ctx, arg);
        }

        return func(ctx, arg, Array.Empty<MinamoObject>());
    }

    public override object ToObject() => func;

    protected override bool Equals(MinamoFunction func) =>
           FunctionName == func.FunctionName
        && func is MinamoExternalFunction fn && fn.func.Equals(func)
        && IsSameInstance(this, func);
}

public abstract class MinamoForeignFunction : MinamoFunction
{
    public override string FunctionName { get; }

    public override bool IsExternal => true;

    protected MinamoForeignFunction(string? name, Par[] pars, int varArgIndex)
        : base(pars, varArgIndex) => FunctionName = name ?? DefaultName;

    protected MinamoForeignFunction(string? name, Par[] pars) : this(name, pars, GetVarArgIndex(pars)) { }

    private static int GetVarArgIndex(Par[] pars)
    {
        for (var i = 0; i < pars.Length; i++)
        {
            if (pars[i].IsVarArg)
            {
                return i;
            }
        }

        return -1;
    }

    internal override MinamoFunction BindToInstance(ExecutionContext ctx, MinamoObject arg)
    {
        var clone = Clone(ctx);
        clone.Self = arg;
        return clone;
    }

    protected virtual MinamoFunction Clone(ExecutionContext ctx) => (MinamoForeignFunction)MemberwiseClone();

    internal override MinamoObject[] CreateLocals(ExecutionContext ctx) =>
        Parameters.Length == 0 ? Array.Empty<MinamoObject>() : new MinamoObject[Parameters.Length];

    internal sealed override int GetMemoryCells(ExecutionContext ctx) => Parameters.Length;
}
