using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Spellkit.Hosting;

public sealed partial class SpellkitSelectSession
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

    private IReadOnlyList<SpellkitChoice> GetChoices()
    {
        if (selectInstance.IsCompleted)
        {
            return Array.Empty<SpellkitChoice>();
        }

        var choices = GetVisibleChoices();
        if (choices.Count == 0
            && selectInstance.ShouldRunOtherwise
            && !otherwiseRunning)
        {
            selectInstance.MarkOtherwiseTriggered();
            otherwiseRunning = true;
            try
            {
                var otherwise = selectInstance.Otherwise()
                    ?? throw new InvalidOperationException("The select otherwise handler is unavailable.");
                var result = instance.InvokeSelectAction(
                    otherwise,
                    Array.Empty<SpellkitObject>());
                return ApplyActionExecution(result).Choices;
            }
            finally
            {
                otherwiseRunning = false;
            }
        }

        return choices;
    }

    private IReadOnlyList<SpellkitChoice> GetVisibleChoices() =>
        GetAvailableChoices()
            .Select(choice => new SpellkitChoice(
                choice.Id,
                choice.Parameters,
                choice.Label,
                CreateView(choice.View, choice.BoundArguments)))
            .ToArray();

    private IReadOnlyList<ResolvedSelectChoice> GetAvailableChoices()
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
            var source = instance.EvaluateSelectDynamicChoice(
                selectInstance.DynamicChoiceSource(group),
                Array.Empty<SpellkitObject>());
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
                        selectInstance.DynamicChoiceId(template),
                        arguments,
                        "ID");
                    var label = selectInstance.DynamicChoiceLabel(template) is { } labelFunction
                        ? EvaluateDynamicChoiceText(labelFunction, arguments, "label") ?? id
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

        return candidates.Where(IsAvailable).ToArray();
    }

    private string RequireDynamicChoiceText(
        SpellkitFunction function,
        SpellkitObject[] arguments,
        string part)
    {
        var value = EvaluateDynamicChoiceText(function, arguments, part);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"A dynamic select choice {part} in state '{selectInstance.State.Name}' cannot be empty.");
        }

        return value;
    }

    private string? EvaluateDynamicChoiceText(
        SpellkitFunction function,
        SpellkitObject[] arguments,
        string part) =>
        SpellkitHostValueConverter.Convert<string>(
            instance.EvaluateSelectDynamicChoice(function, arguments),
            $"Dynamic select choice {part}");

    private SpellkitSelectView? CreateView(SpellkitFunction? view) =>
        CreateView(view, Array.Empty<SpellkitObject>());

    private SpellkitSelectView? CreateView(
        SpellkitFunction? view,
        SpellkitObject[] arguments) =>
        view is null
            ? null
            : new SpellkitSelectView(
                instance.EvaluateSelectView(view, arguments));

    private bool IsAvailable(ResolvedSelectChoice choice) =>
        choice.Guard is null
        || instance.EvaluateSelectGuard(choice.Guard, choice.BoundArguments);

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
