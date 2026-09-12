using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Linq;

namespace Minamo.Hosting;

public sealed record MinamoChoiceParameter(string Name, string? TypeName);

/// <summary>Free-form dictionary description declared for a select.</summary>
public sealed class MinamoSelectDescription
{
    private readonly MinamoDictionary value;

    internal MinamoSelectDescription(MinamoDictionary value) => this.value = value;

    /// <summary>Converts the description dictionary to a host type.</summary>
    public T? GetValue<T>() => MinamoHostValueConverter.Convert<T>(value, "Select description");

    /// <summary>Attempts to convert the description dictionary to a host type.</summary>
    public bool TryGetValue<T>(out T? result) =>
        MinamoHostValueConverter.TryConvert(value, out result);
}

/// <summary>Free-form UI hints attached to a published select member.</summary>
public sealed class MinamoSelectMetadata
{
    private readonly MinamoDictionary value;

    internal MinamoSelectMetadata(MinamoDictionary value) => this.value = value;

    /// <summary>Converts the metadata dictionary to a host type.</summary>
    public T? GetValue<T>() => MinamoHostValueConverter.Convert<T>(value, "Select metadata");

    /// <summary>Attempts to convert the metadata dictionary to a host type.</summary>
    public bool TryGetValue<T>(out T? result) =>
        MinamoHostValueConverter.TryConvert(value, out result);
}

/// <summary>A read-only value published by the currently active select.</summary>
public sealed class MinamoSelectProperty
{
    private readonly MinamoObject value;

    internal MinamoSelectProperty(
        string name,
        MinamoObject value,
        MinamoSelectMetadata? metadata)
    {
        Name = name;
        this.value = value;
        Metadata = metadata;
    }

    public string Name { get; }

    public MinamoSelectMetadata? Metadata { get; }

    /// <summary>Converts the published value to a host type.</summary>
    public T? GetValue<T>() => MinamoHostValueConverter.Convert<T>(value, $"Select property '{Name}'");

    /// <summary>Attempts to convert the published value to a host type.</summary>
    public bool TryGetValue<T>(out T? result) =>
        MinamoHostValueConverter.TryConvert(value, out result);
}

/// <summary>An input request yielded by a running select action.</summary>
public sealed class MinamoSelectRequest
{
    private readonly MinamoObject payload;

    internal MinamoSelectRequest(MinamoSelectRequestAwaitable awaitable)
    {
        Awaitable = awaitable;
        Kind = awaitable.Kind;
        payload = awaitable.Payload;
    }

    internal MinamoSelectRequestAwaitable Awaitable { get; }

    /// <summary>Gets the application-defined kind of input requested by the script.</summary>
    public string Kind { get; }

    /// <summary>Converts the request payload to a host type.</summary>
    public T? GetPayload<T>() => MinamoHostValueConverter.Convert<T>(payload, "Select request payload");

    /// <summary>Attempts to convert the request payload to a host type.</summary>
    public bool TryGetPayload<T>(out T? result) =>
        MinamoHostValueConverter.TryConvert(payload, out result);
}

/// <summary>Immutable data published internally for the current select screen.</summary>
internal sealed class MinamoSelectSnapshot
{
    internal MinamoSelectSnapshot(
        string name,
        MinamoSelectDescription? description,
        IReadOnlyList<MinamoSelectProperty> properties,
        IReadOnlyList<MinamoChoice> choices,
        bool isCompleted,
        MinamoSelectRequest? request = null)
    {
        Name = name;
        Description = description;
        Properties = properties;
        Choices = choices;
        IsCompleted = isCompleted;
        Request = request;
    }

    public string Name { get; }

    public MinamoSelectDescription? Description { get; }

    public IReadOnlyList<MinamoSelectProperty> Properties { get; }

    public IReadOnlyList<MinamoChoice> Choices { get; }

    public bool IsCompleted { get; }

    public MinamoSelectRequest? Request { get; }
}

public sealed record MinamoChoice
{
    public MinamoChoice(
        string id,
        int parameterCount)
    {
        Id = id;
        ParameterCount = parameterCount;
        Parameters = Array.Empty<MinamoChoiceParameter>();
    }

    internal MinamoChoice(
        string id,
        IReadOnlyList<MinamoChoiceParameter> parameters,
        MinamoSelectMetadata? metadata)
    {
        Id = id;
        Parameters = parameters.ToArray();
        ParameterCount = Parameters.Count;
        Metadata = metadata;
    }

    public string Id { get; }

    public MinamoSelectMetadata? Metadata { get; }

    public int ParameterCount { get; }

    public IReadOnlyList<MinamoChoiceParameter> Parameters { get; }

}
