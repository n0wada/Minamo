using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

public sealed partial class SpellkitInstance
{
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
}
