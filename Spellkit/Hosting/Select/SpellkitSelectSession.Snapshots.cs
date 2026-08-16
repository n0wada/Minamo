using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;

namespace Spellkit.Hosting;

public sealed partial class SpellkitSelectSession
{
    private SpellkitSelectResult WaitingResult()
    {
        return selectInstance.IsCompleted
            ? CompletedResult(selectInstance.Value)
            : new(GetSnapshot());
    }

    private SpellkitSelectResult CompletedResult(SpellkitObject value) =>
        new(GetSnapshot(), value);

    private SpellkitSelectSnapshot GetSnapshot()
    {
        if (nested is not null)
        {
            return nested.Snapshot;
        }

        if (selectInstance.IsCompleted)
        {
            return CreateSnapshot(Array.Empty<SpellkitChoice>());
        }

        var choices = GetChoices();
        if (nested is not null)
        {
            return nested.Snapshot;
        }

        return CreateSnapshot(
            selectInstance.IsCompleted ? Array.Empty<SpellkitChoice>() : choices);
    }

    private SpellkitSelectSnapshot RefreshCore(bool invalidate)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (invalidate)
            {
                revision.Advance();
            }

            return GetSnapshot();
        }
    }

    private SpellkitSelectSnapshot CreateSnapshot(IReadOnlyList<SpellkitChoice> choices)
    {
        var state = selectInstance.State;
        var stateView = selectInstance.IsCompleted
            ? null
            : CreateView(selectInstance.View(state));
        return new(
            selectInstance.Name,
            revision.Current,
            new SpellkitSelectState(state.Name, stateView),
            choices,
            selectInstance.IsCompleted);
    }
}
