using Minamo.Compiler;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Minamo.Hosting;

internal sealed partial class MinamoSelectSession
{
    private sealed record ResolvedSelectAction(
        string Id,
        IReadOnlyList<MinamoSelectActionParameter> Parameters,
        MinamoFunction? Handler,
        MinamoSelectMetadata? Metadata)
    {
        internal int ParameterCount => Parameters.Count;
    }

    private async Task PublishSnapshotAsync(
        CancellationToken cancellationToken,
        ExecutionControl? control = null)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selectInstance.IsCompleted)
            {
                PublishCompletedSnapshot();
                return;
            }

            var actions = await GetAvailableActionsAsync(
                selectInstance,
                cancellationToken,
                control).ConfigureAwait(false);
            if (actions.Count == 0
                && selectInstance.Events.Count == 0)
            {
                if (ReturnFromCurrentSelect())
                {
                    continue;
                }

                PublishCompletedSnapshot();
                return;
            }

            var visibleActions = new MinamoSelectAction[actions.Count];
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                visibleActions[i] = new MinamoSelectAction(
                    action.Id,
                    action.Parameters,
                    action.Metadata);
            }

            var properties = await GetPropertiesAsync(
                selectInstance,
                cancellationToken,
                control).ConfigureAwait(false);
            var publishedActions = Array.AsReadOnly(visibleActions);
            var publishedSnapshot = CreateSnapshot(properties, publishedActions);

            availableActions = actions;
            snapshot = publishedSnapshot;
            return;
        }
    }

    private async Task<IReadOnlyList<ResolvedSelectAction>> GetAvailableActionsAsync(
        SelectInstance owner,
        CancellationToken cancellationToken,
        ExecutionControl? control)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var available = new List<ResolvedSelectAction>(owner.Actions.Count);
        foreach (var action in owner.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ids.Add(action.Name))
            {
                throw new InvalidOperationException(
                    $"The select '{selectInstance.Name}' generated duplicate action ID '{action.Name}'.");
            }

            var guard = owner.Guard(action);
            if (guard is null
                || await instance.EvaluateSelectGuardAsync(
                    guard,
                    Array.Empty<MinamoObject>(),
                    cancellationToken,
                    control).ConfigureAwait(false))
            {
                available.Add(new(
                    action.Name,
                    action.Parameters
                        .Select(parameter => new MinamoSelectActionParameter(
                            parameter.Name,
                            parameter.TypeName))
                        .ToArray(),
                    owner.Handler(action),
                    await CreateMetadataAsync(
                        owner.ActionMetadata(action),
                        cancellationToken,
                        control).ConfigureAwait(false)));
            }
        }

        return available;
    }

    private async Task<IReadOnlyList<MinamoSelectProperty>> GetPropertiesAsync(
        SelectInstance owner,
        CancellationToken cancellationToken,
        ExecutionControl? control)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var properties = new MinamoSelectProperty[owner.Properties.Count];
        for (var i = 0; i < owner.Properties.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var property = owner.Properties[i];
            if (!names.Add(property.Name))
            {
                throw new InvalidOperationException(
                    $"The select '{owner.Name}' generated duplicate property name '{property.Name}'.");
            }

            var value = await instance.EvaluateSelectPropertyAsync(
                owner.Property(property),
                cancellationToken,
                control).ConfigureAwait(false);
            var metadata = await CreateMetadataAsync(
                owner.PropertyMetadata(property),
                cancellationToken,
                control).ConfigureAwait(false);
            properties[i] = new(property.Name, value, metadata);
        }

        return Array.AsReadOnly(properties);
    }

    private IReadOnlyList<SelectEventDefinition> GetEventHandlers(string eventId) =>
        selectInstance.Events
            .Where(candidate => string.Equals(candidate.Name, eventId, StringComparison.Ordinal))
            .ToArray();

    private async Task<MinamoSelectDescription?> CreateDescriptionAsync(
        MinamoFunction? description,
        MinamoObject[] arguments,
        CancellationToken cancellationToken,
        ExecutionControl? control = null) =>
        description is null
            ? null
            : new MinamoSelectDescription(
                await EvaluateDescriptionAsync(
                    description,
                    arguments,
                    cancellationToken,
                    control).ConfigureAwait(false));

    private async Task<MinamoDictionary> EvaluateDescriptionAsync(
        MinamoFunction description,
        MinamoObject[] arguments,
        CancellationToken cancellationToken,
        ExecutionControl? control)
    {
        var value = await instance.EvaluateSelectDescriptionAsync(
            description,
            arguments,
            cancellationToken,
            control).ConfigureAwait(false);
        return value as MinamoDictionary
            ?? throw new InvalidOperationException("A select description must evaluate to a dictionary.");
    }

    private async Task<MinamoSelectMetadata?> CreateMetadataAsync(
        MinamoFunction? metadata,
        CancellationToken cancellationToken,
        ExecutionControl? control)
    {
        if (metadata is null)
        {
            return null;
        }

        var value = await instance.EvaluateSelectMetadataAsync(
            metadata,
            cancellationToken,
            control).ConfigureAwait(false);
        return value is MinamoDictionary dictionary
            ? new MinamoSelectMetadata(dictionary)
            : throw new InvalidOperationException("Select metadata must evaluate to a dictionary.");
    }

    private static MinamoObject[] ConvertArguments(
        ResolvedSelectAction action,
        object? argument,
        bool hasArgument) =>
        ConvertArguments(action.Id, action.ParameterCount, "Action", argument, hasArgument);

    private static MinamoObject[] ConvertArguments(
        string name,
        int parameterCount,
        string actionKind,
        object? argument,
        bool hasArgument)
    {
        if (parameterCount == 0)
        {
            if (hasArgument)
            {
                throw new ArgumentException($"{actionKind} '{name}' does not accept an argument.", nameof(argument));
            }

            return Array.Empty<MinamoObject>();
        }

        if (!hasArgument)
        {
            throw new ArgumentException($"{actionKind} '{name}' requires an argument.", nameof(argument));
        }

        if (parameterCount == 1)
        {
            return [TypeConverter.ConvertFrom(argument)];
        }

        if (argument is not ITuple tuple || tuple.Length != parameterCount)
        {
            throw new ArgumentException(
                $"{actionKind} '{name}' requires one tuple with {parameterCount} elements.",
                nameof(argument));
        }

        var values = new MinamoObject[tuple.Length];
        for (var i = 0; i < tuple.Length; i++)
        {
            values[i] = TypeConverter.ConvertFrom(tuple[i]);
        }
        return values;
    }
}
