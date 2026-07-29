using Spellkit.Debug;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Spellkit.Compiler;
using System.Diagnostics;
using System.Threading;

namespace Spellkit.Runtime;

public sealed class ExecutionContext
{
    internal int CallCnt; //Call counter

    internal ExecutionControl? Control { get; set; }

    internal int UnitId { get; set; }

    internal int CallerUnitId { get; set; }

    internal CallStack CallStack { get; }

    public RuntimeContext RuntimeContext { get; }

    internal ExecutionContext(CallStack callStack, RuntimeContext rtx) =>
        (CallStack, CatchMarks, RuntimeContext) = (callStack, new(), rtx);

    internal ExecutionContext(RuntimeContext rtx) : this(new(), rtx) { }

    public ExecutionContext Clone() => new(CallStack, RuntimeContext) { Control = Control };

    #region CallBack
    internal SpellkitFunction? CallBackFunction { get; set; }

    internal SpellkitObject InvokeCallBackFunction()
    {
        var fn = CallBackFunction!;
        CallBackFunction = null;
        Error = null;
        return fn.Call(this);
    }

    internal SpellkitObject InvokeCallBackFunction(SpellkitObject arg)
    {
        var fn = CallBackFunction!;
        CallBackFunction = null;
        Error = null;
        return fn.Call(this, arg);
    }

    internal SpellkitObject InvokeCallBackFunction(SpellkitObject arg1, SpellkitObject arg2)
    {
        var fn = CallBackFunction!;
        CallBackFunction = null;
        Error = null;
        return fn.Call(this, arg1, arg2);
    }

    internal SpellkitObject InvokeCallBackFunction(params SpellkitObject[] args)
    {
        var fn = CallBackFunction!;
        CallBackFunction = null;
        Error = null;
        return fn.Call(this, args);
    }
    #endregion

    #region Critical sections
    internal SectionStack CatchMarks { get; }

    internal Stack<int>? Sections { get; set; }
    #endregion

    #region Errors
    public bool HasErrors => Error != null;

    private SpellkitObject? _error;
    public SpellkitObject? Error
    {
        get => _error;
        internal set
        {
            if (_error is null || value is null)
            {
                _error = value;
            }
        }
    }

    internal Stack<StackPoint>? ErrorDump { get; set; }

    internal CallStackTrace? Trace { get; set; }

    public SpellkitObject? PopError()
    {
        var err = Error;
        Error = null;
        return err;
    }

    public void ThrowIf()
    {
        if (Error is not null)
        {
            var err = Error;
            Error = null;
            throw new SpellkitCodeException(err);
        }
    }
    #endregion

    #region Context variables
    public void SetContextVariable(string key, object val)
    {
        lock (RuntimeContext.SyncRoot)
        {
            RuntimeContext.Variables[key] = val;
        }
    }

    public T? GetContextVariable<T>(string key)
    {
        if (!RuntimeContext.Variables.TryGetValue(key, out var val))
        {
            return default;
        }

        return (T)val;
    }

    public bool HasContextVariable(string key) => RuntimeContext.Variables.ContainsKey(key);

    public bool TryGetContextVariable(string key, out object? value) =>
        RuntimeContext.Variables.TryGetValue(key, out value);
    #endregion

    #region ArgContainer
    private int count;
    private readonly List<ArgContainer> containers = new(2);

    internal sealed class ArgContainer
    {
        public SpellkitObject[] Locals = null!;
        public SpellkitObject[]? VarArgs;
        public int VarArgsSize;
        public int VarArgsIndex;
    }

    internal ArgContainer PushArguments(SpellkitObject[] locals, int varArgsIndex, SpellkitObject[]? varArgs = null)
    {
        if (containers.Count <= count)
        {
            containers.Add(new());
        }

        var ret = containers[count++];
        ret.Locals = locals;
        ret.VarArgsIndex = varArgsIndex;
        ret.VarArgs = varArgs;
        ret.VarArgsSize = 0;
        return ret;
    }

    internal ArgContainer PopArguments() => containers[--count];

    internal ArgContainer PeekArguments() => containers[count - 1];
    #endregion
}

public enum SpellkitExecutionLimitKind
{
    Instructions,
    Time,
    HostCommands,
    Signals,
    CallDepth
}

public sealed class SpellkitExecutionLimitException : SpellkitRuntimeException
{
    public SpellkitExecutionLimitException(SpellkitExecutionLimitKind kind, string message)
        : base(message) => Kind = kind;

    public SpellkitExecutionLimitKind Kind { get; }
}

internal sealed class ExecutionControl : IDisposable
{
    private readonly long started;
    private readonly long? maxInstructions;
    private readonly TimeSpan? maxTime;
    private readonly int? maxHostCommands;
    private readonly int? maxSignals;
    private readonly TimeProvider timeProvider;
    private readonly CancellationToken cancellationToken;
    private readonly CancellationTokenSource? timeoutSource;
    private readonly CancellationTokenSource? linkedSource;

    public ExecutionControl(
        long? maxInstructions,
        TimeSpan? maxTime,
        int? maxHostCommands,
        int? maxSignals,
        int? maxCallDepth,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        this.timeProvider = timeProvider;
        started = timeProvider.GetTimestamp();
        this.maxInstructions = maxInstructions;
        this.maxTime = maxTime;
        this.maxHostCommands = maxHostCommands;
        this.maxSignals = maxSignals;
        if (maxTime is not null)
        {
            timeoutSource = new CancellationTokenSource(maxTime.Value, timeProvider);
            linkedSource = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token);
            this.cancellationToken = linkedSource.Token;
        }
        else
        {
            this.cancellationToken = cancellationToken;
        }

        MaxCallDepth = maxCallDepth;
    }

    public long Instructions { get; private set; }
    public int HostCommands { get; private set; }
    public int Signals { get; private set; }
    public int? MaxCallDepth { get; }
    public CancellationToken CancellationToken => cancellationToken;

    public void OnInstruction()
    {
        if (maxInstructions is not null && Instructions >= maxInstructions.Value)
        {
            throw new SpellkitExecutionLimitException(
                SpellkitExecutionLimitKind.Instructions,
                $"Instruction limit of {maxInstructions.Value} was exceeded.");
        }

        Instructions++;

        if ((Instructions & 255) == 0)
        {
            Checkpoint();
        }
    }

    public void OnHostCommand()
    {
        if (maxHostCommands is not null && HostCommands >= maxHostCommands.Value)
        {
            throw new SpellkitExecutionLimitException(
                SpellkitExecutionLimitKind.HostCommands,
                $"Host command limit of {maxHostCommands.Value} was exceeded.");
        }

        HostCommands++;
        Checkpoint();
    }

    public void OnSignal()
    {
        if (maxSignals is not null && Signals >= maxSignals.Value)
        {
            throw new SpellkitExecutionLimitException(
                SpellkitExecutionLimitKind.Signals,
                $"Signal delivery limit of {maxSignals.Value} was exceeded.");
        }

        Signals++;
        Checkpoint();
    }

    public void CheckCallDepth(int depth)
    {
        if (MaxCallDepth is not null && depth > MaxCallDepth.Value)
        {
            throw new SpellkitExecutionLimitException(
                SpellkitExecutionLimitKind.CallDepth,
                $"Call depth limit of {MaxCallDepth.Value} was exceeded.");
        }
    }

    public void Checkpoint()
    {
        if (maxTime is { } timeLimit
            && (timeoutSource?.IsCancellationRequested == true
                || timeProvider.GetElapsedTime(started) > timeLimit))
        {
            throw new SpellkitExecutionLimitException(
                SpellkitExecutionLimitKind.Time,
                $"Execution time limit of {timeLimit} was exceeded.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        linkedSource?.Dispose();
        timeoutSource?.Dispose();
    }
}

internal enum TerminationReason
{
    Complete = 0,

    Abort = 1,

    Exception = 2,

    Suspended = 3
}

internal sealed record VmSuspension(SelectInstance? Select);

internal sealed class ExecutionResult
{
    public long Ticks { get; }

    public SpellkitObject? Value { get; }

    public ExecutionContext Context { get; }

    public TerminationReason Reason { get; }

    internal SpellkitMachine.VmContinuation? Continuation { get; }

    internal VmSuspension? Suspension { get; }

    private ExecutionResult(
        long ticks,
        SpellkitObject? value,
        ExecutionContext ctx,
        TerminationReason reason,
        SpellkitMachine.VmContinuation? continuation = null,
        VmSuspension? suspension = null) =>
        (Ticks, Value, Context, Reason, Continuation, Suspension) =
        (ticks, value, ctx, reason, continuation, suspension);

    internal static ExecutionResult Fetch(long ticks, SpellkitObject? value, ExecutionContext ctx) =>
        new(ticks, value, ctx, TerminationReason.Complete);

    internal static ExecutionResult Abort(long ticks, ExecutionContext ctx) =>
        new(ticks, null, ctx, TerminationReason.Abort);

    internal static ExecutionResult Suspend(
        ExecutionContext ctx,
        SpellkitMachine.VmContinuation continuation,
        VmSuspension suspension) =>
        new(0, null, ctx, TerminationReason.Suspended, continuation, suspension);
}
