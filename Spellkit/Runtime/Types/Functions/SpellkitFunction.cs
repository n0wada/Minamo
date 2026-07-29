using Spellkit.Compiler;
using Spellkit.Debug;
using Spellkit.Parser;
using System.Runtime.CompilerServices;
using System.Text;
using Spellkit.Codegen;
using System.Collections.Generic;

namespace Spellkit.Runtime.Types;

public abstract class SpellkitFunction : SpellkitObject
{
    internal const string DefaultName = "<func>";
    internal static readonly SpellkitObject CallbackPending = new SpellkitFunctionCallbackMarker();
    internal protected SpellkitObject? Self;
    internal Par[] Parameters;
    internal protected int VarArgIndex;
    internal protected int Attr;

    public override string TypeName => nameof(SpellkitTypeCodes.Function);

    public abstract string FunctionName { get; }

    public abstract bool IsExternal { get; }

    internal bool Auto => (Attr & FunAttr.Auto) == FunAttr.Auto;

    protected SpellkitFunction(Par[] pars, int varArgIndex) : base(SpellkitTypeCodes.Function) =>
        (Parameters, VarArgIndex) = (pars, varArgIndex);

    public override object ToObject() => (Func<ExecutionContext, SpellkitObject[], SpellkitObject>)Call;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SpellkitObject PrepareFunction(ExecutionContext ctx, SpellkitObject self)
    {
        if (IsExternal)
        {
            return ((SpellkitUnaryFunction)this).CallUnary(ctx, self);
        }

        var func = BindToInstance(ctx, self);
        ctx.CallBackFunction = func;
        ctx.Error = CallbackPending;
        return Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SpellkitObject PrepareFunction(ExecutionContext ctx, SpellkitObject self, SpellkitObject arg)
    {
        if (IsExternal)
        {
            return ((SpellkitBinaryFunction)this).CallBinary(ctx, self, arg);
        }

        var func = BindToInstance(ctx, self);
        ctx.CallBackFunction = func;
        ctx.Error = CallbackPending;
        return Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SpellkitObject PrepareFunction(ExecutionContext ctx, SpellkitObject self, SpellkitObject arg1, SpellkitObject arg2)
    {
        if (IsExternal)
        {
            return ((SpellkitTernaryFunction)this).CallTernary(ctx, self, arg1, arg2);
        }

        var func = BindToInstance(ctx, self);
        ctx.CallBackFunction = func;
        ctx.Error = CallbackPending;
        return Nil;
    }

    internal abstract SpellkitFunction BindToInstance(ExecutionContext ctx, SpellkitObject arg);

    internal SpellkitObject TryInvokeProperty(ExecutionContext ctx, SpellkitObject arg) => BindOrRun(ctx, arg);

    protected virtual SpellkitObject BindOrRun(ExecutionContext ctx, SpellkitObject arg) => BindToInstance(ctx, arg);

    internal SpellkitObject FastCall(ExecutionContext ctx, SpellkitObject[] args) => CallWithMemoryLayout(ctx, args);

    protected abstract SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args);

    public SpellkitObject Call(ExecutionContext ctx)
    {
        var newArgs = PrepareMemoryLayout(ctx, Array.Empty<SpellkitObject>());

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return CallWithMemoryLayout(ctx, newArgs);
    }

    public SpellkitObject Call(ExecutionContext ctx, SpellkitObject arg)
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

    public SpellkitObject Call(ExecutionContext ctx, SpellkitObject arg1, SpellkitObject arg2)
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

    public SpellkitObject Call(ExecutionContext ctx, params SpellkitObject[] args)
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

    protected virtual SpellkitObject CallWithSingleArgument(ExecutionContext ctx, SpellkitObject arg) =>
        CallWithMemoryLayout(ctx, new[] { arg });

    protected virtual SpellkitObject CallWithTwoArguments(ExecutionContext ctx, SpellkitObject arg1, SpellkitObject arg2) =>
        CallWithMemoryLayout(ctx, new[] { arg1, arg2 });

    protected SpellkitObject[] PrepareMemoryLayout(ExecutionContext ctx, SpellkitObject arg)
    {
        if (VarArgIndex > -1)
        {
            return PrepareMemoryLayout(ctx, new[] { arg });
        }

        if (Parameters.Length < 1)
        {
            ctx.TooManyArguments(FunctionName, Parameters.Length, 1);
            return Array.Empty<SpellkitObject>();
        }

        var memorySize = GetMemoryCells(ctx);
        var newLocals = memorySize == 1 ? new[] { arg } : new SpellkitObject[memorySize];
        if (memorySize != 1)
        {
            newLocals[0] = arg;
            SpellkitMachine.FillDefaults(newLocals, this, ctx);
        }

        return newLocals;
    }

    protected SpellkitObject[] PrepareMemoryLayout(ExecutionContext ctx, SpellkitObject arg1, SpellkitObject arg2)
    {
        if (VarArgIndex > -1)
        {
            return PrepareMemoryLayout(ctx, new[] { arg1, arg2 });
        }

        if (Parameters.Length < 2)
        {
            ctx.TooManyArguments(FunctionName, Parameters.Length, 2);
            return Array.Empty<SpellkitObject>();
        }

        var memorySize = GetMemoryCells(ctx);
        var newLocals = memorySize == 2 ? new[] { arg1, arg2 } : new SpellkitObject[memorySize];
        if (memorySize != 2)
        {
            newLocals[0] = arg1;
            newLocals[1] = arg2;
            SpellkitMachine.FillDefaults(newLocals, this, ctx);
        }

        return newLocals;
    }

    protected SpellkitObject[] PrepareMemoryLayout(ExecutionContext ctx, SpellkitObject[] args)
    {
        if (args.Length > Parameters.Length)
        {
            ctx.TooManyArguments(FunctionName, Parameters.Length, args.Length);
            return args;
        }

        SpellkitObject[] newLocals;
        var needDefaults = false;
        var memorySize = GetMemoryCells(ctx);

        if (args.Length == memorySize)
        {
            newLocals = args;
        }
        else
        {
            needDefaults = true;
            newLocals = new SpellkitObject[memorySize];
            if (args.Length > 0)
            {
                Array.Copy(args, newLocals, args.Length);
            }
        }

        if (VarArgIndex > -1)
        {
            var o = newLocals[VarArgIndex];
            if (o.TypeId == SpellkitTypeCodes.Nil)
            {
                newLocals[VarArgIndex] = SpellkitTuple.Empty;
            }
            else if (o.TypeId == SpellkitTypeCodes.Array)
            {
                var arr = (SpellkitArray)o;
                arr.Compact();
                newLocals[VarArgIndex] = new SpellkitTuple(arr.UnsafeAccess(), arr.Count);
            }
            else if (o.TypeId != SpellkitTypeCodes.Tuple)
            {
                newLocals[VarArgIndex] = new SpellkitTuple(new[] { o });
            }
        }

        if (needDefaults)
        {
            SpellkitMachine.FillDefaults(newLocals, this, ctx);
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
                if (p.Value is SpellkitString)
                {
                    sb.Append(StringUtil.Escape(p.Value.ToString()!));
                }
                else if (p.Value is SpellkitChar)
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
    public static bool IsSameInstance(SpellkitFunction first, SpellkitFunction second) =>
        ReferenceEquals(first.Self, second.Self) || (first.Self is not null && first.Self.Equals(second.Self));

    internal abstract int GetMemoryCells(ExecutionContext ctx);

    internal abstract SpellkitObject[] CreateLocals(ExecutionContext ctx);

    protected abstract bool Equals(SpellkitFunction func);

    public sealed override bool Equals(SpellkitObject? other) => other is SpellkitFunction func && Equals(func);

    public override int GetHashCode() => HashCode.Combine(TypeId, FunctionName ?? DefaultName, Parameters, Self);

    private sealed class SpellkitFunctionCallbackMarker : SpellkitObject
    {
        public SpellkitFunctionCallbackMarker() : base(SpellkitTypeCodes.Nil) { }

        public override string TypeName => "<callback>";

        public override object ToObject() => this;

        public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

        public override int GetHashCode() => typeof(SpellkitFunctionCallbackMarker).GetHashCode();
    }
}

[SpellkitType]
internal sealed partial class SpellkitFunctionTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Function);

    public override int ReflectedTypeId => SpellkitTypeCodes.Function;

    public SpellkitFunctionTypeInfo() => AddMixins(SpellkitTypeCodes.Functor, SpellkitTypeCodes.Equatable);

    #region Operations
    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        left.TypeId == right.TypeId && ((SpellkitFunction)left).Equals((SpellkitFunction)right) ? True : False;

    protected override SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.String)
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

    internal override SpellkitObject GetInstanceMember(SpellkitObject self, HashString name, ExecutionContext ctx) =>
        name == "Call" ? self : base.GetInstanceMember(self, name, ctx);
    #endregion

    [SpellkitMethod(BuiltinMethodNames.Apply)]
    internal static SpellkitObject Apply(SpellkitFunction self, [VarArg]SpellkitTuple parameters)
    {
        var tv = parameters.UnsafeAccess();
        var fn = (SpellkitFunction)self.Clone();
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
                if (tv[j] is SpellkitLabel la && la.Label == p.Name)
                {
                    val = la.Value;
                }
            }

            pars[i] = new Par(p.Name, val, p.IsVarArg, p.TypeAnnotation);
        }

        fn.Parameters = pars;
        return fn;
    }

    [SpellkitMethod(BuiltinMethodNames.Compose)]
    internal static SpellkitObject Compose(SpellkitFunction self, SpellkitFunction other) => new CompositionContainer(self, other);

    [SpellkitProperty("Object")]
    internal static SpellkitObject GetObject(SpellkitFunction self) => self.Self ?? Nil;

    [SpellkitProperty("Name")]
    internal static string GetName(SpellkitFunction self) => self.FunctionName;

    [SpellkitProperty("Parameters")]
    internal static SpellkitObject GetParameters(SpellkitFunction self)
    {
        var arr = new SpellkitObject[self.Parameters.Length];

        for (var i = 0; i < self.Parameters.Length; i++)
        {
            var p = self.Parameters[i];
            arr[i] = new SpellkitTuple(
                    new SpellkitLabel[] {
                        new("name", new SpellkitString(p.Name)),
                        new("hasDefault", p.Value is not null ? True : False),
                        new("default", p.Value ?? Nil),
                        new("varArg", self.VarArgIndex == i ? True : False)
                    }
                );
        }

        return new SpellkitArray(arr);
    }

    [SpellkitStaticMethod("Compose")]
    internal static SpellkitObject StaticCompose(SpellkitFunction first, SpellkitFunction second) => new CompositionContainer(first, second);
}

internal class SpellkitNativeFunction : SpellkitFunction
{
    private readonly FunSym? sym;
    internal readonly FastList<SpellkitObject[]> Captures;
    internal SpellkitObject[]? Locals;
    internal Stack<CatchMark> CatchMarks = null!;
    internal int PreviousOffset;
    internal readonly int UnitId;
    internal readonly int FunctionId;

    public override string FunctionName => sym?.Name != null ? sym.Name : DefaultName;

    public override bool IsExternal => false;

    internal SpellkitNativeFunction(FunSym? sym, int unitId, int funcId, FastList<SpellkitObject[]> captures, int varArgIndex) :
        base(sym?.Parameters ?? Array.Empty<Par>(), varArgIndex)
    {
        this.sym = sym;
        UnitId = unitId;
        FunctionId = funcId;
        Captures = captures;
    }

    public static SpellkitNativeFunction Create(FunSym sym, int unitId, int funcId, FastList<SpellkitObject[]> captures, SpellkitObject[] locals, int varArgIndex = -1)
    {
        var vars = new FastList<SpellkitObject[]>(captures) { locals };
        return new(sym, unitId, funcId, vars, varArgIndex);
    }

    internal override SpellkitFunction BindToInstance(ExecutionContext ctx, SpellkitObject arg) =>
        new SpellkitNativeFunction(sym, UnitId, FunctionId, Captures, VarArgIndex)
        {
            Self = arg
        };

    protected override SpellkitObject BindOrRun(ExecutionContext ctx, SpellkitObject arg)
    {
        if (Auto)
        {
            try
            {
                var size = GetMemoryCells(ctx);
                var locals = size == 0 ? Array.Empty<SpellkitObject>() : new SpellkitObject[size];
                ctx.CallStack.Push(Caller.External);
                return SpellkitMachine.ExecuteWithData((SpellkitNativeFunction)BindToInstance(ctx, arg), locals, ctx);
            }
            catch (SpellkitCodeException ex)
            {
                ctx.Error = ex.Error;
                return Nil;
            }
        }

        return BindToInstance(ctx, arg);
    }

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] locals)
    {
        ctx.CallStack.Push(Caller.External);
        return SpellkitMachine.ExecuteWithData(this, locals, ctx);
    }

    internal override int GetMemoryCells(ExecutionContext ctx) => ctx.RuntimeContext.Layouts[UnitId][FunctionId].Size;

    internal override SpellkitObject[] CreateLocals(ExecutionContext ctx)
    {
        var size = ctx.RuntimeContext.Layouts[UnitId][FunctionId].Size;
        return size == 0 ? Array.Empty<SpellkitObject>() : new SpellkitObject[size];
    }

    protected override bool Equals(SpellkitFunction func) =>
           func is SpellkitNativeFunction m && m.UnitId == UnitId && m.FunctionId == FunctionId
        && IsSameInstance(this, func);
}

public sealed class SpellkitExternalFunction : SpellkitForeignFunction
{
    private readonly Func<ExecutionContext, SpellkitObject?, SpellkitObject[], SpellkitObject> func;

    public SpellkitExternalFunction(string name, bool isPropertyGetter, Func<ExecutionContext, SpellkitObject?, SpellkitObject[], SpellkitObject> func, params Par[] pars)
        : base(name, pars)
    {
        this.func = func;

        if (isPropertyGetter)
        {
            Attr |= FunAttr.Auto;
        }
    }

    public override SpellkitObject Clone() => new SpellkitExternalFunction(FunctionName, (Attr & FunAttr.Auto) == FunAttr.Auto, func, Parameters);

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args) =>
        func(ctx, Self, args);

    protected override SpellkitObject BindOrRun(ExecutionContext ctx, SpellkitObject arg)
    {
        if (!Auto)
        {
            return BindToInstance(ctx, arg);
        }

        return func(ctx, arg, Array.Empty<SpellkitObject>());
    }

    public override object ToObject() => func;

    protected override bool Equals(SpellkitFunction func) =>
           FunctionName == func.FunctionName
        && func is SpellkitExternalFunction fn && fn.func.Equals(func)
        && IsSameInstance(this, func);
}

public abstract class SpellkitForeignFunction : SpellkitFunction
{
    public override string FunctionName { get; }

    public override bool IsExternal => true;

    protected SpellkitForeignFunction(string? name, Par[] pars, int varArgIndex)
        : base(pars, varArgIndex) => FunctionName = name ?? DefaultName;

    protected SpellkitForeignFunction(string? name, Par[] pars) : this(name, pars, GetVarArgIndex(pars)) { }

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

    internal override SpellkitFunction BindToInstance(ExecutionContext ctx, SpellkitObject arg)
    {
        var clone = Clone(ctx);
        clone.Self = arg;
        return clone;
    }

    protected virtual SpellkitFunction Clone(ExecutionContext ctx) => (SpellkitForeignFunction)MemberwiseClone();

    internal override SpellkitObject[] CreateLocals(ExecutionContext ctx) =>
        Parameters.Length == 0 ? Array.Empty<SpellkitObject>() : new SpellkitObject[Parameters.Length];

    internal sealed override int GetMemoryCells(ExecutionContext ctx) => Parameters.Length;
}
