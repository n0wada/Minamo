using System.Collections;
using Xunit;

namespace Spellkit.UnitTesting.Compiler;

public sealed class BuildResultTests
{
    [Fact]
    public void SnapshotsAndClassifiesMessages()
    {
        var source = new SingleUseMessages(
            Message(BuildMessageType.Warning, "warning"),
            Message(BuildMessageType.Error, "error"),
            Message(BuildMessageType.Hint, "hint"));

        var result = Result.Create<string>("ignored", source);

        Assert.False(result.Success);
        Assert.Equal(3, result.Messages.Count);
        Assert.Single(result.Errors);
        Assert.Equal("error", result.Errors[0].Message);
        Assert.Single(result.Warnings);
        Assert.Equal("warning", result.Warnings[0].Message);
        Assert.Equal(1, source.EnumerationCount);
    }

    [Fact]
    public void GetsSuccessfulValue()
    {
        var result = Result.Create<string>("value");

        Assert.Equal("value", result.GetValueOrThrow());
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("value", value);
    }

    [Fact]
    public void RejectsFailedValue()
    {
        var result = Result.Create<string>(
            "ignored",
            new[] { Message(BuildMessageType.Error, "error") });

        Assert.Throws<SpellkitBuildException>(() => result.GetValueOrThrow());
        Assert.False(result.TryGetValue(out var value));
        Assert.Null(value);
    }

    private static BuildMessage Message(BuildMessageType type, string message) =>
        new(message, type, 1, 1, 1, null);

    private sealed class SingleUseMessages(params BuildMessage[] messages) : IEnumerable<BuildMessage>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<BuildMessage> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("Messages were enumerated more than once.");
            }

            return ((IEnumerable<BuildMessage>)messages).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
