using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Minamo.Compiler;
using Minamo.Debug;
using Minamo.Linker;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Hosting;

internal sealed class HostModuleProvider : IModuleProvider
{
    private readonly Dictionary<string, HostModuleDefinition> modules;

    public HostModuleProvider(IEnumerable<HostModuleDefinition> modules) =>
        this.modules = modules.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

    public bool TryGetUnit(string name, out Unit unit)
    {
        if (modules.TryGetValue(name, out var module))
        {
            unit = module.UnitFactory?.Invoke() ?? new HostForeignUnit(module);
            return true;
        }

        unit = null!;
        return false;
    }
}

internal sealed class HostForeignUnit : ForeignUnit
{
    public HostForeignUnit(HostModuleDefinition module)
    {
        FileName = $"<host:{module.Name}>";

        foreach (var factory in module.ForeignTypes)
        {
            AddForeignType(factory());
        }

        foreach (var type in module.Types)
        {
            AddHostType(type);
        }

        foreach (var command in module.Commands)
        {
            Add(command.Name, new HostCommandFunction(command));
        }
    }

    private void AddHostType(HostTypeDefinition type)
    {
        var typeInfo = new HostTypeInfo(type);
        Types.Add(typeInfo);
        Add(type.Name, typeInfo);
        typeInfo.DeclaringUnit = this;
    }

    private void AddForeignType(MinamoForeignTypeInfo typeInfo)
    {
        Types.Add(typeInfo);
        typeInfo.DeclaringUnit = this;
    }
}

internal sealed class HostTypeInfo : MinamoForeignTypeInfo
{
    private readonly HostTypeDefinition type;

    public HostTypeInfo(HostTypeDefinition type) => this.type = type;

    public override string ReflectedTypeName => type.Name;

    protected override MinamoFunction? InitializeStaticMember(string name, ExecutionContext ctx)
    {
        for (var i = 0; i < type.Commands.Count; i++)
        {
            if (string.Equals(type.Commands[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return new HostCommandFunction(type.Commands[i]);
            }
        }

        return base.InitializeStaticMember(name, ctx);
    }
}

internal sealed class HostCommandFunction : MinamoForeignFunction
{
    private const string HostFailureMessage = "The host command failed.";
    private readonly MinamoCommandDescriptor command;

    public HostCommandFunction(MinamoCommandDescriptor command)
        : base(command.Name, CreateParameters(command.Parameters))
    {
        this.command = command;
        if (command.IsPropertyGetter)
        {
            Attr |= FunAttr.Auto;
        }
    }

    public override MinamoObject Clone() => new HostCommandFunction(command);

    protected override MinamoObject BindOrRun(ExecutionContext ctx, MinamoObject arg) =>
        Auto ? CallWithMemoryLayout(ctx, Array.Empty<MinamoObject>()) : base.BindOrRun(ctx, arg);

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args)
    {
        var environment = ctx.GetContextVariable<MinamoHostEnvironment>(MinamoHostEnvironment.ContextKey);
        var traceStarted = 0L;
        MinamoCallbackScope? callbackScope = null;
        var deferredCompletion = false;
        try
        {
            environment?.Capabilities.Demand(command.Capability);
            ctx.Control?.OnHostCommand();
            if (environment?.Tracing.Enabled == true)
            {
                traceStarted = Stopwatch.GetTimestamp();
            }

            using var commandScope = environment?.Telemetry.EnterCommand(command.Name);
            callbackScope = new MinamoCallbackScope();
            var value = command.Invoke(new MinamoCommandContext(ctx, command, args, callbackScope));
            if (value is MinamoAwaitable awaitable)
            {
                deferredCompletion = true;
                return awaitable.Configure(
                    (completionContext, result) =>
                    {
                        completionContext.Control?.Checkpoint();
                        return completionContext.HasErrors
                            ? MinamoNil.Instance
                            : MinamoHostRootTypeInfo.Wrap(completionContext, result);
                    },
                    (completionContext, exception) =>
                        CompleteFailure(completionContext, environment, exception),
                    () =>
                    {
                        callbackScope.Dispose();
                        WriteTrace(environment, traceStarted);
                    });
            }

            ctx.Control?.Checkpoint();
            if (ctx.HasErrors)
            {
                return MinamoNil.Instance;
            }

            return MinamoHostRootTypeInfo.Wrap(ctx, value);
        }
        catch (MinamoExecutionLimitException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            ctx.Control?.Checkpoint();
            throw;
        }
        catch (Exception ex)
        {
            ReportFailure(environment, ex);
            return ctx.ExternalFunctionFailure(this, HostFailureMessage);
        }
        finally
        {
            if (!deferredCompletion)
            {
                callbackScope?.Dispose();
                WriteTrace(environment, traceStarted);
            }
        }
    }

    protected override bool Equals(MinamoFunction func) =>
        func is HostCommandFunction other && ReferenceEquals(command, other.command);

    private void ReportFailure(MinamoHostEnvironment? environment, Exception exception)
    {
        if (environment is null)
        {
            return;
        }

        try
        {
            environment.Telemetry.Write(
                MinamoLogLevel.Error,
                HostFailureMessage,
                new Dictionary<string, object?>
                {
                    ["command"] = command.Name,
                    ["exceptionType"] = exception.GetType().FullName,
                    ["exceptionMessage"] = exception.Message
                });
        }
        catch
        {
            // Error reporting must not replace the original host command failure.
        }
    }

    private MinamoObject CompleteFailure(
        ExecutionContext context,
        MinamoHostEnvironment? environment,
        Exception exception)
    {
        if (exception is MinamoExecutionLimitException)
        {
            throw exception;
        }
        if (exception is OperationCanceledException)
        {
            context.Control?.Checkpoint();
            throw exception;
        }

        ReportFailure(environment, exception);
        return context.ExternalFunctionFailure(this, HostFailureMessage);
    }

    private void WriteTrace(MinamoHostEnvironment? environment, long traceStarted)
    {
        if (traceStarted != 0)
        {
            environment!.Tracing.Write(
                MinamoTraceKind.HostCommand,
                command.Name,
                Stopwatch.GetElapsedTime(traceStarted));
        }
    }

    private static Par[] CreateParameters(IReadOnlyList<MinamoCommandParameter> parameters)
    {
        var result = new Par[parameters.Count];

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            result[i] = parameter.HasDefault
                ? new Par(parameter.Name, TypeConverter.ConvertFrom(parameter.DefaultValue, parameter.Type))
                : new Par(parameter.Name);
        }

        return result;
    }
}
