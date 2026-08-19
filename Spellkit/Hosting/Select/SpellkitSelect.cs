using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

/// <summary>
/// Provides the choice-oriented API for an interactive select.
/// The current state and choices are published asynchronously and remain safe to read until the
/// next action, refresh, or invalidation.
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

    /// <summary>Gets the dictionary metadata declared for this select.</summary>
    public SpellkitSelectDescription? Description => session.Description;

    /// <summary>Gets the revision of the currently published select screen.</summary>
    public long Revision => session.Revision;

    /// <summary>Gets the choices currently available to the host.</summary>
    public IReadOnlyList<SpellkitChoice> Choices => session.Choices;

    /// <summary>Gets whether the select has completed.</summary>
    public bool IsCompleted => session.IsCompleted;

    /// <summary>Asynchronously executes a choice that does not accept an argument.</summary>
    public Task<SpellkitSelectResult> SelectAsync(string choiceId) => session.SelectAsync(choiceId);

    /// <summary>Asynchronously executes a choice with its host-supplied argument.</summary>
    public Task<SpellkitSelectResult> SelectAsync(string choiceId, object? argument) =>
        session.SelectAsync(choiceId, argument);

    /// <summary>Executes a choice from the currently published screen.</summary>
    public Task<SpellkitSelectResult> SelectAsync(SpellkitChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return session.SelectAtRevisionAsync(choice.Id, choice.Revision);
    }

    /// <summary>Executes a choice with its host-supplied argument from the published screen.</summary>
    public Task<SpellkitSelectResult> SelectAsync(SpellkitChoice choice, object? argument)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return session.SelectAtRevisionAsync(choice.Id, argument, choice.Revision);
    }

    /// <summary>Executes a choice only if it belongs to the specified published screen.</summary>
    public Task<SpellkitSelectResult> SelectAtRevisionAsync(string choiceId, long revision) =>
        session.SelectAtRevisionAsync(choiceId, revision);

    /// <summary>Executes a choice with its host-supplied argument only for the specified screen.</summary>
    public Task<SpellkitSelectResult> SelectAtRevisionAsync(string choiceId, object? argument, long revision) =>
        session.SelectAtRevisionAsync(choiceId, argument, revision);

    /// <summary>Asynchronously sends a host event that does not accept an argument.</summary>
    public Task<SpellkitSelectResult> SendAsync(string eventId) => session.SendAsync(eventId);

    /// <summary>Asynchronously sends a host event with its host-supplied argument.</summary>
    public Task<SpellkitSelectResult> SendAsync(string eventId, object? argument) =>
        session.SendAsync(eventId, argument);

    /// <summary>Sends a host event only if it belongs to the specified published screen.</summary>
    public Task<SpellkitSelectResult> SendAtRevisionAsync(string eventId, long revision) =>
        session.SendAtRevisionAsync(eventId, revision);

    /// <summary>Sends a host event with its host-supplied argument only for the specified screen.</summary>
    public Task<SpellkitSelectResult> SendAtRevisionAsync(string eventId, object? argument, long revision) =>
        session.SendAtRevisionAsync(eventId, argument, revision);

    /// <summary>Reevaluates and republishes the current screen without invalidating it.</summary>
    public Task RefreshAsync() => session.RefreshAsync();

    /// <summary>Invalidates earlier screens, then reevaluates and republishes the current screen.</summary>
    public Task InvalidateAsync() => session.InvalidateAsync();

    /// <summary>Cancels the select.</summary>
    public void Cancel() => session.Cancel();

    /// <inheritdoc/>
    public void Dispose() => session.Dispose();
}
