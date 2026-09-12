using Minamo.Compiler;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Minamo.Hosting;

internal sealed partial class MinamoSelectSession
{
    private sealed record ResolvedSelectChoice(
        string Id,
        IReadOnlyList<MinamoChoiceParameter> Parameters,
        MinamoFunction? Action,
        MinamoSelectMetadata? Metadata)
    {
        internal int ParameterCount => Parameters.Count;
    }

    private async Task PublishSnapshotAsync()
    {
        while (true)
        {
            if (selectInstance.IsCompleted)
            {
                PublishCompletedSnapshot();
                return;
            }

            var choices = await GetAvailableChoicesAsync(selectInstance).ConfigureAwait(false);
            if (choices.Count == 0
                && selectInstance.Events.Count == 0)
            {
                if (ReturnFromCurrentSelect())
                {
                    continue;
                }

                PublishCompletedSnapshot();
                return;
            }

            var visibleChoices = new MinamoChoice[choices.Count];
            for (var i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                visibleChoices[i] = new MinamoChoice(
                    choice.Id,
                    choice.Parameters,
                    choice.Metadata);
            }

            var properties = await GetPropertiesAsync(selectInstance).ConfigureAwait(false);
            var publishedChoices = Array.AsReadOnly(visibleChoices);
            var publishedSnapshot = CreateSnapshot(properties, publishedChoices);

            availableChoices = choices;
            snapshot = publishedSnapshot;
            return;
        }
    }

    private async Task<IReadOnlyList<ResolvedSelectChoice>> GetAvailableChoicesAsync(
        SelectInstance owner)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var available = new List<ResolvedSelectChoice>(owner.Choices.Count);
        foreach (var choice in owner.Choices)
        {
            if (!ids.Add(choice.Name))
            {
                throw new InvalidOperationException(
                    $"The select '{selectInstance.Name}' generated duplicate case ID '{choice.Name}'.");
            }

            var guard = owner.Guard(choice);
            if (guard is null
                || await instance.EvaluateSelectGuardAsync(
                    guard,
                    Array.Empty<MinamoObject>()).ConfigureAwait(false))
            {
                available.Add(new(
                    choice.Name,
                    choice.Parameters
                        .Select(parameter => new MinamoChoiceParameter(
                            parameter.Name,
                            parameter.TypeName))
                        .ToArray(),
                    owner.Choice(choice),
                    await CreateMetadataAsync(owner.ChoiceMetadata(choice)).ConfigureAwait(false)));
            }
        }

        return available;
    }

    private async Task<IReadOnlyList<MinamoSelectProperty>> GetPropertiesAsync(
        SelectInstance owner)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var properties = new MinamoSelectProperty[owner.Properties.Count];
        for (var i = 0; i < owner.Properties.Count; i++)
        {
            var property = owner.Properties[i];
            if (!names.Add(property.Name))
            {
                throw new InvalidOperationException(
                    $"The select '{owner.Name}' generated duplicate property name '{property.Name}'.");
            }

            var value = await instance.EvaluateSelectPropertyAsync(
                owner.Property(property)).ConfigureAwait(false);
            var metadata = await CreateMetadataAsync(
                owner.PropertyMetadata(property)).ConfigureAwait(false);
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
        MinamoObject[] arguments) =>
        description is null
            ? null
            : new MinamoSelectDescription(
                await EvaluateDescriptionAsync(description, arguments).ConfigureAwait(false));

    private async Task<MinamoDictionary> EvaluateDescriptionAsync(
        MinamoFunction description,
        MinamoObject[] arguments)
    {
        var value = await instance.EvaluateSelectDescriptionAsync(description, arguments).ConfigureAwait(false);
        return value as MinamoDictionary
            ?? throw new InvalidOperationException("A select description must evaluate to a dictionary.");
    }

    private async Task<MinamoSelectMetadata?> CreateMetadataAsync(MinamoFunction? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var value = await instance.EvaluateSelectMetadataAsync(metadata).ConfigureAwait(false);
        return value is MinamoDictionary dictionary
            ? new MinamoSelectMetadata(dictionary)
            : throw new InvalidOperationException("Select metadata must evaluate to a dictionary.");
    }

    private static MinamoObject[] ConvertArguments(
        ResolvedSelectChoice choice,
        object? argument,
        bool hasArgument) =>
        ConvertArguments(choice.Id, choice.ParameterCount, "Case", argument, hasArgument);

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
