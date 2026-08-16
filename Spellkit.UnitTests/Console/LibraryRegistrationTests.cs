using Spellkit.Hosting;
using Spellkit.Library;
using Spellkit.Library.Mathematics;
using Xunit;

namespace Spellkit.UnitTesting.Cli;

public sealed class LibraryRegistrationTests
{
    [Fact]
    public void StandardLibraryRegistersAllBundledModules()
    {
        var host = new SpellkitHost().AddStandardLibrary();

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
        var host = new SpellkitHost();

        ExtensionLibraryLoader.RegisterAssembly(
            host,
            typeof(MathModule).Assembly,
            "Spellkit.Console");

        Assert.True(Execute(host, "import * from math\nsqrt(9)").Success);
    }

    private static SpellkitExecutionResult Execute(SpellkitHost host, string source)
    {
        using var instance = host.CreateInstance();
        return instance.Execute(source);
    }
}
