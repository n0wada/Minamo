using Minamo.Compiler;
using Minamo.Linker;
using Minamo.Parser;
using Minamo.Parser.Model;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CancellationToken = System.Threading.CancellationToken;

namespace Minamo.Hosting;

public sealed partial class MinamoInstance : IDisposable
{
    private readonly System.Threading.Lock syncRoot = new();
    private readonly System.Threading.SemaphoreSlim operationGate = new(1, 1);
    private readonly System.Threading.AsyncLocal<bool> operationScope = new();
    private readonly FileLookup lookup;
    private readonly MinamoProgram? program;
    private readonly MinamoTuple? arguments;
    private MinamoIncrementalLinker linker;
    private RuntimeContext? runtimeContext;
    private int submission;
    private bool disposed;
    private bool active;

    internal MinamoInstance(
        FileLookup lookup,
        MinamoHostEnvironment environment,
        MinamoEnvironment minamoEnvironment,
        MinamoProgram? program = null,
        MinamoTuple? arguments = null)
    {
        this.lookup = lookup;
        this.program = program;
        this.arguments = arguments;
        Environment = environment;
        MinamoEnvironment = minamoEnvironment;
        linker = new(lookup, arguments);
    }

    public object? HostContext => Environment.HostContext;

    public MinamoHostEnvironment Environment { get; }

    public MinamoEnvironment MinamoEnvironment { get; }

    internal RuntimeContext? RuntimeContext => runtimeContext;

    public Task<MinamoExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        if (program is null)
        {
            throw new InvalidOperationException(
                "This instance was not created from a compiled Minamo program.");
        }

        return ExecuteCoreAsync(
            () => Result.Create(program.Composition),
            "ExecuteProgram",
            cancellationToken,
            touchesLinker: false);
    }

    public Task<MinamoExecutionResult> ExecuteAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (TryParseOpenSelectCommand(source, out var selectName))
        {
            return ExecuteOpenSelectCommandAsync(selectName, cancellationToken);
        }

        return ExecuteCoreAsync(
            () => linker.Make(SourceBuffer.FromString(
                source,
                $"<host:{++submission}>")),
            "Execute",
            cancellationToken);
    }

    private async Task<MinamoExecutionResult> ExecuteOpenSelectCommandAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var executionId = Guid.NewGuid();
        MinamoSelect? select = null;
        ExecutionControl? control = null;
        var gateAcquired = false;
        var operationStarted = false;
        var telemetryStarted = false;
        var vmDuration = TimeSpan.Zero;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureSelectRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (operationScope.Value)
            {
                throw new InvalidOperationException("A host instance cannot be entered recursively.");
            }

            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateAcquired = true;
            operationScope.Value = true;
            ObjectDisposedException.ThrowIf(disposed, this);
            BeginOperation();
            operationStarted = true;
            Environment.Telemetry.BeginExecution(executionId);
            telemetryStarted = true;
            control = CreateControl(cancellationToken, collectMetrics: true);
            control?.Checkpoint();

            var vmStarted = Stopwatch.GetTimestamp();
            try
            {
                var session = await OpenSelectSessionUnderGateAsync(
                    name,
                    control,
                    cancellationToken).ConfigureAwait(false);
                select = new MinamoSelect(session);
            }
            finally
            {
                vmDuration = Stopwatch.GetElapsedTime(vmStarted);
            }

            control?.Checkpoint();
            return new(
                null,
                Array.Empty<BuildMessage>(),
                null,
                "OpenSelect",
                executionId,
                Metrics(started, TimeSpan.Zero, vmDuration, control),
                select);
        }
        catch (Exception ex)
        {
            select?.Dispose();
            return new(
                null,
                Array.Empty<BuildMessage>(),
                MinamoFailure.From(ex, MinamoFailureKind.Input),
                "OpenSelect",
                executionId,
                Metrics(started, TimeSpan.Zero, vmDuration, control));
        }
        finally
        {
            control?.Dispose();
            if (telemetryStarted)
            {
                Environment.Telemetry.EndExecution();
            }
            if (operationStarted)
            {
                active = false;
            }
            if (gateAcquired)
            {
                operationScope.Value = false;
                operationGate.Release();
            }
        }
    }

    private static bool TryParseOpenSelectCommand(string source, out string name)
    {
        name = string.Empty;
        var lexer = new Lexer(SourceBuffer.FromString(source));
        var open = lexer.Next();
        if (open.Kind != TokenKind.LowerIdentifier
            || !open.TextSpan.SequenceEqual("open".AsSpan()))
        {
            return false;
        }

        var identifier = lexer.Next();
        if (identifier.AfterEol || !IsIdentifier(identifier.Kind))
        {
            return false;
        }

        name = identifier.Text;
        var token = lexer.Next();
        while (token.Kind == TokenKind.Dot && !token.AfterEol)
        {
            identifier = lexer.Next();
            if (identifier.AfterEol || !IsIdentifier(identifier.Kind))
            {
                name = string.Empty;
                return false;
            }

            name += "." + identifier.Text;
            token = lexer.Next();
        }

        if (token.Kind == TokenKind.EndOfFile)
        {
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static bool IsIdentifier(TokenKind kind) =>
        kind is TokenKind.LowerIdentifier or TokenKind.UpperIdentifier;

    internal Task<MinamoExecutionResult> ExecuteAsync(
        MinamoCodeModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ExecuteCoreAsync(
            () => linker.Make(model),
            "ExecuteModel",
            cancellationToken);
    }

    public Task<MinamoExecutionResult> ExecuteFileAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var path = Path.GetFullPath(fileName);
        return ExecuteCoreAsync(
            () => linker.Make(SourceBuffer.FromString(File.ReadAllText(path), path)),
            "ExecuteFile",
            cancellationToken,
            MinamoFailureKind.Input);
    }

    private async Task<MinamoExecutionResult> ExecuteCoreAsync(
        Func<Result<UnitComposition>> compile,
        string operationName,
        CancellationToken cancellationToken,
        MinamoFailureKind fallbackFailureKind = MinamoFailureKind.Host,
        bool touchesLinker = true)
    {
        if (operationScope.Value)
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        var queuedAt = Stopwatch.GetTimestamp();
        try
        {
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            return new(
                null,
                Array.Empty<BuildMessage>(),
                MinamoFailure.From(ex, fallbackFailureKind),
                operationName,
                Guid.NewGuid(),
                Metrics(queuedAt, TimeSpan.Zero, TimeSpan.Zero, control: null));
        }

        operationScope.Value = true;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BeginOperation();
            var started = Stopwatch.GetTimestamp();
            var executionId = Guid.NewGuid();
            Environment.Telemetry.BeginExecution(executionId);
            using var control = CreateControl(cancellationToken);
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
                messages = made.Messages.ToArray();
                control?.Checkpoint();

                if (!made.Success || made.Value is null)
                {
                    return new(
                        null,
                        messages,
                        MinamoFailure.Compilation(messages.Select(MinamoDiagnostic.From).ToArray()),
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
                        () => MinamoMachine.Execute(context),
                        CancellationToken.None).ConfigureAwait(false);
                    result = await CompleteAwaitablesAsync(result).ConfigureAwait(false);
                }
                finally
                {
                    vmDuration = Stopwatch.GetElapsedTime(vmStarted);
                }
                control?.Checkpoint();
                if (touchesLinker)
                {
                    linker.Commit();
                }
                return new(result.Value, messages, null, operationName, executionId,
                    Metrics(started, compileDuration, vmDuration, control));
            }
            catch (Exception ex)
            {
                if (linkerTouched)
                {
                    TryRollback();
                }

                return new(null, messages, MinamoFailure.From(ex, fallbackFailureKind), operationName, executionId,
                    Metrics(started, compileDuration, vmDuration, control));
            }
            finally
            {
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

    private void BeginOperation()
    {
        if (active)
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
            context = MinamoMachine.CreateExecutionContext(composition);
            runtimeContext = context.RuntimeContext;
        }
        else
        {
            runtimeContext.Refresh(composition);
            context = MinamoMachine.CreateExecutionContext(runtimeContext);
        }

        SetHostVariables(context, control);

        return context;
    }

    private ExecutionContext CreateExecutionContext(
        RuntimeContext context,
        ExecutionControl? control)
    {
        var executionContext = MinamoMachine.CreateExecutionContext(context);
        SetHostVariables(executionContext, control);
        return executionContext;
    }

    private void SetHostVariables(ExecutionContext context, ExecutionControl? control)
    {
        context.Control = control;
        context.SetContextVariable(MinamoHostEnvironment.ContextKey, Environment);
        context.SetContextVariable(MinamoHostEnvironment.RootContextKey, Environment.Root);
        context.SetContextVariable(MinamoEnvironment.ContextKey, MinamoEnvironment);

        if (HostContext is not null)
        {
            context.SetContextVariable(MinamoCommandContext.HostContextKey, HostContext);
        }

        foreach (var (name, value) in MinamoEnvironment.Bindings)
        {
            context.SetContextVariable("Minamo.Environment." + name, value!);
        }
    }

    private ExecutionControl? CreateControl(
        CancellationToken cancellationToken,
        bool collectMetrics = false)
    {
        var limits = Environment.Limits;
        if (!collectMetrics
            && !limits.RequiresControl
            && !cancellationToken.CanBeCanceled)
        {
            return null;
        }

        return new(
            limits.MaxInstructions,
            limits.MaxExecutionTime,
            limits.MaxHostCommands,
            limits.MaxCallDepth,
            limits.TimeProvider,
            cancellationToken);
    }

    private async ValueTask<ExecutionResult> CompleteAwaitablesAsync(
        ExecutionResult result,
        bool stopAtSelectRequest = false)
    {
        while (result.Reason is TerminationReason.Suspended
            && result.Continuation is { } continuation
            && result.Suspension?.Awaitable is { } awaitable)
        {
            if (stopAtSelectRequest && awaitable is MinamoSelectRequestAwaitable)
            {
                break;
            }

            try
            {
                await awaitable.WaitAsync(
                    result.Context.Control?.CancellationToken ?? default).ConfigureAwait(false);
                result = MinamoMachine.Resume(continuation, awaitable);
            }
            catch
            {
                awaitable.Abandon();
                continuation.Complete();
                result.Context.Control?.Checkpoint();
                throw;
            }
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

    private static MinamoExecutionMetrics Metrics(
        long started,
        TimeSpan compilation,
        TimeSpan vm,
        ExecutionControl? control) => new(
            Stopwatch.GetElapsedTime(started),
            compilation,
            vm,
            control?.Instructions ?? 0,
            control?.HostCommands ?? 0);
}
