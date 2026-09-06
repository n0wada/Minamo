using Minamo.Hosting;
using Minamo.Library;
using Xunit;

namespace Minamo.UnitTesting.Cli;

public sealed class JsonLibraryTests
{
    [Fact]
    public void RejectsInvalidJsonText()
    {
        using var instance = new MinamoHost().CreateInstance();

        var result = instance.Execute("Json.Parse(\"{\")");

        Assert.False(result.Success);
    }

    [Fact]
    public void RejectsNonStringObjectKeys()
    {
        using var instance = new MinamoHost().CreateInstance();

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
        using var instance = new MinamoHost().CreateInstance();
        const string source = """
            mut values = []
            values.Add(values)
            Json.Stringify(values)
            """;

        var result = instance.Execute(source);

        Assert.False(result.Success);
    }
}
