using Spellkit.Hosting;
using Spellkit.Library;
using Xunit;

namespace Spellkit.UnitTesting.Cli;

public sealed class JsonLibraryTests
{
    [Fact]
    public void RejectsInvalidJsonText()
    {
        using var instance = new SpellkitHost().AddStandardLibrary().CreateInstance();

        var result = instance.Execute("import * from json\nparse(\"{\")");

        Assert.False(result.Success);
    }

    [Fact]
    public void RejectsNonStringObjectKeys()
    {
        using var instance = new SpellkitHost().AddStandardLibrary().CreateInstance();

        const string source = """
            import * from json
            mut values = Dictionary()
            values.Add(1, "one")
            stringify(values)
            """;

        var result = instance.Execute(source);

        Assert.False(result.Success);
    }

    [Fact]
    public void RejectsCyclicCollections()
    {
        using var instance = new SpellkitHost().AddStandardLibrary().CreateInstance();
        const string source = """
            import * from json
            mut values = []
            values.Add(values)
            stringify(values)
            """;

        var result = instance.Execute(source);

        Assert.False(result.Success);
    }
}
