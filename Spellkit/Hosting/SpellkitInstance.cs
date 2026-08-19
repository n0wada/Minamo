using Spellkit.Compiler;
using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Parser.Model;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CancellationToken = System.Threading.CancellationToken;

namespace Spellkit.Hosting;

public sealed partial class SpellkitInstance : IDisposable
{
    private readonly System.Threading.Lock syncRoot = new();
    private readonly System.Threading.SemaphoreSlim operationGate = new(1, 1);
    private readonly System.Threading.AsyncLocal<bool> operationScope = new();
    private readonly FileLookup lookup;
    private readonly SpellkitProgram? program;
    private readonly SpellkitTuple? arguments;
    private SpellkitIncrementalLinker linker;
    private RuntimeContext? runtimeContext;
    private int submission;
    private bool disposed;
    private bool active;
    private SpellkitRunSession? suspendedRun;

    internal SpellkitInstance(
        FileLookup lookup,
        SpellkitHostEnvironment environment,
        SpellkitEnvironment spellkitEnvironment,
        SpellkitProgram? program = null,
        SpellkitTuple? arguments = null)
    {
        this.lookup = lookup;
        this.program = program;
        this.arguments = arguments;
        Environment = environment;
        SpellkitEnvironment = spellkitEnvironment;
        linker = new(lookup, arguments);
    }

    public object? HostContext => Environment.HostContext;

    public SpellkitHostEnvironment Environment { get; }

    public SpellkitEnvironment SpellkitEnvironment { get; }

    internal RuntimeContext? RuntimeContext => runtimeContext;

    public Task<SpellkitExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        if (program is null)
        {
            throw new InvalidOperationException(
                "This instance was not created from a compiled Spellkit program.");
        }

        return ExecuteCoreAsync(
            () => Result.Create(program.Composition),
            "ExecuteProgram",
            cancellationToken,
            touchesLinker: false);
    }

    public Task<SpellkitRunSession> StartAsync(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return StartCoreAsync(() => linker.Make(SourceBuffer.FromString(
            source,
            $"<host:{++submission}>")));
    }

    public Task<SpellkitExecutionResult> ExecuteAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ExecuteCoreAsync(
            () => linker.Make(SourceBuffer.FromString(
                source,
                $"<host:{++submission}>")),
            "Execute",
            cancellationToken);
    }

    internal Task<SpellkitExecutionResult> ExecuteAsync(
        SpellkitCodeModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ExecuteCoreAsync(
            () => linker.Make(model),
            "ExecuteModel",
            cancellationToken);
    }

    public Task<SpellkitExecutionResult> ExecuteFileAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var path = Path.GetFullPath(fileName);
        return ExecuteCoreAsync(
            () => linker.Make(SourceBuffer.FromString(File.ReadAllText(path), path)),
            "ExecuteFile",
            cancellationToken,
            SpellkitFailureKind.Input);
    }

    private async Task<SpellkitRunSession> StartCoreAsync(
        Func<Result<UnitComposition>> compile)
    {
        if (operationScope.Value)
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        operationScope.Value = true;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BeginOperation();
            var linkerTouched = false;
            try
            {
                linkerTouched = true;
                var made = await Task.Run(compile, CancellationToken.None).ConfigureAwait(false);
                if (!made.Success || made.Value is null)
                {
                    TryRollback();
                    return new SpellkitRunSession(this, new InvalidOperationException(
                        string.Join(System.Environment.NewLine, made.Messages)));
                }

                var context = CreateExecutionContext(made.Value, control: null);
                var result = await Task.Run(
                    () => SpellkitMachine.Execute(context),
                    CancellationToken.None).ConfigureAwait(false);
                result = await CompleteAwaitablesAsync(result).ConfigureAwait(false);
                linker.Commit();
                var run = new SpellkitRunSession(this, result);
                await run.AdvanceAsync(result).ConfigureAwait(false);
                if (!run.IsCompleted)
                {
                    suspendedRun = run;
                }

                return run;
            }
            catch (Exception ex)
            {
                if (linkerTouched)
                {
                    TryRollback();
                }

                return new SpellkitRunSession(this, ex);
            }
            finally
            {
                active = false;
            }
        }
        finally
        {
            operationScope.Value = false;
            operationGate.Release();
        }
    }

    public Task<SpellkitSignalDispatchResult> DispatchSignalsAsync(
        CancellationToken cancellationToken = default) =>
        DispatchSignalsCoreAsync(cancellationToken);

    private async Task<SpellkitSignalDispatchResult> DispatchSignalsCoreAsync(
        CancellationToken cancellationToken)
    {
        if (operationScope.Value)
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        operationScope.Value = true;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BeginOperation();
            var started = Stopwatch.GetTimestamp();
            var executionId = Guid.NewGuid();
            Environment.Telemetry.BeginExecution(executionId);
            Environment.Tracing.Write(SpellkitTraceKind.ExecutionStarted, "DispatchSignals");
            using var control = CreateControl(cancellationToken);
            var errors = new List<Exception>();
            var delivered = 0;
            var completed = false;
            try
            {
                var pending = Environment.Signals.PendingCount;
                var stop = false;

                for (var i = 0; i < pending && !stop; i++)
                {
                    try
                    {
                        control?.OnSignal();
                    }
                    catch (Exception ex) when (ex is SpellkitRuntimeException or OperationCanceledException)
                    {
                        errors.Add(ex);
                        break;
                    }

                    if (!Environment.Signals.TryDequeue(out var signal))
                    {
                        break;
                    }

                    delivered++;
                    Environment.Tracing.Write(SpellkitTraceKind.SignalDelivered, signal.Name);
                    foreach (var handler in Environment.Signals.GetHostHandlers(signal.Name))
                    {
                        try
                        {
                            await Task.Run(
                                () => handler(signal),
                                CancellationToken.None).ConfigureAwait(false);
                            control?.Checkpoint();
                        }
                        catch (Exception ex)
                        {
                            errors.Add(ex);
                            if (IsExecutionStop(ex))
                            {
                                stop = true;
                                break;
                            }
                        }
                    }

                    if (stop || runtimeContext is null)
                    {
                        continue;
                    }

                    foreach (var handler in Environment.Signals.GetScriptHandlers(signal.Name))
                    {
                        try
                        {
                            var context = CreateExecutionContext(runtimeContext, control);
                            var payload = SpellkitHostRootTypeInfo.Wrap(context, signal.RawPayload);
                            if (handler is SpellkitNativeFunction function)
                            {
                                var execution = await Task.Run(
                                    () => SpellkitMachine.ExecuteWithArguments(
                                        function,
                                        [payload],
                                        context),
                                    CancellationToken.None).ConfigureAwait(false);
                                await CompleteExecutionAsync(execution).ConfigureAwait(false);
                            }
                            else
                            {
                                handler.Call(context, payload);
                            }
                            context.ThrowIf();
                        }
                        catch (Exception ex)
                        {
                            errors.Add(ex);
                            if (IsExecutionStop(ex))
                            {
                                stop = true;
                                break;
                            }
                        }
                    }
                }

                var result = new SpellkitSignalDispatchResult(
                    delivered,
                    errors,
                    executionId,
                    Metrics(started, TimeSpan.Zero, Stopwatch.GetElapsedTime(started), control));
                completed = true;
                return result;
            }
            finally
            {
                Environment.Tracing.Write(
                    SpellkitTraceKind.ExecutionCompleted,
                    "DispatchSignals",
                    Stopwatch.GetElapsedTime(started),
                    new Dictionary<string, object?>
                    {
                        ["success"] = completed && errors.Count == 0,
                        ["delivered"] = delivered
                    });
                Environment.Telemetry.EndExecution();
                active = false;
            }
        }
        finally
        {
            operationScope.Value = false;
            operationGate.Release();
        }
    }

    private async Task<SpellkitExecutionResult> ExecuteCoreAsync(
        Func<Result<UnitComposition>> compile,
        string operationName,
        CancellationToken cancellationToken,
        SpellkitFailureKind fallbackFailureKind = SpellkitFailureKind.Host,
        bool touchesLinker = true)
    {
        if (operationScope.Value)
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        operationScope.Value = true;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BeginOperation();
            var started = Stopwatch.GetTimestamp();
            var executionId = Guid.NewGuid();
            Environment.Telemetry.BeginExecution(executionId);
            Environment.Tracing.Write(SpellkitTraceKind.ExecutionStarted, operationName);
            using var control = CreateControl(cancellationToken);
            var signalCheckpoint = Environment.Signals.CreateScriptSubscriptionCheckpoint();
            var committed = false;
            var succeeded = false;
            var linkerTouched = false;
            var compileDuration = TimeSpan.Zero;
            var vmDuration = TimeSpan.Zero;
            IReadOnlyList<BuildMessage> messages = Array.Empty<BuildMessage>();
            try
            {
                control?.Checkpoint();
                var compileStarted = Stopwatch.GetTimestamp();
                linkerTouched = touchesLinker;
                var made = await Task.Run(compile, CancellationToken.None).ConfigureAwait(false);
                compileDuration = Stopwatch.GetElapsedTime(compileStarted);
                Environment.Tracing.Write(
                    SpellkitTraceKind.Compilation, duration: compileDuration);
                messages = made.Messages.ToArray();
                control?.Checkpoint();

                if (!made.Success || made.Value is null)
                {
                    return new(
                        null,
                        messages,
                        SpellkitFailure.Compilation(messages.Select(SpellkitDiagnostic.From).ToArray()),
                        operationName,
                        executionId,
                        Metrics(started, compileDuration, vmDuration, control));
                }

                var context = CreateExecutionContext(made.Value, control);

                var vmStarted = Stopwatch.GetTimestamp();
                ExecutionResult result;
                try
                {
                    result = await Task.Run(
                        () => SpellkitMachine.Execute(context),
                        CancellationToken.None).ConfigureAwait(false);
                    result = await CompleteExecutionAsync(result).ConfigureAwait(false);
                }
                finally
                {
                    vmDuration = Stopwatch.GetElapsedTime(vmStarted);
                    Environment.Tracing.Write(SpellkitTraceKind.VmExecution, duration: vmDuration);
                }
                control?.Checkpoint();
                if (touchesLinker)
                {
                    linker.Commit();
                }
                committed = true;
                succeeded = true;
                return new(result.Value, messages, null, operationName, executionId,
                    Metrics(started, compileDuration, vmDuration, control));
            }
            catch (Exception ex)
            {
                if (linkerTouched)
                {
                    TryRollback();
                }

                return new(null, messages, SpellkitFailure.From(ex, fallbackFailureKind), operationName, executionId,
                    Metrics(started, compileDuration, vmDuration, control));
            }
            finally
            {
                if (!committed)
                {
                    Environment.Signals.RollbackScriptSubscriptions(signalCheckpoint);
                }

                Environment.Tracing.Write(
                    SpellkitTraceKind.ExecutionCompleted,
                    operationName,
                    Stopwatch.GetElapsedTime(started),
                    new Dictionary<string, object?> { ["success"] = succeeded });
                Environment.Telemetry.EndExecution();
                active = false;
            }
        }
        finally
        {
            operationScope.Value = false;
            operationGate.Release();
        }
    }

    public void Reset()
    {
        EnterSynchronousOperationGate();
        try
        {
            lock (syncRoot)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (active)
                {
                    throw new InvalidOperationException("A host instance cannot be reset while it is executing.");
                }

                linker = new(lookup, arguments);
                runtimeContext = null;
                submission = 0;
                Environment.Reset();
            }
        }
        finally
        {
            ExitSynchronousOperationGate();
        }
    }

    public void Dispose()
    {
        EnterSynchronousOperationGate();
        try
        {
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                if (active)
                {
                    throw new InvalidOperationException("A host instance cannot be disposed while it is executing.");
                }

                try
                {
                    Environment.Dispose();
                }
                finally
                {
                    runtimeContext = null;
                    disposed = true;
                }
            }
        }
        finally
        {
            ExitSynchronousOperationGate();
        }
    }

    private void BeginOperation(SpellkitRunSession? resumingRun = null)
    {
        if (active || (suspendedRun is not null && !ReferenceEquals(suspendedRun, resumingRun)))
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        active = true;
    }

    private void TryRollback()
    {
        try
        {
            linker.Rollback();
        }
        catch
        {
            // Preserve the original execution failure.
        }
    }

    private ExecutionContext CreateExecutionContext(
        UnitComposition composition,
        ExecutionControl? control)
    {
        ExecutionContext context;

        if (runtimeContext is null)
        {
            context = SpellkitMachine.CreateExecutionContext(composition);
            runtimeContext = context.RuntimeContext;
        }
        else
        {
            runtimeContext.Refresh(composition);
            context = SpellkitMachine.CreateExecutionContext(runtimeContext);
        }

        SetHostVariables(context, control);

        return context;
    }

    private ExecutionContext CreateExecutionContext(
        RuntimeContext context,
        ExecutionControl? control)
    {
        var executionContext = SpellkitMachine.CreateExecutionContext(context);
        SetHostVariables(executionContext, control);
        return executionContext;
    }

    private void SetHostVariables(ExecutionContext context, ExecutionControl? control)
    {
        context.Control = control;
        context.SetContextVariable(SpellkitHostEnvironment.ContextKey, Environment);
        context.SetContextVariable(SpellkitHostEnvironment.RootContextKey, Environment.Root);
        context.SetContextVariable(SpellkitEnvironment.ContextKey, SpellkitEnvironment);
        SetSelectHostVariables(context);

        if (HostContext is not null)
        {
            context.SetContextVariable(SpellkitCommandContext.HostContextKey, HostContext);
        }

        foreach (var (name, value) in SpellkitEnvironment.Bindings)
        {
            context.SetContextVariable("Spellkit.Environment." + name, value!);
        }
    }

    private ExecutionControl? CreateControl(CancellationToken cancellationToken)
    {
        var limits = Environment.Limits;
        if (!limits.RequiresControl && !Environment.Tracing.Enabled && !cancellationToken.CanBeCanceled)
        {
            return null;
        }

        return new(
            limits.MaxInstructions,
            limits.MaxExecutionTime,
            limits.MaxHostCommands,
            limits.MaxSignals,
            limits.MaxCallDepth,
            limits.TimeProvider,
            cancellationToken);
    }

    private async ValueTask<ExecutionResult> CompleteAwaitablesAsync(ExecutionResult result)
    {
        while (result.Reason is TerminationReason.Suspended
            && result.Continuation is not null
            && result.Suspension?.Awaitable is { } awaitable)
        {
            await awaitable.WaitAsync().ConfigureAwait(false);

            result = SpellkitMachine.Resume(result.Continuation, awaitable);
        }

        return result;
    }

    private void EnterSynchronousOperationGate()
    {
        if (operationScope.Value)
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        operationGate.Wait();
        operationScope.Value = true;
    }

    private void ExitSynchronousOperationGate()
    {
        operationScope.Value = false;
        operationGate.Release();
    }

    private static SpellkitExecutionMetrics Metrics(
        long started,
        TimeSpan compilation,
        TimeSpan vm,
        ExecutionControl? control) => new(
            Stopwatch.GetElapsedTime(started),
            compilation,
            vm,
            control?.Instructions ?? 0,
            control?.HostCommands ?? 0,
            control?.Signals ?? 0);

    private static bool IsExecutionStop(Exception exception) =>
        exception is SpellkitExecutionLimitException or OperationCanceledException;
}
