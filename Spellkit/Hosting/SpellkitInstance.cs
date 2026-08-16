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

public enum SpellkitDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record SpellkitDiagnostic(
    SpellkitDiagnosticSeverity Severity,
    int Code,
    string Message,
    string? File,
    int Line,
    int Column)
{
    internal static SpellkitDiagnostic From(BuildMessage message) => new(
        message.Type switch
        {
            BuildMessageType.Error => SpellkitDiagnosticSeverity.Error,
            BuildMessageType.Warning => SpellkitDiagnosticSeverity.Warning,
            _ => SpellkitDiagnosticSeverity.Information
        },
        message.Code,
        message.Message,
        message.File,
        message.Line,
        message.Column);
}

public enum SpellkitFailureKind
{
    Compilation,
    Runtime,
    Host,
    Input,
    Cancelled,
    Limit
}

public sealed record SpellkitFailure(
    SpellkitFailureKind Kind,
    string Message,
    Exception? Exception = null,
    SpellkitExecutionLimitKind? Limit = null)
{
    internal static SpellkitFailure Compilation(IReadOnlyList<SpellkitDiagnostic> diagnostics) => new(
        SpellkitFailureKind.Compilation,
        diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == SpellkitDiagnosticSeverity.Error)?.Message
            ?? "Compilation failed.");

    internal static SpellkitFailure From(Exception exception, SpellkitFailureKind fallback) => exception switch
    {
        SpellkitBuildException { InnerException: { } inner } =>
            From(inner, fallback),
        SpellkitExecutionLimitException limit => new(
            SpellkitFailureKind.Limit,
            limit.Message,
            limit,
            limit.Kind),
        OperationCanceledException => new(SpellkitFailureKind.Cancelled, exception.Message, exception),
        SpellkitRuntimeException => new(SpellkitFailureKind.Runtime, exception.Message, exception),
        _ => new(fallback, exception.Message, exception)
    };
}

public interface ISpellkitOperationResult
{
    bool Success { get; }
    IReadOnlyList<SpellkitFailure> Failures { get; }
    Guid ExecutionId { get; }
    SpellkitExecutionMetrics Metrics { get; }
    SpellkitExecution Execution { get; }
}

public sealed class SpellkitExecutionResult : ISpellkitOperationResult
{
    private readonly SpellkitObject? value;

    internal SpellkitExecutionResult(
        SpellkitObject? value,
        IReadOnlyList<BuildMessage> messages,
        SpellkitFailure? failure,
        string operation,
        Guid executionId,
        SpellkitExecutionMetrics metrics)
    {
        this.value = value;
        Diagnostics = messages.Select(SpellkitDiagnostic.From).ToArray();
        Failure = failure ?? (messages.Any(message => message.Type == BuildMessageType.Error)
            ? SpellkitFailure.Compilation(Diagnostics)
            : null);
        Failures = Failure is null ? Array.Empty<SpellkitFailure>() : new[] { Failure };
        ExecutionId = executionId;
        Metrics = metrics;
        Execution = new(executionId, operation, metrics);
    }

    public bool Success => Failure is null;

    public T? GetValue<T>() =>
        SpellkitHostValueConverter.Convert<T>(value, "Execution result");

    public bool TryGetValue<T>(out T? value) =>
        SpellkitHostValueConverter.TryConvert(this.value, out value);

    public IReadOnlyList<SpellkitDiagnostic> Diagnostics { get; }

    public SpellkitFailure? Failure { get; }

    public IReadOnlyList<SpellkitFailure> Failures { get; }

    public Guid ExecutionId { get; }

    public SpellkitExecutionMetrics Metrics { get; }

    public SpellkitExecution Execution { get; }
}

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

    internal SpellkitExecutionResult Execute(CancellationToken cancellationToken = default)
    {
        if (program is null)
        {
            throw new InvalidOperationException(
                "This instance was not created from a compiled Spellkit program.");
        }

        return ExecuteProgram(program, cancellationToken);
    }

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
            runAsynchronously: true,
            touchesLinker: false);
    }

    internal SpellkitExecutionResult Execute(string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ExecuteSource(source, null, "Execute", cancellationToken);
    }

    internal SpellkitRunSession Start(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return StartCore(() => linker.Make(SourceBuffer.FromString(
            source,
            $"<host:{++submission}>")));
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
            cancellationToken,
            runAsynchronously: true);
    }

    internal SpellkitExecutionResult Execute(
        string source,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        return ExecuteSource(source, sourceName, "Execute", cancellationToken);
    }

    internal SpellkitExecutionResult Execute(
        SpellkitCodeModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ExecuteCore(() => linker.Make(model), "ExecuteModel", cancellationToken);
    }

    internal SpellkitExecutionResult Execute(
        SourceBuffer buffer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return ExecuteCore(() => linker.Make(buffer), "ExecuteBuffer", cancellationToken);
    }

    internal SpellkitExecutionResult ExecuteFile(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var path = Path.GetFullPath(fileName);
        return ExecuteCore(
            () => linker.Make(SourceBuffer.FromString(File.ReadAllText(path), path)),
            "ExecuteFile",
            cancellationToken,
            SpellkitFailureKind.Input);
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
            runAsynchronously: true,
            SpellkitFailureKind.Input);
    }

    private SpellkitExecutionResult ExecuteSource(
        string source,
        string? sourceName,
        string operationName,
        CancellationToken cancellationToken)
    {
        return ExecuteCore(
            () => linker.Make(SourceBuffer.FromString(
                source,
                sourceName ?? $"<host:{++submission}>")),
            operationName,
            cancellationToken);
    }

    private SpellkitExecutionResult ExecuteProgram(
        SpellkitProgram program,
        CancellationToken cancellationToken)
    {
        return ExecuteCore(
            () => Result.Create(program.Composition),
            "ExecuteProgram",
            cancellationToken,
            touchesLinker: false);
    }

    private SpellkitExecutionResult ExecuteCore(
        Func<Result<UnitComposition>> compile,
        string operationName,
        CancellationToken cancellationToken,
        SpellkitFailureKind fallbackFailureKind = SpellkitFailureKind.Host,
        bool touchesLinker = true) =>
        ExecuteCoreAsync(
            compile,
            operationName,
            cancellationToken,
            runAsynchronously: false,
            fallbackFailureKind,
            touchesLinker).GetAwaiter().GetResult();

    private async Task<SpellkitExecutionResult> ExecuteCoreAsync(
        Func<Result<UnitComposition>> compile,
        string operationName,
        CancellationToken cancellationToken,
        bool runAsynchronously,
        SpellkitFailureKind fallbackFailureKind = SpellkitFailureKind.Host,
        bool touchesLinker = true)
    {
        if (operationScope.Value)
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        if (runAsynchronously)
        {
            await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            operationGate.Wait();
        }

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
                var made = runAsynchronously
                    ? await Task.Run(compile, CancellationToken.None).ConfigureAwait(false)
                    : compile();
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
                    result = runAsynchronously
                        ? await Task.Run(
                            () => SpellkitMachine.Execute(context),
                            CancellationToken.None).ConfigureAwait(false)
                        : SpellkitMachine.Execute(context);
                    result = await CompleteExecutionAsync(result, runAsynchronously).ConfigureAwait(false);
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

    private async ValueTask<ExecutionResult> CompleteExecutionAsync(
        ExecutionResult result,
        bool runAsynchronously)
    {
        while (result.Reason is TerminationReason.Suspended)
        {
            if (result.Continuation is null || result.Suspension is null)
            {
                throw new InvalidOperationException("The VM suspended without a continuation request.");
            }

            if (result.Suspension.Awaitable is { } awaitable)
            {
                if (runAsynchronously)
                {
                    await awaitable.WaitAsync().ConfigureAwait(false);
                }
                else
                {
                    awaitable.Wait();
                }

                result = SpellkitMachine.Resume(result.Continuation, awaitable);
                continue;
            }

            if (result.Suspension.Select is not { } selectInstance)
            {
                throw new InvalidOperationException("The VM suspended without a supported request.");
            }

            using var select = runAsynchronously
                ? await CreateSelectSessionAsync(selectInstance).ConfigureAwait(false)
                : CreateSelectSession(selectInstance);
            if (!select.IsCompleted)
            {
                if (runAsynchronously)
                {
                    await SpellkitEnvironment.RunSelectAsync(select).ConfigureAwait(false);
                }
                else
                {
                    SpellkitEnvironment.RunSelect(select);
                }
            }
            result = SpellkitMachine.Resume(result.Continuation, select.CompletionValue);
        }

        return result;
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

    internal Task<SpellkitSelectResult> SelectAsync(
        SpellkitRunSession run,
        string choiceId,
        object? argument,
        bool hasArgument) =>
        DispatchSelectActionAsync(
            run,
            session => hasArgument
                ? session.SelectAsync(choiceId, argument)
                : session.SelectAsync(choiceId));

    internal Task<SpellkitSelectResult> SendAsync(
        SpellkitRunSession run,
        string eventId,
        object? argument,
        bool hasArgument) =>
        DispatchSelectActionAsync(
            run,
            session => hasArgument
                ? session.SendAsync(eventId, argument)
                : session.SendAsync(eventId));

    private async Task<SpellkitSelectResult> DispatchSelectActionAsync(
        SpellkitRunSession run,
        Func<SpellkitSelectSession, Task<SpellkitSelectResult>> dispatch)
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
            BeginOperation(run);
            try
            {
                var select = run.GetSelect();
                var actionResult = await dispatch(select).ConfigureAwait(false);
                if (!actionResult.IsCompleted)
                {
                    return actionResult;
                }

                var completionValue = select.CompletionValue;
                var execution = await ResumeSelectContinuationAsync(
                    run.GetContinuation(),
                    completionValue).ConfigureAwait(false);
                await run.AdvanceAsync(execution).ConfigureAwait(false);
                if (run.IsCompleted)
                {
                    suspendedRun = null;
                    return new SpellkitSelectResult(actionResult.Snapshot, actionResult.Value);
                }

                return new SpellkitSelectResult(run.GetSelect().Snapshot);
            }
            catch (Exception ex)
            {
                run.Fail(ex);
                suspendedRun = null;
                throw;
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

    internal void Cancel(SpellkitRunSession run)
    {
        EnterSynchronousOperationGate();
        try
        {
            lock (syncRoot)
            {
                if (ReferenceEquals(suspendedRun, run))
                {
                    suspendedRun = null;
                }

                run.Cancel();
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

    internal ExecutionResult InvokeSelectAction(SpellkitFunction action, SpellkitObject[] arguments)
    {
        var ownsGate = !operationScope.Value;
        if (ownsGate)
        {
            EnterSynchronousOperationGate();
        }

        try
        {
            lock (syncRoot)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                var nested = active;
                if (!nested)
                {
                    BeginOperation();
                }
                try
                {
                    var context = CreateExecutionContext(runtimeContext!, control: null);
                    if (action is not SpellkitNativeFunction function)
                    {
                        throw new InvalidOperationException("The select action function is unavailable.");
                    }
                    var result = SpellkitMachine.ExecuteWithArguments(function, arguments, context);
                    return CompleteAwaitablesAsync(
                        result,
                        runAsynchronously: false).GetAwaiter().GetResult();
                }
                finally
                {
                    if (!nested)
                    {
                        active = false;
                    }
                }
            }
        }
        finally
        {
            if (ownsGate)
            {
                ExitSynchronousOperationGate();
            }
        }
    }

    internal async Task<ExecutionResult> InvokeSelectActionAsync(
        SpellkitFunction action,
        SpellkitObject[] arguments)
    {
        var ownsGate = !operationScope.Value;
        if (ownsGate)
        {
            await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            operationScope.Value = true;
        }

        var nested = false;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            nested = active;
            if (!nested)
            {
                BeginOperation();
            }

            var context = CreateExecutionContext(runtimeContext!, control: null);
            if (action is not SpellkitNativeFunction function)
            {
                throw new InvalidOperationException("The select action function is unavailable.");
            }

            var result = await Task.Run(
                () => SpellkitMachine.ExecuteWithArguments(function, arguments, context),
                CancellationToken.None).ConfigureAwait(false);
            return await CompleteAwaitablesAsync(
                result,
                runAsynchronously: true).ConfigureAwait(false);
        }
        finally
        {
            if (!nested)
            {
                active = false;
            }
            if (ownsGate)
            {
                operationScope.Value = false;
                operationGate.Release();
            }
        }
    }

    internal ExecutionResult ResumeSelectContinuation(
        SpellkitMachine.VmContinuation continuation,
        SpellkitObject value) =>
        CompleteAwaitablesAsync(
            SpellkitMachine.Resume(continuation, value),
            runAsynchronously: false).GetAwaiter().GetResult();

    internal async Task<ExecutionResult> ResumeSelectContinuationAsync(
        SpellkitMachine.VmContinuation continuation,
        SpellkitObject value)
    {
        var result = SpellkitMachine.Resume(continuation, value);
        return await CompleteAwaitablesAsync(
            result,
            runAsynchronously: true).ConfigureAwait(false);
    }

    internal bool EvaluateSelectGuard(
        SpellkitFunction guard,
        SpellkitObject[] arguments) =>
        EvaluateSelectValue(guard, arguments, "guard").IsTrue();

    internal SpellkitObject EvaluateSelectView(
        SpellkitFunction view,
        SpellkitObject[] arguments) =>
        EvaluateSelectValue(view, arguments, "view");

    internal SpellkitObject EvaluateSelectDynamicChoice(
        SpellkitFunction function,
        SpellkitObject[] arguments) =>
        EvaluateSelectValue(function, arguments, "dynamic choice");

    private SpellkitObject EvaluateSelectValue(
        SpellkitFunction function,
        SpellkitObject[] arguments,
        string kind)
    {
        var ownsGate = !operationScope.Value;
        if (ownsGate)
        {
            EnterSynchronousOperationGate();
        }

        try
        {
            lock (syncRoot)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                var nested = active;
                if (!nested)
                {
                    // Reading Choices for the one suspended run is safe: the continuation stays
                    // suspended while this short, serialized guard evaluation executes.
                    if (suspendedRun is null)
                    {
                        BeginOperation();
                    }
                    else
                    {
                        active = true;
                    }
                }
                try
                {
                    var context = CreateExecutionContext(runtimeContext!, control: null);
                    SpellkitObject value;
                    if (function is SpellkitNativeFunction nativeFunction)
                    {
                        var execution = SpellkitMachine.ExecuteWithArguments(nativeFunction, arguments, context);
                        execution = CompleteAwaitablesAsync(
                            execution,
                            runAsynchronously: false).GetAwaiter().GetResult();
                        if (execution.Reason is TerminationReason.Suspended)
                        {
                            throw new InvalidOperationException($"A select {kind} cannot start a select.");
                        }
                        value = execution.Value ?? SpellkitNil.Instance;
                    }
                    else
                    {
                        value = function.Call(context, arguments);
                    }
                    context.ThrowIf();
                    return value;
                }
                finally
                {
                    if (!nested)
                    {
                        active = false;
                    }
                }
            }
        }
        finally
        {
            if (ownsGate)
            {
                ExitSynchronousOperationGate();
            }
        }
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
        context.SetContextVariable(
            SpellkitSelectFactoryResolver.ContextKey,
            new SpellkitSelectFactoryResolver(this));

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

    private async ValueTask<ExecutionResult> CompleteAwaitablesAsync(
        ExecutionResult result,
        bool runAsynchronously)
    {
        while (result.Reason is TerminationReason.Suspended
            && result.Continuation is not null
            && result.Suspension?.Awaitable is { } awaitable)
        {
            if (runAsynchronously)
            {
                await awaitable.WaitAsync().ConfigureAwait(false);
            }
            else
            {
                awaitable.Wait();
            }

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
