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

    /// <summary>Gets a property by name from the current publication.</summary>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    /// <exception cref="ArgumentException">No property with that name is currently published.</exception>
    public MinamoSelectProperty GetProperty(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var property in Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                return property;
            }
        }

        throw new ArgumentException($"No property named '{name}' is published by select '{Name}'.", nameof(name));
    }

    /// <summary>Gets all currently available actions, including hidden actions.</summary>
    public IReadOnlyList<MinamoSelectAction> Actions => session.Actions;

    /// <summary>Gets the token identifying the current publication of this select.</summary>
    public MinamoSelectToken Token => session.Token;

    /// <summary>Gets whether the whole select interaction has completed.</summary>
    public bool IsCompleted => session.IsCompleted;

    /// <summary>Gets the input request currently waiting for a host response.</summary>
    public MinamoSelectRequest? Request => session.Request;

    /// <summary>Executes the action with the specified ID from the current publication.</summary>
    public Task SelectAsync(string actionId) =>
        session.SelectAsync(actionId);

    /// <summary>Executes the action with the specified ID and a host-supplied argument.</summary>
    public Task SelectAsync(string actionId, object? argument) =>
        session.SelectAsync(actionId, argument);

    /// <summary>Executes an action after verifying the token of the expected publication.</summary>
    public Task SelectAsync(string actionId, MinamoSelectToken expectedToken) =>
        session.SelectAsync(actionId, expectedToken);

    /// <summary>Executes an action with an argument after verifying the expected publication token.</summary>
    public Task SelectAsync(string actionId, object? argument, MinamoSelectToken expectedToken) =>
        session.SelectAsync(actionId, argument, expectedToken);

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
