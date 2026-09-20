using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExecutionContext = Minamo.Runtime.ExecutionContext;

namespace Minamo.Hosting;

public sealed partial class MinamoInstance
{
    /// <summary>Asynchronously opens a named select through its basic action-oriented API.</summary>
    public async Task<MinamoSelect> OpenSelectAsync(string name) =>
        new(await OpenSelectSessionAsync(name).ConfigureAwait(false));

    internal Task<MinamoSelectSession> OpenSelectSessionAsync(string name) =>
        OpenSelectSessionAsync(name, CancellationToken.None);

    internal async Task<MinamoSelectSession> OpenSelectSessionAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await EnsureSelectRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (operationScope.Value)
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        operationScope.Value = true;
        var executionId = Guid.NewGuid();
        var operationStarted = false;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BeginOperation();
            operationStarted = true;
            Environment.Telemetry.BeginExecution(executionId);
            using var control = CreateControl(cancellationToken);
            control?.Checkpoint();
            return await OpenSelectSessionUnderGateAsync(
                name,
                control,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (operationStarted)
            {
                Environment.Telemetry.EndExecution();
                active = false;
            }
            operationScope.Value = false;
            operationGate.Release();
        }
    }

    private async Task EnsureSelectRuntimeInitializedAsync(CancellationToken cancellationToken)
    {
        if (runtimeContext is not null)
        {
            return;
        }
        if (program is null)
        {
            throw new InvalidOperationException(
                "Execute source containing the select before opening a select session.");
        }

        var initialization = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (!initialization.Success)
        {
            if (initialization.Failure is
                { Kind: MinamoFailureKind.Cancelled or MinamoFailureKind.Limit, Exception: { } failure })
            {
                throw failure;
            }

            throw new InvalidOperationException(
                "The select program could not be initialized.", initialization.Failure?.Exception);
        }
    }

    private async Task<MinamoSelectSession> OpenSelectSessionUnderGateAsync(
        string name,
        ExecutionControl? control,
        CancellationToken cancellationToken)
    {
        control?.Checkpoint();
        var factory = ResolveSelectFactory(name)
            ?? throw new ArgumentException($"No select named '{name}' is available.", nameof(name));
        var session = await CreateSelectSessionAsync(
            CreateSelectInstance(factory, control),
            control,
            cancellationToken).ConfigureAwait(false);
        control?.Checkpoint();
        return session;
    }

    internal SelectInstance CreateSelectInstance(
        MinamoSelectFactory factory,
        ExecutionControl? control = null)
    {
        var nested = active;
        if (!nested)
        {
            BeginOperation();
        }

        try
        {
            var context = CreateExecutionContext(runtimeContext!, control);
            var select = factory.Create(context);
            context.ThrowIf();
            return select;
        }
        finally
        {
            if (!nested)
            {
                active = false;
            }
        }
    }

    internal MinamoSelectFactory? ResolveSelectFactory(string name)
    {
        if (MinamoSelectAliases.ResolveFactory(runtimeContext!, name) is { } aliasedFactory)
        {
            return aliasedFactory;
        }

        var selectName = MinamoSelectAliases.ResolveName(runtimeContext!, name);
        var matches = new List<MinamoSelectFactory>();
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
                && values[symbol.Address] is MinamoSelectFactory factory)
            {
                matches.Add(factory);
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"The select name '{name}' is ambiguous.");
        }

        return matches[0];
    }

    internal async Task<MinamoSelectSession> CreateSelectSessionAsync(
        SelectInstance select,
        ExecutionControl? control = null,
        CancellationToken cancellationToken = default)
    {
        var session = new MinamoSelectSession(this, select);
        try
        {
            await session.InitializeAsync(control, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    internal async Task<ExecutionResult> InvokeSelectActionAsync(
        MinamoFunction action,
        MinamoObject[] arguments,
        bool allowRequests = false,
        CancellationToken cancellationToken = default)
    {
        var ownsGate = !operationScope.Value;
        if (ownsGate)
        {
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            operationScope.Value = true;
        }

        var nested = false;
        var telemetryStarted = false;
        ExecutionControl? control = null;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            nested = active;
            if (!nested)
            {
                BeginOperation();
                Environment.Telemetry.BeginExecution(Guid.NewGuid());
                telemetryStarted = true;
            }

            control = CreateControl(cancellationToken);
            control?.Checkpoint();
            var context = CreateExecutionContext(runtimeContext!, control);
            context.SelectRequestsAllowed = allowRequests;
            if (action is not MinamoNativeFunction function)
            {
                throw new InvalidOperationException("The select action function is unavailable.");
            }

            var result = await Task.Run(
                () => MinamoMachine.ExecuteWithArguments(function, arguments, context),
                CancellationToken.None).ConfigureAwait(false);
            result = await CompleteAwaitablesAsync(
                result,
                stopAtSelectRequest: allowRequests).ConfigureAwait(false);
            control?.Checkpoint();
            return result;
        }
        finally
        {
            control?.Dispose();
            if (telemetryStarted)
            {
                Environment.Telemetry.EndExecution();
            }
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

    internal async Task<ExecutionResult> ResumeSelectActionAsync(
        ExecutionResult suspended,
        MinamoSelectRequestAwaitable request,
        MinamoObject response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suspended);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        if (suspended.Reason is not TerminationReason.Suspended
            || suspended.Continuation is null
            || !ReferenceEquals(suspended.Suspension?.Awaitable, request))
        {
            throw new InvalidOperationException("The select action is not waiting for this request.");
        }

        var ownsGate = !operationScope.Value;
        if (ownsGate)
        {
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            operationScope.Value = true;
        }

        var nested = false;
        var telemetryStarted = false;
        ExecutionControl? control = null;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            nested = active;
            if (!nested)
            {
                BeginOperation();
                Environment.Telemetry.BeginExecution(Guid.NewGuid());
                telemetryStarted = true;
            }

            control = CreateControl(cancellationToken);
            control?.Checkpoint();
            suspended.Context.Control = control;
            request.Respond(response);
            var result = await Task.Run(
                () => MinamoMachine.Resume(suspended.Continuation, request),
                CancellationToken.None).ConfigureAwait(false);
            result = await CompleteAwaitablesAsync(
                result,
                stopAtSelectRequest: true).ConfigureAwait(false);
            control?.Checkpoint();
            return result;
        }
        finally
        {
            control?.Dispose();
            if (telemetryStarted)
            {
                Environment.Telemetry.EndExecution();
            }
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

    internal async Task<bool> EvaluateSelectGuardAsync(
        MinamoFunction guard,
        MinamoObject[] arguments,
        CancellationToken cancellationToken = default,
        ExecutionControl? control = null) =>
        (await EvaluateSelectValueAsync(
            guard,
            arguments,
            "guard",
            cancellationToken,
            control).ConfigureAwait(false)).IsTrue();

    internal Task<MinamoObject> EvaluateSelectDescriptionAsync(
        MinamoFunction description,
        MinamoObject[] arguments,
        CancellationToken cancellationToken = default,
        ExecutionControl? control = null) =>
        EvaluateSelectValueAsync(description, arguments, "description", cancellationToken, control);

    internal Task<MinamoObject> EvaluateSelectPropertyAsync(
        MinamoFunction property,
        CancellationToken cancellationToken = default,
        ExecutionControl? control = null) =>
        EvaluateSelectValueAsync(
            property,
            Array.Empty<MinamoObject>(),
            "property",
            cancellationToken,
            control);

    internal Task<MinamoObject> EvaluateSelectMetadataAsync(
        MinamoFunction metadata,
        CancellationToken cancellationToken = default,
        ExecutionControl? control = null) =>
        EvaluateSelectValueAsync(
            metadata,
            Array.Empty<MinamoObject>(),
            "metadata",
            cancellationToken,
            control);

    private async Task<MinamoObject> EvaluateSelectValueAsync(
        MinamoFunction function,
        MinamoObject[] arguments,
        string kind,
        CancellationToken cancellationToken,
        ExecutionControl? control)
    {
        var ownsGate = !operationScope.Value;
        if (ownsGate)
        {
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            operationScope.Value = true;
        }

        var ownsControl = control is null;
        var effectiveControl = control;
        var nested = false;
        var telemetryStarted = false;
        try
        {
            effectiveControl ??= CreateControl(cancellationToken);
            ObjectDisposedException.ThrowIf(disposed, this);
            nested = active;
            if (!nested)
            {
                BeginOperation();
                Environment.Telemetry.BeginExecution(Guid.NewGuid());
                telemetryStarted = true;
            }
            try
            {
                effectiveControl?.Checkpoint();
                var context = CreateExecutionContext(runtimeContext!, effectiveControl);
                MinamoObject value;
                if (function is MinamoNativeFunction nativeFunction)
                {
                    var execution = await Task.Run(
                        () => MinamoMachine.ExecuteWithArguments(nativeFunction, arguments, context),
                        CancellationToken.None).ConfigureAwait(false);
                    execution = await CompleteAwaitablesAsync(execution).ConfigureAwait(false);
                    if (execution.Reason is TerminationReason.Suspended)
                    {
                        throw new InvalidOperationException($"A select {kind} did not complete successfully.");
                    }
                    value = execution.Value ?? MinamoNil.Instance;
                }
                else
                {
                    value = function.Call(context, arguments);
                }
                context.ThrowIf();
                effectiveControl?.Checkpoint();
                return value;
            }
            finally
            {
                if (telemetryStarted)
                {
                    Environment.Telemetry.EndExecution();
                }
                if (!nested)
                {
                    active = false;
                }
            }
        }
        finally
        {
            if (ownsControl)
            {
                effectiveControl?.Dispose();
            }
            if (ownsGate)
            {
                operationScope.Value = false;
                operationGate.Release();
            }
        }
    }

}
