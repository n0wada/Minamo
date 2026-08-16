using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

internal sealed partial class SpellkitSelectSession
{
    private sealed record ResolvedSelectChoice(
        string Id,
        string Label,
        IReadOnlyList<SpellkitChoiceParameter> Parameters,
        SpellkitFunction Action,
        SpellkitFunction? Guard,
        SpellkitFunction? View,
        SpellkitObject[] BoundArguments)
    {
        internal int ParameterCount => Parameters.Count;
    }

    private async Task PublishSnapshotAsync()
    {
        if (nested is not null)
        {
            await nested.PublishSnapshotAsync().ConfigureAwait(false);
            return;
        }

        if (selectInstance.IsCompleted)
        {
            PublishCompletedSnapshot();
            return;
        }

        var choices = await GetAvailableChoicesAsync().ConfigureAwait(false);
        if (choices.Count == 0
            && selectInstance.ShouldRunOtherwise
            && !otherwiseRunning)
        {
            availableChoices = Array.Empty<ResolvedSelectChoice>();
            selectInstance.MarkOtherwiseTriggered();
            otherwiseRunning = true;
            try
            {
                var otherwise = selectInstance.Otherwise()
                    ?? throw new InvalidOperationException("The select otherwise handler is unavailable.");
                var result = await instance.InvokeSelectActionAsync(
                    otherwise,
                    Array.Empty<SpellkitObject>()).ConfigureAwait(false);
                await ApplyActionExecutionAsync(result).ConfigureAwait(false);
            }
            finally
            {
                otherwiseRunning = false;
            }

            return;
        }

        var visibleChoices = new SpellkitChoice[choices.Count];
        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            visibleChoices[i] = new SpellkitChoice(
                choice.Id,
                choice.Parameters,
                choice.Label,
                await CreateViewAsync(choice.View, choice.BoundArguments).ConfigureAwait(false),
                revision.Current);
        }

        var state = selectInstance.State;
        var stateView = await CreateViewAsync(
            selectInstance.View(state),
            Array.Empty<SpellkitObject>()).ConfigureAwait(false);
        var publishedChoices = Array.AsReadOnly(visibleChoices);
        var publishedSnapshot = CreateSnapshot(stateView, publishedChoices);

        availableChoices = choices;
        snapshot = publishedSnapshot;
    }

    private async Task<IReadOnlyList<ResolvedSelectChoice>> GetAvailableChoicesAsync()
    {
        var candidates = new List<ResolvedSelectChoice>();
        foreach (var choice in selectInstance.State.Choices)
        {
            candidates.Add(new(
                choice.Name,
                choice.Label,
                choice.Parameters
                    .Select(parameter => new SpellkitChoiceParameter(
                        parameter.Name,
                        parameter.TypeName))
                    .ToArray(),
                selectInstance.Choice(choice),
                selectInstance.Guard(choice),
                selectInstance.View(choice),
                Array.Empty<SpellkitObject>()));
        }

        foreach (var group in selectInstance.State.DynamicChoices)
        {
            var source = await instance.EvaluateSelectDynamicChoiceAsync(
                selectInstance.DynamicChoiceSource(group),
                Array.Empty<SpellkitObject>()).ConfigureAwait(false);
            if (source is not IEnumerable<SpellkitObject> items)
            {
                throw new InvalidOperationException(
                    $"The dynamic choices in select state '{selectInstance.State.Name}' must be a collection.");
            }

            foreach (var item in items)
            {
                SpellkitObject[] arguments = [item];
                foreach (var template in group.Choices)
                {
                    var id = RequireDynamicChoiceText(
                        await instance.EvaluateSelectDynamicChoiceAsync(
                            selectInstance.DynamicChoiceId(template),
                            arguments).ConfigureAwait(false),
                        "ID");
                    var label = selectInstance.DynamicChoiceLabel(template) is { } labelFunction
                        ? EvaluateDynamicChoiceText(
                            await instance.EvaluateSelectDynamicChoiceAsync(
                                labelFunction,
                                arguments).ConfigureAwait(false),
                            "label") ?? id
                        : id;
                    candidates.Add(new(
                        id,
                        label,
                        Array.Empty<SpellkitChoiceParameter>(),
                        selectInstance.DynamicChoiceAction(template),
                        selectInstance.DynamicChoiceGuard(template),
                        selectInstance.DynamicChoiceView(template),
                        arguments));
                }
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!ids.Add(candidate.Id))
            {
                throw new InvalidOperationException(
                    $"The select state '{selectInstance.State.Name}' generated duplicate choice ID '{candidate.Id}'.");
            }
        }

        var available = new List<ResolvedSelectChoice>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (candidate.Guard is null
                || await instance.EvaluateSelectGuardAsync(
                    candidate.Guard,
                    candidate.BoundArguments).ConfigureAwait(false))
            {
                available.Add(candidate);
            }
        }

        return available;
    }

    private static string RequireDynamicChoiceText(SpellkitObject value, string part)
    {
        var text = EvaluateDynamicChoiceText(value, part);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"A dynamic select choice {part} in state cannot be empty.");
        }

        return text;
    }

    private static string? EvaluateDynamicChoiceText(SpellkitObject value, string part) =>
        SpellkitHostValueConverter.Convert<string>(value, $"Dynamic select choice {part}");

    private async Task<SpellkitSelectView?> CreateViewAsync(
        SpellkitFunction? view,
        SpellkitObject[] arguments) =>
        view is null
            ? null
            : new SpellkitSelectView(
                await instance.EvaluateSelectViewAsync(view, arguments).ConfigureAwait(false));

    private static SpellkitObject[] ConvertArguments(
        ResolvedSelectChoice choice,
        object? argument,
        bool hasArgument) =>
        ConvertArguments(choice.Id, choice.ParameterCount, "Choice", argument, hasArgument);

    private static SpellkitObject[] AddArguments(
        IReadOnlyList<SpellkitObject> boundArguments,
        IReadOnlyList<SpellkitObject> arguments)
    {
        var result = new SpellkitObject[boundArguments.Count + arguments.Count];
        for (var i = 0; i < boundArguments.Count; i++)
        {
            result[i] = boundArguments[i];
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            result[boundArguments.Count + i] = arguments[i];
        }

        return result;
    }

    private static SpellkitObject[] ConvertArguments(
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

            return Array.Empty<SpellkitObject>();
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

        var values = new SpellkitObject[tuple.Length];
        for (var i = 0; i < tuple.Length; i++)
        {
            values[i] = TypeConverter.ConvertFrom(tuple[i]);
        }
        return values;
    }
}
