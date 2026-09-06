using Minamo.Hosting;
using Minamo.Library;
using Minamo.Library.Mathematics;
using Xunit;

namespace Minamo.UnitTesting.Cli;

public sealed class LibraryRegistrationTests
{
    [Fact]
    public void StandardLibraryRegistersAllBundledModules()
    {
        var host = new MinamoHost().AddStandardLibrary();

        Assert.True(Execute(host, "import * from math\nsqrt(9)").Success);
        Assert.True(Execute(host, "Json.Parse(\"null\")").Success);
        Assert.False(Execute(host, "import json").Success);
        Assert.True(Execute(host, "import random").Success);
        Assert.True(Execute(host, "import readline").Success);
        Assert.True(Execute(host, "import io").Success);
        Assert.False(Execute(host, "import * from http\nGet(\"https://example.test\")").Success);
    }

    [Fact]
    public void ExtensionAssemblyRegistersGeneratedModulesWithoutALibraryInterface()
    {
        var host = new MinamoHost();

        ExtensionLibraryLoader.RegisterAssembly(
            host,
            typeof(MathModule).Assembly,
            "Minamo.Console");

        Assert.True(Execute(host, "import * from math\nsqrt(9)").Success);
    }

    private static MinamoExecutionResult Execute(MinamoHost host, string source)
    {
        using var instance = host.CreateInstance();
        return instance.Execute(source);
    }
}
