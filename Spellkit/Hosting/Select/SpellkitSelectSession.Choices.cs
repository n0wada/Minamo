using Spellkit.Compiler;
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
        SpellkitFunction? Action,
        SpellkitFunction? Guard,
        SpellkitObject[] BoundArguments,
        SelectInstance Owner)
    {
        internal int ParameterCount => Parameters.Count;
    }

    private sealed record ResolvedSelectEvent(
        SelectInstance Owner,
        SelectEventDefinition Handler);

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

        if (await RunExpandedEmptyHandlerAsync().ConfigureAwait(false))
        {
            return;
        }

        var choices = await GetAvailableChoicesAsync(selectInstance).ConfigureAwait(false);
        if (choices.Count == 0
            && selectInstance.ShouldRunEmpty
            && !HasExpandedEvents())
        {
            availableChoices = Array.Empty<ResolvedSelectChoice>();
            selectInstance.MarkEmptyTriggered();
            var empty = selectInstance.Empty()
                ?? throw new InvalidOperationException("The select empty handler is unavailable.");
            var result = await instance.InvokeSelectActionAsync(
                empty,
                Array.Empty<SpellkitObject>()).ConfigureAwait(false);
            await ApplyActionExecutionAsync(result).ConfigureAwait(false);

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
                revision.Current);
        }

        var publishedChoices = Array.AsReadOnly(visibleChoices);
        var publishedSnapshot = CreateSnapshot(publishedChoices);

        availableChoices = choices;
        snapshot = publishedSnapshot;
    }

    private async Task<IReadOnlyList<ResolvedSelectChoice>> GetAvailableChoicesAsync(
        SelectInstance owner)
    {
        var candidates = new List<ResolvedSelectChoice>();
        await AddAvailableChoicesAsync(owner, candidates).ConfigureAwait(false);

        if (ReferenceEquals(owner, selectInstance))
        {
            foreach (var spread in selectInstance.State.ChoiceSpreads)
            {
                var child = await GetExpandedSelectAsync(spread).ConfigureAwait(false);
                await AddAvailableChoicesAsync(child, candidates).ConfigureAwait(false);
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

    private async Task AddAvailableChoicesAsync(
        SelectInstance owner,
        List<ResolvedSelectChoice> candidates)
    {
        foreach (var choice in owner.State.Choices)
        {
            candidates.Add(new(
                choice.Name,
                choice.Label,
                choice.Parameters
                    .Select(parameter => new SpellkitChoiceParameter(
                        parameter.Name,
                        parameter.TypeName))
                    .ToArray(),
                owner.Choice(choice),
                owner.Guard(choice),
                Array.Empty<SpellkitObject>(),
                owner));
        }

        foreach (var group in owner.State.DynamicChoices)
        {
            var source = await instance.EvaluateSelectDynamicChoiceAsync(
                owner.DynamicChoiceSource(group),
                Array.Empty<SpellkitObject>()).ConfigureAwait(false);
            if (source is not IEnumerable<SpellkitObject> items)
            {
                throw new InvalidOperationException(
                    $"The dynamic choices in select state '{owner.State.Name}' must be a collection.");
            }

            foreach (var item in items)
            {
                SpellkitObject[] arguments = [item];
                foreach (var template in group.Choices)
                {
                    var id = RequireDynamicChoiceText(
                        await instance.EvaluateSelectDynamicChoiceAsync(
                            owner.DynamicChoiceId(template),
                            arguments).ConfigureAwait(false),
                        "ID");
                    var label = owner.DynamicChoiceLabel(template) is { } labelFunction
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
                        owner.DynamicChoiceAction(template),
                        owner.DynamicChoiceGuard(template),
                        arguments,
                        owner));
                }
            }
        }
    }

    private async Task<SelectInstance> GetExpandedSelectAsync(
        SelectChoiceSpreadDefinition spread)
    {
        if (expandedSelects.TryGetValue(spread, out var expanded))
        {
            return expanded;
        }

        var value = await instance.EvaluateSelectChoiceSpreadAsync(
            selectInstance.ChoiceSpreadSource(spread),
            Array.Empty<SpellkitObject>()).ConfigureAwait(false);
        if (value is not SpellkitSelectFactory factory)
        {
            throw new InvalidOperationException("A select choice spread must evaluate to a select.");
        }

        var child = instance.CreateSelectInstance(factory);
        if (child.State.Name.Length != 0)
        {
            throw new InvalidOperationException("A select choice spread must use a state-less select.");
        }
        expanded = child;
        expandedSelects.Add(spread, expanded);
        return expanded;
    }

    private async Task EnterCurrentStateAsync()
    {
        if (selectInstance.IsCompleted)
        {
            return;
        }

        await EnsureExpandedSelectsAsync().ConfigureAwait(false);
        await RunExpandedLifecycleHooksAsync(selectInstance.State, entering: true).ConfigureAwait(false);
        if (selectInstance.Enter(selectInstance.State) is { } enter)
        {
            await RunLifecycleHookAsync(enter).ConfigureAwait(false);
        }
    }

    private async Task EnsureExpandedSelectsAsync()
    {
        foreach (var spread in selectInstance.State.ChoiceSpreads)
        {
            _ = await GetExpandedSelectAsync(spread).ConfigureAwait(false);
        }
    }

    private async Task RunExpandedLifecycleHooksAsync(
        SelectStateDefinition state,
        bool entering)
    {
        foreach (var spread in state.ChoiceSpreads)
        {
            if (!expandedSelects.TryGetValue(spread, out var child))
            {
                continue;
            }

            var hook = entering
                ? child.Enter(child.State)
                : child.Leave(child.State);
            if (hook is not null)
            {
                await RunLifecycleHookAsync(hook).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> RunExpandedEmptyHandlerAsync()
    {
        foreach (var spread in selectInstance.State.ChoiceSpreads)
        {
            var child = await GetExpandedSelectAsync(spread).ConfigureAwait(false);
            var choices = await GetAvailableChoicesAsync(child).ConfigureAwait(false);
            if (choices.Count != 0 || !child.ShouldRunEmpty)
            {
                continue;
            }

            child.MarkEmptyTriggered();
            var empty = child.Empty()
                ?? throw new InvalidOperationException("The expanded select empty handler is unavailable.");
            var result = await instance.InvokeSelectActionAsync(
                empty,
                Array.Empty<SpellkitObject>()).ConfigureAwait(false);
            await ApplyActionExecutionAsync(result).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private bool HasExpandedEvents() => expandedSelects.Values.Any(child =>
        child.State.Events.Count != 0);

    private async Task<IReadOnlyList<ResolvedSelectEvent>> GetEventHandlersAsync(string eventId)
    {
        await EnsureExpandedSelectsAsync().ConfigureAwait(false);
        var handlers = new List<ResolvedSelectEvent>();
        foreach (var spread in selectInstance.State.ChoiceSpreads)
        {
            var child = await GetExpandedSelectAsync(spread).ConfigureAwait(false);
            foreach (var handler in child.State.Events.Where(candidate =>
                string.Equals(candidate.Name, eventId, StringComparison.Ordinal)))
            {
                handlers.Add(new(child, handler));
            }
        }

        foreach (var handler in selectInstance.State.Events.Where(candidate =>
            string.Equals(candidate.Name, eventId, StringComparison.Ordinal)))
        {
            handlers.Add(new(selectInstance, handler));
        }

        return handlers;
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

    private async Task<SpellkitSelectDescription?> CreateDescriptionAsync(
        SpellkitFunction? description,
        SpellkitObject[] arguments) =>
        description is null
            ? null
            : new SpellkitSelectDescription(
                await EvaluateDescriptionAsync(description, arguments).ConfigureAwait(false));

    private async Task<SpellkitDictionary> EvaluateDescriptionAsync(
        SpellkitFunction description,
        SpellkitObject[] arguments)
    {
        var value = await instance.EvaluateSelectDescriptionAsync(description, arguments).ConfigureAwait(false);
        return value as SpellkitDictionary
            ?? throw new InvalidOperationException("A select description must evaluate to a dictionary.");
    }

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
