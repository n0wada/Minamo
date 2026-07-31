using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

internal static class SpellkitResourceDefinition
{
    internal static HostResourceDefinition Create<T>() where T : SpellkitResource
    {
        var type = typeof(T);
        if (type.IsAbstract)
        {
            throw new InvalidOperationException(
                $"Resource type '{type.FullName}' must be concrete.");
        }

        var resource = type.GetCustomAttribute<SpellkitResourceAttribute>(inherit: false)
            ?? throw new InvalidOperationException(
                $"Resource type '{type.FullName}' requires SpellkitResourceAttribute.");
        HostNames.ValidateDottedName(resource.Name, nameof(resource.Name), "resource type");

        var methods = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Command:
                method.GetCustomAttribute<SpellkitCommandAttribute>(inherit: false)))
            .Where(entry => entry.Command is not null)
            .Select(entry => CreateCommand(entry.Method, entry.Command!))
            .ToArray();

        var duplicate = methods
            .GroupBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Resource command '{duplicate.Key}' is registered more than once on '{type.FullName}'.");
        }

        var catalog = methods.Select(method => method.CatalogDescriptor()).ToArray();
        return new(
            type,
            resource.Name,
            resource.Lifetime,
            catalog,
            instance => methods.Select(method => method.Bind(instance)).ToArray());
    }

    private static ResourceMethod CreateCommand(
        MethodInfo method,
        SpellkitCommandAttribute command)
    {
        var name = command.Name ?? method.Name;
        HostNames.ValidateIdentifier(name, nameof(command.Name), "resource command");
        HostNames.ValidateCapability(command.Capability, nameof(command.Capability), optional: true);

        if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
        {
            throw Invalid(method, "generic methods are not supported");
        }

        if (method.ReturnType.IsByRef)
        {
            throw Invalid(method, "by-reference return values are not supported");
        }

        var contextCount = 0;
        var exposed = new List<SpellkitCommandParameter>();
        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType.IsByRef || parameter.IsOut || parameter.IsIn)
            {
                throw Invalid(method, "ref, in, and out parameters are not supported");
            }

            if (parameter.GetCustomAttribute<ParamArrayAttribute>() is not null)
            {
                throw Invalid(method, "params arrays are not supported");
            }

            if (parameter.ParameterType == typeof(SpellkitCommandContext))
            {
                contextCount++;
                if (contextCount > 1 || parameter.HasDefaultValue)
                {
                    throw Invalid(
                        method,
                        "only one non-optional SpellkitCommandContext parameter is allowed");
                }

                continue;
            }

            var parameterName = parameter.Name
                ?? throw Invalid(method, "all parameters require names");
            exposed.Add(new(
                parameterName,
                parameter.ParameterType,
                parameter.HasDefaultValue,
                parameter.HasDefaultValue ? parameter.DefaultValue : null));
        }

        return new(
            method,
            name,
            command.Description,
            command.Capability,
            exposed);
    }

    private static InvalidOperationException Invalid(MethodInfo method, string reason) =>
        new($"Method '{method.DeclaringType?.FullName}.{method.Name}' cannot be exposed "
            + $"as a resource command: {reason}.");

    private sealed record ResourceMethod(
        MethodInfo Method,
        string Name,
        string? Description,
        string? Capability,
        IReadOnlyList<SpellkitCommandParameter> Parameters)
    {
        internal SpellkitCommandDescriptor CatalogDescriptor() =>
            new(
                Name,
                Description,
                Capability,
                Parameters,
                (SpellkitCommandHandler)(_ => SpellkitNil.Instance));

        internal SpellkitCommandDescriptor Bind(SpellkitResource resource) =>
            new(
                Name,
                Description,
                Capability,
                Parameters,
                context => Invoke(resource, context));

        private SpellkitObject Invoke(SpellkitResource resource, SpellkitCommandContext context)
        {
            var parameters = Method.GetParameters();
            var arguments = new object?[parameters.Length];
            var argumentIndex = 0;
            for (var i = 0; i < parameters.Length; i++)
            {
                arguments[i] = parameters[i].ParameterType == typeof(SpellkitCommandContext)
                    ? context
                    : context.Argument(argumentIndex++, parameters[i].ParameterType);
            }

            if (context.ExecutionContext.HasErrors)
            {
                return SpellkitNil.Instance;
            }

            object? result;
            try
            {
                result = Method.Invoke(resource, arguments);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException is { } inner)
            {
                ExceptionDispatchInfo.Capture(inner).Throw();
                throw;
            }

            if (Method.ReturnType == typeof(void))
            {
                return SpellkitNil.Instance;
            }
            if (result is Task task)
            {
                return Method.ReturnType.IsGenericType
                    ? SpellkitCommandConvert.FromAwaitable(
                        task,
                        Method.ReturnType.GetGenericArguments()[0])
                    : SpellkitCommandConvert.FromAwaitable(task);
            }
            if (Method.ReturnType == typeof(ValueTask))
            {
                return SpellkitCommandConvert.FromAwaitable((ValueTask)result!);
            }
            if (Method.ReturnType.IsGenericType
                && Method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                var asTask = Method.ReturnType.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(result, null)!;
                return SpellkitCommandConvert.FromAwaitable(
                    (Task)asTask,
                    Method.ReturnType.GetGenericArguments()[0]);
            }

            return SpellkitCommandConvert.FromObject(result, Method.ReturnType);
        }
    }
}
