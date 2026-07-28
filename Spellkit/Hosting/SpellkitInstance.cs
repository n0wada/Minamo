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
    SpkExecutionLimitKind? Limit = null)
{
    internal static SpellkitFailure Compilation(IReadOnlyList<SpellkitDiagnostic> diagnostics) => new(
        SpellkitFailureKind.Compilation,
        diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == SpellkitDiagnosticSeverity.Error)?.Message
            ?? "Compilation failed.");

    internal static SpellkitFailure From(Exception exception, SpellkitFailureKind fallback) => exception switch
    {
        SpkBuildException { InnerException: { } inner } =>
            From(inner, fallback),
        SpkExecutionLimitException limit => new(
            SpellkitFailureKind.Limit,
            limit.Message,
            limit,
            limit.Kind),
        OperationCanceledException => new(SpellkitFailureKind.Cancelled, exception.Message, exception),
        SpkRuntimeException => new(SpellkitFailureKind.Runtime, exception.Message, exception),
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
    private readonly SpkObject? value;

    internal SpellkitExecutionResult(
        SpkObject? value,
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

public sealed class SpellkitInstance : IDisposable
{
    private readonly System.Threading.Lock syncRoot = new();
    private readonly FileLookup lookup;
    private readonly SpellkitProgram? program;
    private readonly SpkTuple? arguments;
    private SpkIncrementalLinker linker;
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
        SpkTuple? arguments = null)
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

    public SpellkitExecutionResult Execute(CancellationToken cancellationToken = default)
    {
        if (program is null)
        {
            throw new InvalidOperationException(
                "This instance was not created from a compiled Spellkit program.");
        }

        return ExecuteProgram(program, cancellationToken);
    }

    public SpellkitExecutionResult Execute(string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ExecuteSource(source, null, "Execute", cancellationToken);
    }

    public SpellkitRunSession Start(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return StartCore(() => linker.Make(SourceBuffer.FromString(
            source,
            $"<host:{++submission}>")));
    }

    public Task<SpellkitExecutionResult> ExecuteAsync(
        string source,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Execute(source, cancellationToken), CancellationToken.None);

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
        SpkCodeModel model,
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

    public SpellkitExecutionResult ExecuteFile(
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

    public SpellkitSelectSession OpenSelect(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (runtimeContext is null)
        {
            if (program is null)
            {
                throw new InvalidOperationException(
                    "Execute source containing the select before opening a select session.");
            }

            var initialization = Execute();
            if (!initialization.Success)
            {
                throw new InvalidOperationException(
                    "The select program could not be initialized.", initialization.Failure?.Exception);
            }
        }

        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (suspendedRun is not null)
            {
                throw new InvalidOperationException("A script run is already waiting for a select.");
            }

            return CreateSelectSession(name);
        }
    }

    public Task<SpellkitExecutionResult> ExecuteFileAsync(
        string fileName,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExecuteFile(fileName, cancellationToken), CancellationToken.None);

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
        bool touchesLinker = true)
    {
        lock (syncRoot)
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
                var made = compile();
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
                    result = SpkMachine.Execute(context);
                    while (result.Reason is TerminationReason.Suspended)
                    {
                        if (result.Continuation is null
                            || result.Suspension is not { SelectName.Length: > 0 } suspension)
                        {
                            throw new InvalidOperationException("The VM suspended without a select request.");
                        }

                        using var select = CreateSelectSession(suspension.SelectName);
                        SpellkitEnvironment.RunSelect(select);
                        result = SpkMachine.Resume(result.Continuation);
                    }
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
    }

    public SpellkitSignalDispatchResult DispatchSignals(CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
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
                    catch (Exception ex) when (ex is SpkRuntimeException or OperationCanceledException)
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
                            handler(signal);
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
                            handler.Call(context, SpellkitHostRootTypeInfo.Wrap(context, signal.RawPayload));
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
    }

    public Task<SpellkitSignalDispatchResult> DispatchSignalsAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DispatchSignals(cancellationToken), CancellationToken.None);

    public void Reset()
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

    public void Dispose()
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

    internal SpellkitSelectSession CreateSelectSession(string name)
    {
        var selectName = SpellkitSelectAliases.Resolve(runtimeContext!, name);
        var matches = new List<SpkSelectFactory>();
        for (var unitId = 0; unitId < runtimeContext!.Composition.Units.Length; unitId++)
        {
            var scope = runtimeContext.Composition.Units[unitId].GlobalScope;
            if (scope is null)
            {
                continue;
            }

            var symbol = scope.GetVariable(selectName);
            if (!symbol.IsEmpty()
                && runtimeContext.Units[unitId] is { } values
                && values[symbol.Address] is SpkSelectFactory factory)
            {
                matches.Add(factory);
            }
        }

        if (matches.Count == 0)
        {
            throw new ArgumentException($"No select named '{name}' is available.", nameof(name));
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"The select name '{name}' is ambiguous.");
        }

        return new SpellkitSelectSession(this, matches[0].Create());
    }

    internal SpellkitSelectResult Choose(
        SpellkitRunSession run,
        string choiceId,
        object? argument,
        bool hasArgument)
    {
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BeginOperation(run);
            try
            {
                var choiceResult = hasArgument
                    ? run.GetSelect().Choose(choiceId, argument)
                    : run.GetSelect().Choose(choiceId);
                if (!choiceResult.IsCompleted)
                {
                    return choiceResult;
                }

                run.Advance(SpkMachine.Resume(run.GetContinuation()));
                if (run.IsCompleted)
                {
                    suspendedRun = null;
                    return new SpellkitSelectResult(Array.Empty<SpellkitChoice>(), isCompleted: true);
                }

                return new SpellkitSelectResult(run.Choices, isCompleted: false);
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
    }

    internal void Cancel(SpellkitRunSession run)
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
            context = SpkMachine.CreateExecutionContext(composition);
            runtimeContext = context.RuntimeContext;
        }
        else
        {
            runtimeContext.Refresh(composition);
            context = SpkMachine.CreateExecutionContext(runtimeContext);
        }

        SetHostVariables(context, control);

        return context;
    }

    private SpellkitRunSession StartCore(Func<Result<UnitComposition>> compile)
    {
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BeginOperation();
            var linkerTouched = false;
            try
            {
                linkerTouched = true;
                var made = compile();
                if (!made.Success || made.Value is null)
                {
                    TryRollback();
                    return new SpellkitRunSession(this, new InvalidOperationException(
                        string.Join(System.Environment.NewLine, made.Messages)));
                }

                var result = SpkMachine.Execute(CreateExecutionContext(made.Value, control: null));
                linker.Commit();
                var run = new SpellkitRunSession(this, result);
                run.Advance(result);
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
    }

    internal ExecutionResult InvokeSelectChoice(SpkFunction choice, SpkObject[] arguments)
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
                if (choice is not SpkNativeFunction function)
                {
                    throw new InvalidOperationException("The select choice function is unavailable.");
                }
                return SpkMachine.ExecuteWithArguments(function, arguments, context);
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

    internal bool EvaluateSelectGuard(SpkFunction guard)
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
                var result = guard.Call(context);
                context.ThrowIf();
                return result.IsTrue();
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

    private ExecutionContext CreateExecutionContext(
        RuntimeContext context,
        ExecutionControl? control)
    {
        var executionContext = SpkMachine.CreateExecutionContext(context);
        SetHostVariables(executionContext, control);
        return executionContext;
    }

    private void SetHostVariables(ExecutionContext context, ExecutionControl? control)
    {
        context.Control = control;
        context.SetContextVariable(SpellkitHostEnvironment.ContextKey, Environment);
        context.SetContextVariable(SpellkitHostEnvironment.RootContextKey, Environment.Root);
        context.SetContextVariable(SpellkitEnvironment.ContextKey, SpellkitEnvironment);
        context.SetContextVariable(SpellkitSelectInvoker.ContextKey, new SpellkitSelectInvoker(this));

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
        exception is SpkExecutionLimitException or OperationCanceledException;
}
