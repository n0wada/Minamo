using Spellkit.Compiler;
using Spellkit.Debug;
using Spellkit.Linker;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Spellkit.Hosting;

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

    private void AddForeignType(SpellkitForeignTypeInfo typeInfo)
    {
        Types.Add(typeInfo);
        typeInfo.DeclaringUnit = this;
    }
}

internal sealed class HostTypeInfo : SpellkitForeignTypeInfo
{
    private readonly HostTypeDefinition type;

    public HostTypeInfo(HostTypeDefinition type) => this.type = type;

    public override string ReflectedTypeName => type.Name;

    protected override SpellkitFunction? InitializeStaticMember(string name, ExecutionContext ctx)
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

internal sealed class HostCommandFunction : SpellkitForeignFunction
{
    private const string HostFailureMessage = "The host command failed.";
    private readonly SpellkitCommandDescriptor command;

    public HostCommandFunction(SpellkitCommandDescriptor command)
        : base(command.Name, CreateParameters(command.Parameters))
    {
        this.command = command;
        if (command.IsPropertyGetter)
        {
            Attr |= FunAttr.Auto;
        }
    }

    public override SpellkitObject Clone() => new HostCommandFunction(command);

    protected override SpellkitObject BindOrRun(ExecutionContext ctx, SpellkitObject arg) =>
        Auto ? CallWithMemoryLayout(ctx, Array.Empty<SpellkitObject>()) : base.BindOrRun(ctx, arg);

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args)
    {
        var environment = ctx.GetContextVariable<SpellkitHostEnvironment>(SpellkitHostEnvironment.ContextKey);
        var traceStarted = 0L;
        try
        {
            environment?.Capabilities.Demand(command.Capability);
            ctx.Control?.OnHostCommand();
            if (environment?.Tracing.Enabled == true)
            {
                traceStarted = Stopwatch.GetTimestamp();
            }

            using var commandScope = environment?.Telemetry.EnterCommand(command.Name);
            using var callbackScope = new SpellkitCallbackScope();
            var value = command.Invoke(new SpellkitCommandContext(ctx, command, args, callbackScope));
            ctx.Control?.Checkpoint();
            if (ctx.HasErrors)
            {
                return SpellkitNil.Instance;
            }

            return SpellkitHostRootTypeInfo.Wrap(ctx, value);
        }
        catch (SpellkitExecutionLimitException)
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
            if (traceStarted != 0)
            {
                environment!.Tracing.Write(
                    SpellkitTraceKind.HostCommand,
                    command.Name,
                    Stopwatch.GetElapsedTime(traceStarted));
            }
        }
    }

    protected override bool Equals(SpellkitFunction func) =>
        func is HostCommandFunction other && ReferenceEquals(command, other.command);

    private void ReportFailure(SpellkitHostEnvironment? environment, Exception exception)
    {
        if (environment is null)
        {
            return;
        }

        try
        {
            environment.Telemetry.Write(
                SpellkitLogLevel.Error,
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

    private static Par[] CreateParameters(IReadOnlyList<SpellkitCommandParameter> parameters)
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
