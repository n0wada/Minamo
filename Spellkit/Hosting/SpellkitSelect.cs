using System.Collections.Generic;

namespace Spellkit.Hosting;

/// <summary>
/// Provides the synchronous, choice-oriented API for an interactive select.
/// Use <see cref="SpellkitSelectSession"/> when a host needs snapshots, revision checks,
/// invalidation, or asynchronous operations.
/// </summary>
public sealed class SpellkitSelect : IDisposable
{
    private readonly SpellkitSelectSession session;

    internal SpellkitSelect(SpellkitSelectSession session) =>
        this.session = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Gets the name of the current state.</summary>
    public string State => session.State;

    /// <summary>Gets the choices currently available to the host.</summary>
    public IReadOnlyList<SpellkitChoice> Choices => session.Choices;

    /// <summary>Gets whether the select has completed.</summary>
    public bool IsCompleted => session.IsCompleted;

    /// <summary>Executes a choice that does not accept an argument.</summary>
    public SpellkitSelectResult Select(string choiceId) => session.Select(choiceId);

    /// <summary>Executes a choice with its host-supplied argument.</summary>
    public SpellkitSelectResult Select(string choiceId, object? argument) =>
        session.Select(choiceId, argument);

    /// <summary>Sends a host event that does not accept an argument.</summary>
    public SpellkitSelectResult Send(string eventId) => session.Send(eventId);

    /// <summary>Sends a host event with its host-supplied argument.</summary>
    public SpellkitSelectResult Send(string eventId, object? argument) =>
        session.Send(eventId, argument);

    /// <summary>Cancels the select.</summary>
    public void Cancel() => session.Cancel();

    /// <inheritdoc/>
    public void Dispose() => session.Dispose();
}
