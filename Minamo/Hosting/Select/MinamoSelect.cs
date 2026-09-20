using System.Collections.Generic;
using System.Threading.Tasks;

namespace Minamo.Hosting;

/// <summary>
/// Provides the action-oriented API for an interactive select.
/// The current actions are published asynchronously and remain safe to read until the
/// next action.
/// </summary>
public sealed class MinamoSelect : IDisposable
{
    private readonly MinamoSelectSession session;

    internal MinamoSelect(MinamoSelectSession session) =>
        this.session = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Gets the declared name of the currently active select.</summary>
    public string Name => session.Name;

    /// <summary>Gets the free-form description declared for the currently active select.</summary>
    public MinamoSelectDescription? Description => session.Description;

    /// <summary>Gets the read-only properties published by the currently active select.</summary>
    public IReadOnlyList<MinamoSelectProperty> Properties => session.Properties;

    /// <summary>Gets the actions currently available to the host.</summary>
    public IReadOnlyList<MinamoSelectAction> Actions => session.Actions;

    /// <summary>Gets whether the whole select interaction has completed.</summary>
    public bool IsCompleted => session.IsCompleted;

    /// <summary>Gets the input request currently waiting for a host response.</summary>
    public MinamoSelectRequest? Request => session.Request;

    /// <summary>Executes an action from the currently published screen.</summary>
    public Task SelectAsync(MinamoSelectAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return session.SelectAsync(action);
    }

    /// <summary>Executes an action with its host-supplied argument from the published screen.</summary>
    public Task SelectAsync(MinamoSelectAction action, object? argument)
    {
        ArgumentNullException.ThrowIfNull(action);
        return session.SelectAsync(action, argument);
    }

    /// <summary>Asynchronously sends a host event that does not accept an argument.</summary>
    public Task SendAsync(string eventId) => session.SendAsync(eventId);

    /// <summary>Asynchronously sends a host event with its host-supplied argument.</summary>
    public Task SendAsync(string eventId, object? argument) =>
        session.SendAsync(eventId, argument);

    /// <summary>Resumes the action with a nil response to its current request.</summary>
    public Task RespondAsync(MinamoSelectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return session.RespondAsync(request, null);
    }

    /// <summary>Resumes the action with a response to its current request.</summary>
    public Task RespondAsync(
        MinamoSelectRequest request,
        object? response)
    {
        ArgumentNullException.ThrowIfNull(request);
        return session.RespondAsync(request, response);
    }

    /// <summary>Converts the completed select value to a host type.</summary>
    public T? GetValue<T>() => session.GetValue<T>();

    /// <summary>Attempts to convert the completed select value to a host type.</summary>
    public bool TryGetValue<T>(out T? result) => session.TryGetValue(out result);

    /// <inheritdoc/>
    public void Dispose() => session.Dispose();
}
