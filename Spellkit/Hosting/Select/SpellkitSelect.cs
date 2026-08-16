using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

/// <summary>
/// Provides the choice-oriented API for an interactive select.
/// Use <see cref="SpellkitSelectSession"/> when a host needs snapshots, revision checks,
/// or invalidation.
/// </summary>
public sealed class SpellkitSelect : IDisposable
{
    private readonly SpellkitSelectSession session;

    internal SpellkitSelect(SpellkitSelectSession session) =>
        this.session = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Gets the declared name of this select.</summary>
    public string Name => session.Name;

    /// <summary>Gets the name of the current state.</summary>
    public string State => session.State;

    /// <summary>Gets the choices currently available to the host.</summary>
    public IReadOnlyList<SpellkitChoice> Choices => session.Choices;

    /// <summary>Gets whether the select has completed.</summary>
    public bool IsCompleted => session.IsCompleted;

    /// <summary>Asynchronously executes a choice that does not accept an argument.</summary>
    public Task<SpellkitSelectResult> SelectAsync(string choiceId) => session.SelectAsync(choiceId);

    /// <summary>Asynchronously executes a choice with its host-supplied argument.</summary>
    public Task<SpellkitSelectResult> SelectAsync(string choiceId, object? argument) =>
        session.SelectAsync(choiceId, argument);

    /// <summary>Asynchronously sends a host event that does not accept an argument.</summary>
    public Task<SpellkitSelectResult> SendAsync(string eventId) => session.SendAsync(eventId);

    /// <summary>Asynchronously sends a host event with its host-supplied argument.</summary>
    public Task<SpellkitSelectResult> SendAsync(string eventId, object? argument) =>
        session.SendAsync(eventId, argument);

    /// <summary>Cancels the select.</summary>
    public void Cancel() => session.Cancel();

    /// <inheritdoc/>
    public void Dispose() => session.Dispose();
}
