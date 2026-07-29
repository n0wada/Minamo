using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Spellkit;

public enum BuildMessageType
{
    None,

    Hint,

    Warning,

    Error
}

public class BuildMessage
{
    private const string ErrorFormat = "{0}({1},{2}): {3} D{4}: {5}";

    public string? File { get; internal set; }

    public string Message { get; protected set; }

    public BuildMessageType Type { get; }

    public int Code { get; protected set; }

    public int Line { get; protected set; }

    public int Column { get; protected set; }

    public BuildMessage(string message, BuildMessageType type, int code, int line, int col, string? file)
    {
        Message = message;
        Type = type;
        Code = code;
        Line = line;
        Column = col;
        File = file;
    }

    public override string ToString()
    {
        var stype = Type == BuildMessageType.Error ? "Error"
            : Type == BuildMessageType.Warning ? "Warning"
            : Type == BuildMessageType.Hint ? "Information"
            : "";
        var scode = Code.ToString().PadLeft(3, '0');
        return string.Format(ErrorFormat, GetFileName(), Line, Column, stype, scode, Message);
    }

    protected string GetFileName() => File ?? "<memory>";
}

public class SpellkitException : Exception
{
    public SpellkitException(string message, Exception? innerException) : base(message, innerException) { }

    public SpellkitException(string message) : base(message, null) { }
}

public class SpellkitBuildException : SpellkitException
{
    public IEnumerable<BuildMessage> Messages { get; }

    public SpellkitBuildException(IEnumerable<BuildMessage> messages) : base("") =>
        Messages = messages;

    public SpellkitBuildException(string message, Exception? innerException) : base(message, innerException) =>
        Messages = Enumerable.Empty<BuildMessage>();

    public override string Message =>
        Messages is null || !Messages.Any() ? base.Message
            : string.Join(Environment.NewLine, Messages.Select(m => m.ToString()));
}

public abstract class Result
{
    protected Result(IEnumerable<BuildMessage> messages)
    {
        var snapshot = messages.ToArray();
        Messages = Array.AsReadOnly(snapshot);
        Errors = Filter(snapshot, BuildMessageType.Error);
        Warnings = Filter(snapshot, BuildMessageType.Warning);
        Success = Errors.Count == 0;
    }

    public static Result<T> Create<T>(T? result, IEnumerable<BuildMessage>? messages = null) =>
        new(result, messages ?? Enumerable.Empty<BuildMessage>());

    public IReadOnlyList<BuildMessage> Messages { get; }

    public IReadOnlyList<BuildMessage> Errors { get; }

    public IReadOnlyList<BuildMessage> Warnings { get; }

    public bool Success { get; }

    private static ReadOnlyCollection<BuildMessage> Filter(
        IEnumerable<BuildMessage> messages,
        BuildMessageType type) =>
        Array.AsReadOnly(messages.Where(message => message.Type == type).ToArray());
}

public sealed class Result<T> : Result
{
    internal Result(T? result, IEnumerable<BuildMessage>? messages = null)
        : base(messages ?? Enumerable.Empty<BuildMessage>()) => Value = result;

    public T? Value { get; }

    public T GetValueOrThrow()
    {
        if (!Success)
        {
            throw new SpellkitBuildException(Messages);
        }

        return Value!;
    }

    public bool TryGetValue(out T? value)
    {
        value = Success ? Value : default;
        return Success;
    }
}
