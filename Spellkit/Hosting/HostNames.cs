using System.Collections.ObjectModel;

namespace Spellkit.Hosting;

internal static class HostNames
{
    public static void ValidateIdentifier(string name, string parameterName, string kind)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"A host {kind} requires a name.", parameterName);
        }

        if (!IsIdentifier(name))
        {
            throw new ArgumentException(
                $"Host {kind} name '{name}' is not a valid Spellkit identifier.",
                parameterName);
        }
    }

    public static void ValidateDottedName(string name, string parameterName, string kind)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"A host {kind} requires a name.", parameterName);
        }

        foreach (var segment in name.Split('.'))
        {
            if (!IsIdentifier(segment))
            {
                throw new ArgumentException(
                    $"Host {kind} name '{name}' must contain only Spellkit identifier segments.",
                    parameterName);
            }
        }
    }

    public static void ValidateCapability(
        string? capability,
        string parameterName,
        bool optional = false)
    {
        if (capability is null)
        {
            if (optional)
            {
                return;
            }

            throw new ArgumentException("Capability names cannot be null.", parameterName);
        }

        if (string.IsNullOrWhiteSpace(capability))
        {
            if (optional)
            {
                return;
            }

            throw new ArgumentException("Capability names cannot be empty.", parameterName);
        }

        if (capability == "*")
        {
            return;
        }

        var name = capability.EndsWith(".*", StringComparison.Ordinal)
            ? capability[..^2]
            : capability;
        ValidateDottedName(name, parameterName, "capability");
    }

    public static ReadOnlyCollection<SpellkitCommandParameter> Snapshot(
        SpellkitCommandParameter[] parameters) =>
        Array.AsReadOnly((SpellkitCommandParameter[])parameters.Clone());

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !IsIdentifierStart(value[0]))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char value) =>
        value == '_' || char.IsLetter(value);

    private static bool IsIdentifierPart(char value) =>
        value == '_' || char.IsLetterOrDigit(value);
}
