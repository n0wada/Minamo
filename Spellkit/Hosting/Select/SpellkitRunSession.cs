using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

public sealed class SpellkitRunSession : IDisposable
{
    private readonly SpellkitInstance instance;
    private SpellkitSelectSession? select;
    private SpellkitMachine.VmContinuation? continuation;
    private SpellkitObject? value;
    private Exception? failure;
    private bool completed;
    private bool disposed;

    internal SpellkitRunSession(SpellkitInstance instance, ExecutionResult result) =>
        (this.instance, continuation) = (instance, result.Continuation);

    internal SpellkitRunSession(SpellkitInstance instance, Exception failure) =>
        (this.instance, this.failure, completed) = (instance, failure, true);

    public bool IsCompleted => completed;

    public bool IsWaitingForSelect => select is not null && !completed;

    public Exception? Failure => failure;

    /// <summary>Gets the name of the state currently waiting for host input.</summary>
    public string? State
    {
        get
        {
            ThrowIfDisposed();
            return select?.State;
        }
    }

    /// <summary>Gets the dictionary metadata declared for the select currently waiting for host input.</summary>
    public SpellkitSelectDescription? Description
    {
        get
        {
            ThrowIfDisposed();
            return select?.Description;
        }
    }

    /// <summary>Gets the revision of the select currently waiting for host input.</summary>
    public long? Revision
    {
        get
        {
            ThrowIfDisposed();
            return select?.Revision;
        }
    }

    public IReadOnlyList<SpellkitChoice> Choices
    {
        get
        {
            ThrowIfDisposed();
            return select?.Choices ?? Array.Empty<SpellkitChoice>();
        }
    }

    public Task<SpellkitSelectResult> SelectAsync(string choiceId) =>
        instance.SelectAsync(this, choiceId, null, hasArgument: false);

    public Task<SpellkitSelectResult> SelectAsync(string choiceId, object? argument) =>
        instance.SelectAsync(this, choiceId, argument, hasArgument: true);

    /// <summary>Executes a choice from the currently published screen.</summary>
    public Task<SpellkitSelectResult> SelectAsync(SpellkitChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return instance.SelectAtRevisionAsync(this, choice.Id, null, hasArgument: false, revision: choice.Revision);
    }

    /// <summary>Executes a choice with its host-supplied argument from the published screen.</summary>
    public Task<SpellkitSelectResult> SelectAsync(SpellkitChoice choice, object? argument)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return instance.SelectAtRevisionAsync(this, choice.Id, argument, hasArgument: true, revision: choice.Revision);
    }

    /// <summary>Executes a choice only if it belongs to the specified published screen.</summary>
    public Task<SpellkitSelectResult> SelectAtRevisionAsync(string choiceId, long revision) =>
        instance.SelectAtRevisionAsync(this, choiceId, null, hasArgument: false, revision: revision);

    /// <summary>Executes a choice with its host-supplied argument only for the specified screen.</summary>
    public Task<SpellkitSelectResult> SelectAtRevisionAsync(string choiceId, object? argument, long revision) =>
        instance.SelectAtRevisionAsync(this, choiceId, argument, hasArgument: true, revision: revision);

    public Task<SpellkitSelectResult> SendAsync(string eventId) =>
        instance.SendAsync(this, eventId, null, hasArgument: false);

    public Task<SpellkitSelectResult> SendAsync(string eventId, object? argument) =>
        instance.SendAsync(this, eventId, argument, hasArgument: true);

    /// <summary>Sends a host event only if it belongs to the specified published screen.</summary>
    public Task<SpellkitSelectResult> SendAtRevisionAsync(string eventId, long revision) =>
        instance.SendAtRevisionAsync(this, eventId, null, hasArgument: false, revision: revision);

    /// <summary>Sends a host event with its host-supplied argument only for the specified screen.</summary>
    public Task<SpellkitSelectResult> SendAtRevisionAsync(string eventId, object? argument, long revision) =>
        instance.SendAtRevisionAsync(this, eventId, argument, hasArgument: true, revision: revision);

    /// <summary>Reevaluates and republishes the current select without invalidating it.</summary>
    public async Task RefreshAsync() =>
        await instance.RefreshSelectAsync(this, invalidate: false).ConfigureAwait(false);

    /// <summary>Invalidates earlier select screens, then reevaluates and republishes the current select.</summary>
    public async Task InvalidateAsync() =>
        await instance.RefreshSelectAsync(this, invalidate: true).ConfigureAwait(false);

    public T? GetValue<T>() => SpellkitHostValueConverter.Convert<T>(value, "Run result");

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        instance.Cancel(this);
        disposed = true;
    }

    internal SpellkitSelectSession GetSelect()
    {
        ThrowIfDisposed();
        return select ?? throw new InvalidOperationException("The script is not waiting for a select.");
    }

    internal SpellkitMachine.VmContinuation GetContinuation() =>
        continuation ?? throw new InvalidOperationException("The script has no suspended VM continuation.");

    internal async Task AdvanceAsync(ExecutionResult result)
    {
        select?.Dispose();
        select = null;

        while (true)
        {
            if (result.Reason is TerminationReason.Complete)
            {
                completed = true;
                continuation = null;
                value = result.Value;
                return;
            }

            if (result.Reason is TerminationReason.Suspended
                && result.Continuation is not null
                && result.Suspension is { Select: not null } suspension)
            {
                continuation = result.Continuation;
                select = await instance.CreateSelectSessionAsync(
                    suspension.Select).ConfigureAwait(false);
                if (!select.IsCompleted)
                {
                    return;
                }

                var selectValue = select.CompletionValue;
                select.Dispose();
                select = null;
                result = await instance.ResumeSelectContinuationAsync(
                    continuation,
                    selectValue).ConfigureAwait(false);
                continue;
            }

            throw new InvalidOperationException("The VM suspended without a select request.");
        }
    }

    internal void Fail(Exception exception)
    {
        failure = exception;
        completed = true;
        continuation = null;
        select?.Dispose();
        select = null;
    }

    internal void Cancel()
    {
        completed = true;
        continuation = null;
        select?.Dispose();
        select = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
