using Spellkit.Hosting;
using Spellkit.Library;
using Xunit;

namespace Spellkit.UnitTesting.Cli;

public sealed class JsonLibraryTests
{
    [Fact]
    public void RejectsInvalidJsonText()
    {
        using var instance = new SpellkitHost().CreateInstance();

        var result = instance.Execute("Json.Parse(\"{\")");

        Assert.False(result.Success);
    }

    [Fact]
    public void RejectsNonStringObjectKeys()
    {
        using var instance = new SpellkitHost().CreateInstance();

        const string source = """
            mut values = Dictionary()
            values.Add(1, "one")
            Json.Stringify(values)
            """;

        var result = instance.Execute(source);

        Assert.False(result.Success);
    }

    [Fact]
    public void RejectsCyclicCollections()
    {
        using var instance = new SpellkitHost().CreateInstance();
        const string source = """
            mut values = []
            values.Add(values)
            Json.Stringify(values)
            """;

        var result = instance.Execute(source);

        Assert.False(result.Success);
    }
}
