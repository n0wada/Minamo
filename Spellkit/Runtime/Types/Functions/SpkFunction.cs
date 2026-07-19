using Spellkit.Compiler;
using Spellkit.Debug;
using Spellkit.Parser;
using System.Runtime.CompilerServices;
using System.Text;
using Spellkit.Codegen;
using System.Collections.Generic;

namespace Spellkit.Runtime.Types;

public abstract class SpkFunction : SpkObject
{
    internal const string DefaultName = "<func>";
    internal static readonly SpkObject CallbackPending = new SpkFunctionCallbackMarker();
    internal protected SpkObject? Self;
    internal Par[] Parameters;
    internal protected int VarArgIndex;
    internal protected int Attr;

    public override string TypeName => nameof(Spk.Function);

    public abstract string FunctionName { get; }

    public abstract bool IsExternal { get; }

    internal bool Auto => (Attr & FunAttr.Auto) == FunAttr.Auto;

    protected SpkFunction(Par[] pars, int varArgIndex) : base(Spk.Function) =>
        (Parameters, VarArgIndex) = (pars, varArgIndex);

    public override object ToObject() => (Func<ExecutionContext, SpkObject[], SpkObject>)Call;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SpkObject PrepareFunction(ExecutionContext ctx, SpkObject self)
    {
        if (IsExternal)
        {
            return ((SpkUnaryFunction)this).CallUnary(ctx, self);
        }

        var func = BindToInstance(ctx, self);
        ctx.CallBackFunction = func;
        ctx.Error = CallbackPending;
        return Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SpkObject PrepareFunction(ExecutionContext ctx, SpkObject self, SpkObject arg)
    {
        if (IsExternal)
        {
            return ((SpkBinaryFunction)this).CallBinary(ctx, self, arg);
        }

        var func = BindToInstance(ctx, self);
        ctx.CallBackFunction = func;
        ctx.Error = CallbackPending;
        return Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SpkObject PrepareFunction(ExecutionContext ctx, SpkObject self, SpkObject arg1, SpkObject arg2)
    {
        if (IsExternal)
        {
            return ((SpkTernaryFunction)this).CallTernary(ctx, self, arg1, arg2);
        }

        var func = BindToInstance(ctx, self);
        ctx.CallBackFunction = func;
        ctx.Error = CallbackPending;
        return Nil;
    }

    internal abstract SpkFunction BindToInstance(ExecutionContext ctx, SpkObject arg);

    internal SpkObject TryInvokeProperty(ExecutionContext ctx, SpkObject arg) => BindOrRun(ctx, arg);

    protected virtual SpkObject BindOrRun(ExecutionContext ctx, SpkObject arg) => BindToInstance(ctx, arg);

    internal SpkObject FastCall(ExecutionContext ctx, SpkObject[] args) => CallWithMemoryLayout(ctx, args);

    protected abstract SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args);

    public SpkObject Call(ExecutionContext ctx)
    {
        var newArgs = PrepareMemoryLayout(ctx, Array.Empty<SpkObject>());

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return CallWithMemoryLayout(ctx, newArgs);
    }

    public SpkObject Call(ExecutionContext ctx, SpkObject arg)
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

    public SpkObject Call(ExecutionContext ctx, SpkObject arg1, SpkObject arg2)
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

    public SpkObject Call(ExecutionContext ctx, params SpkObject[] args)
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

    protected virtual SpkObject CallWithSingleArgument(ExecutionContext ctx, SpkObject arg) =>
        CallWithMemoryLayout(ctx, new[] { arg });

    protected virtual SpkObject CallWithTwoArguments(ExecutionContext ctx, SpkObject arg1, SpkObject arg2) =>
        CallWithMemoryLayout(ctx, new[] { arg1, arg2 });

    protected SpkObject[] PrepareMemoryLayout(ExecutionContext ctx, SpkObject arg)
    {
        if (VarArgIndex > -1)
        {
            return PrepareMemoryLayout(ctx, new[] { arg });
        }

        if (Parameters.Length < 1)
        {
            ctx.TooManyArguments(FunctionName, Parameters.Length, 1);
            return Array.Empty<SpkObject>();
        }

        var memorySize = GetMemoryCells(ctx);
        var newLocals = memorySize == 1 ? new[] { arg } : new SpkObject[memorySize];
        if (memorySize != 1)
        {
            newLocals[0] = arg;
            SpkMachine.FillDefaults(newLocals, this, ctx);
        }

        return newLocals;
    }

    protected SpkObject[] PrepareMemoryLayout(ExecutionContext ctx, SpkObject arg1, SpkObject arg2)
    {
        if (VarArgIndex > -1)
        {
            return PrepareMemoryLayout(ctx, new[] { arg1, arg2 });
        }

        if (Parameters.Length < 2)
        {
            ctx.TooManyArguments(FunctionName, Parameters.Length, 2);
            return Array.Empty<SpkObject>();
        }

        var memorySize = GetMemoryCells(ctx);
        var newLocals = memorySize == 2 ? new[] { arg1, arg2 } : new SpkObject[memorySize];
        if (memorySize != 2)
        {
            newLocals[0] = arg1;
            newLocals[1] = arg2;
            SpkMachine.FillDefaults(newLocals, this, ctx);
        }

        return newLocals;
    }

    protected SpkObject[] PrepareMemoryLayout(ExecutionContext ctx, SpkObject[] args)
    {
        if (args.Length > Parameters.Length)
        {
            ctx.TooManyArguments(FunctionName, Parameters.Length, args.Length);
            return args;
        }

        SpkObject[] newLocals;
        var needDefaults = false;
        var memorySize = GetMemoryCells(ctx);

        if (args.Length == memorySize)
        {
            newLocals = args;
        }
        else
        {
            needDefaults = true;
            newLocals = new SpkObject[memorySize];
            if (args.Length > 0)
            {
                Array.Copy(args, newLocals, args.Length);
            }
        }

        if (VarArgIndex > -1)
        {
            var o = newLocals[VarArgIndex];
            if (o.TypeId == Spk.Nil)
            {
                newLocals[VarArgIndex] = SpkTuple.Empty;
            }
            else if (o.TypeId == Spk.Array)
            {
                var arr = (SpkArray)o;
                arr.Compact();
                newLocals[VarArgIndex] = new SpkTuple(arr.UnsafeAccess(), arr.Count);
            }
            else if (o.TypeId != Spk.Tuple)
            {
                newLocals[VarArgIndex] = new SpkTuple(new[] { o });
            }
        }

        if (needDefaults)
        {
            SpkMachine.FillDefaults(newLocals, this, ctx);
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
                if (p.Value is SpkString)
                {
                    sb.Append(StringUtil.Escape(p.Value.ToString()!));
                }
                else if (p.Value is SpkChar)
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
    public static bool IsSameInstance(SpkFunction first, SpkFunction second) =>
        ReferenceEquals(first.Self, second.Self) || (first.Self is not null && first.Self.Equals(second.Self));

    internal abstract int GetMemoryCells(ExecutionContext ctx);

    internal abstract SpkObject[] CreateLocals(ExecutionContext ctx);

    protected abstract bool Equals(SpkFunction func);

    public sealed override bool Equals(SpkObject? other) => other is SpkFunction func && Equals(func);

    public override int GetHashCode() => HashCode.Combine(TypeId, FunctionName ?? DefaultName, Parameters, Self);

    private sealed class SpkFunctionCallbackMarker : SpkObject
    {
        public SpkFunctionCallbackMarker() : base(Spk.Nil) { }

        public override string TypeName => "<callback>";

        public override object ToObject() => this;

        public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);

        public override int GetHashCode() => typeof(SpkFunctionCallbackMarker).GetHashCode();
    }
}

[SpkType]
internal sealed partial class SpkFunctionTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Function);

    public override int ReflectedTypeId => Spk.Function;

    public SpkFunctionTypeInfo() => AddMixins(Spk.Functor, Spk.Equatable);

    #region Operations
    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        left.TypeId == right.TypeId && ((SpkFunction)left).Equals((SpkFunction)right) ? True : False;

    protected override SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.String)
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

    internal override SpkObject GetInstanceMember(SpkObject self, HashString name, ExecutionContext ctx) =>
        name == "Call" ? self : base.GetInstanceMember(self, name, ctx);
    #endregion

    [SpkMethod(BuiltinMethodNames.Apply)]
    internal static SpkObject Apply(SpkFunction self, [VarArg]SpkTuple parameters)
    {
        var tv = parameters.UnsafeAccess();
        var fn = (SpkFunction)self.Clone();
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
                if (tv[j] is SpkLabel la && la.Label == p.Name)
                {
                    val = la.Value;
                }
            }

            pars[i] = new Par(p.Name, val, p.IsVarArg, p.TypeAnnotation);
        }

        fn.Parameters = pars;
        return fn;
    }

    [SpkMethod(BuiltinMethodNames.Compose)]
    internal static SpkObject Compose(SpkFunction self, SpkFunction other) => new CompositionContainer(self, other);

    [SpkProperty("Object")]
    internal static SpkObject GetObject(SpkFunction self) => self.Self ?? Nil;

    [SpkProperty("Name")]
    internal static string GetName(SpkFunction self) => self.FunctionName;

    [SpkProperty("Parameters")]
    internal static SpkObject GetParameters(SpkFunction self)
    {
        var arr = new SpkObject[self.Parameters.Length];

        for (var i = 0; i < self.Parameters.Length; i++)
        {
            var p = self.Parameters[i];
            arr[i] = new SpkTuple(
                    new SpkLabel[] {
                        new("name", new SpkString(p.Name)),
                        new("hasDefault", p.Value is not null ? True : False),
                        new("default", p.Value ?? Nil),
                        new("varArg", self.VarArgIndex == i ? True : False)
                    }
                );
        }

        return new SpkArray(arr);
    }

    [SpkStaticMethod("Compose")]
    internal static SpkObject StaticCompose(SpkFunction first, SpkFunction second) => new CompositionContainer(first, second);
}

internal class SpkNativeFunction : SpkFunction
{
    private readonly FunSym? sym;
    internal readonly FastList<SpkObject[]> Captures;
    internal SpkObject[]? Locals;
    internal Stack<CatchMark> CatchMarks = null!;
    internal int PreviousOffset;
    internal readonly int UnitId;
    internal readonly int FunctionId;

    public override string FunctionName => sym?.Name != null ? sym.Name : DefaultName;

    public override bool IsExternal => false;

    internal SpkNativeFunction(FunSym? sym, int unitId, int funcId, FastList<SpkObject[]> captures, int varArgIndex) :
        base(sym?.Parameters ?? Array.Empty<Par>(), varArgIndex)
    {
        this.sym = sym;
        UnitId = unitId;
        FunctionId = funcId;
        Captures = captures;
    }

    public static SpkNativeFunction Create(FunSym sym, int unitId, int funcId, FastList<SpkObject[]> captures, SpkObject[] locals, int varArgIndex = -1)
    {
        var vars = new FastList<SpkObject[]>(captures) { locals };
        return new(sym, unitId, funcId, vars, varArgIndex);
    }

    internal override SpkFunction BindToInstance(ExecutionContext ctx, SpkObject arg) =>
        new SpkNativeFunction(sym, UnitId, FunctionId, Captures, VarArgIndex)
        {
            Self = arg
        };

    protected override SpkObject BindOrRun(ExecutionContext ctx, SpkObject arg)
    {
        if (Auto)
        {
            try
            {
                var size = GetMemoryCells(ctx);
                var locals = size == 0 ? Array.Empty<SpkObject>() : new SpkObject[size];
                ctx.CallStack.Push(Caller.External);
                return SpkMachine.ExecuteWithData((SpkNativeFunction)BindToInstance(ctx, arg), locals, ctx);
            }
            catch (SpkCodeException ex)
            {
                ctx.Error = ex.Error;
                return Nil;
            }
        }

        return BindToInstance(ctx, arg);
    }

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] locals)
    {
        ctx.CallStack.Push(Caller.External);
        return SpkMachine.ExecuteWithData(this, locals, ctx);
    }

    internal override int GetMemoryCells(ExecutionContext ctx) => ctx.RuntimeContext.Layouts[UnitId][FunctionId].Size;

    internal override SpkObject[] CreateLocals(ExecutionContext ctx)
    {
        var size = ctx.RuntimeContext.Layouts[UnitId][FunctionId].Size;
        return size == 0 ? Array.Empty<SpkObject>() : new SpkObject[size];
    }

    protected override bool Equals(SpkFunction func) =>
           func is SpkNativeFunction m && m.UnitId == UnitId && m.FunctionId == FunctionId
        && IsSameInstance(this, func);
}

public sealed class SpkExternalFunction : SpkForeignFunction
{
    private readonly Func<ExecutionContext, SpkObject?, SpkObject[], SpkObject> func;

    public SpkExternalFunction(string name, bool isPropertyGetter, Func<ExecutionContext, SpkObject?, SpkObject[], SpkObject> func, params Par[] pars)
        : base(name, pars)
    {
        this.func = func;

        if (isPropertyGetter)
        {
            Attr |= FunAttr.Auto;
        }
    }

    public override SpkObject Clone() => new SpkExternalFunction(FunctionName, (Attr & FunAttr.Auto) == FunAttr.Auto, func, Parameters);

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args) =>
        func(ctx, Self, args);

    protected override SpkObject BindOrRun(ExecutionContext ctx, SpkObject arg)
    {
        if (!Auto)
        {
            return BindToInstance(ctx, arg);
        }

        return func(ctx, arg, Array.Empty<SpkObject>());
    }

    public override object ToObject() => func;

    protected override bool Equals(SpkFunction func) =>
           FunctionName == func.FunctionName
        && func is SpkExternalFunction fn && fn.func.Equals(func)
        && IsSameInstance(this, func);
}

public abstract class SpkForeignFunction : SpkFunction
{
    public override string FunctionName { get; }

    public override bool IsExternal => true;

    protected SpkForeignFunction(string? name, Par[] pars, int varArgIndex)
        : base(pars, varArgIndex) => FunctionName = name ?? DefaultName;

    protected SpkForeignFunction(string? name, Par[] pars) : this(name, pars, GetVarArgIndex(pars)) { }

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

    internal override SpkFunction BindToInstance(ExecutionContext ctx, SpkObject arg)
    {
        var clone = Clone(ctx);
        clone.Self = arg;
        return clone;
    }

    protected virtual SpkFunction Clone(ExecutionContext ctx) => (SpkForeignFunction)MemberwiseClone();

    internal override SpkObject[] CreateLocals(ExecutionContext ctx) =>
        Parameters.Length == 0 ? Array.Empty<SpkObject>() : new SpkObject[Parameters.Length];

    internal sealed override int GetMemoryCells(ExecutionContext ctx) => Parameters.Length;
}
