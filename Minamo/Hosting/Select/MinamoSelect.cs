using System.Collections.Generic;
using System.Threading.Tasks;

namespace Minamo.Hosting;

/// <summary>
/// Provides the choice-oriented API for an interactive select.
/// The current choices are published asynchronously and remain safe to read until the
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

    /// <summary>Gets the choices currently available to the host.</summary>
    public IReadOnlyList<MinamoChoice> Choices => session.Choices;

    /// <summary>Gets whether the whole select interaction has completed.</summary>
    public bool IsCompleted => session.IsCompleted;

    /// <summary>Gets the input request currently waiting for a host response.</summary>
    public MinamoSelectRequest? Request => session.Request;

    /// <summary>Executes a choice from the currently published screen.</summary>
    public Task SelectAsync(MinamoChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return session.SelectAsync(choice);
    }

    /// <summary>Executes a choice with its host-supplied argument from the published screen.</summary>
    public Task SelectAsync(MinamoChoice choice, object? argument)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return session.SelectAsync(choice, argument);
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
