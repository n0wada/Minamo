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
    internal SpkFunction? CallBackFunction { get; set; }

    internal SpkObject InvokeCallBackFunction()
    {
        var fn = CallBackFunction!;
        CallBackFunction = null;
        Error = null;
        return fn.Call(this);
    }

    internal SpkObject InvokeCallBackFunction(SpkObject arg)
    {
        var fn = CallBackFunction!;
        CallBackFunction = null;
        Error = null;
        return fn.Call(this, arg);
    }

    internal SpkObject InvokeCallBackFunction(SpkObject arg1, SpkObject arg2)
    {
        var fn = CallBackFunction!;
        CallBackFunction = null;
        Error = null;
        return fn.Call(this, arg1, arg2);
    }

    internal SpkObject InvokeCallBackFunction(params SpkObject[] args)
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

    private SpkObject? _error;
    public SpkObject? Error
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

    public SpkObject? PopError()
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
            throw new SpkCodeException(err);
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
        public SpkObject[] Locals = null!;
        public SpkObject[]? VarArgs;
        public int VarArgsSize;
        public int VarArgsIndex;
    }

    internal ArgContainer PushArguments(SpkObject[] locals, int varArgsIndex, SpkObject[]? varArgs = null)
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

public enum SpkExecutionLimitKind
{
    Instructions,
    Time,
    HostCommands,
    Signals,
    CallDepth
}

public sealed class SpkExecutionLimitException : SpkRuntimeException
{
    public SpkExecutionLimitException(SpkExecutionLimitKind kind, string message)
        : base(message) => Kind = kind;

    public SpkExecutionLimitKind Kind { get; }
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
            throw new SpkExecutionLimitException(
                SpkExecutionLimitKind.Instructions,
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
            throw new SpkExecutionLimitException(
                SpkExecutionLimitKind.HostCommands,
                $"Host command limit of {maxHostCommands.Value} was exceeded.");
        }

        HostCommands++;
        Checkpoint();
    }

    public void OnSignal()
    {
        if (maxSignals is not null && Signals >= maxSignals.Value)
        {
            throw new SpkExecutionLimitException(
                SpkExecutionLimitKind.Signals,
                $"Signal delivery limit of {maxSignals.Value} was exceeded.");
        }

        Signals++;
        Checkpoint();
    }

    public void CheckCallDepth(int depth)
    {
        if (MaxCallDepth is not null && depth > MaxCallDepth.Value)
        {
            throw new SpkExecutionLimitException(
                SpkExecutionLimitKind.CallDepth,
                $"Call depth limit of {MaxCallDepth.Value} was exceeded.");
        }
    }

    public void Checkpoint()
    {
        if (maxTime is { } timeLimit
            && (timeoutSource?.IsCancellationRequested == true
                || timeProvider.GetElapsedTime(started) > timeLimit))
        {
            throw new SpkExecutionLimitException(
                SpkExecutionLimitKind.Time,
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

internal sealed class ExecutionResult
{
    public long Ticks { get; }

    public SpkObject? Value { get; }

    public ExecutionContext Context { get; }

    public TerminationReason Reason { get; }

    internal SpkMachine.VmContinuation? Continuation { get; }

    private ExecutionResult(
        long ticks,
        SpkObject? value,
        ExecutionContext ctx,
        TerminationReason reason,
        SpkMachine.VmContinuation? continuation = null) =>
        (Ticks, Value, Context, Reason, Continuation) = (ticks, value, ctx, reason, continuation);

    internal static ExecutionResult Fetch(long ticks, SpkObject? value, ExecutionContext ctx) =>
        new(ticks, value, ctx, TerminationReason.Complete);

    internal static ExecutionResult Abort(long ticks, ExecutionContext ctx) =>
        new(ticks, null, ctx, TerminationReason.Abort);

    internal static ExecutionResult Suspend(
        ExecutionContext ctx,
        SpkMachine.VmContinuation continuation) =>
        new(0, null, ctx, TerminationReason.Suspended, continuation);
}
