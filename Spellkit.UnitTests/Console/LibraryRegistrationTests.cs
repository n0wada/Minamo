using Spellkit.Hosting;
using Spellkit.Library;
using Xunit;

namespace Spellkit.UnitTesting.Cli;

public sealed class LibraryRegistrationTests
{
    [Fact]
    public void StandardLibraryDoesNotGrantHostOrExtendedModules()
    {
        var host = new SpellkitHost().AddStandardLibrary();

        Assert.True(Execute(host, "import * from math\nsqrt(9)").Success);
        Assert.True(Execute(host, "import json").Success);
        Assert.True(Execute(host, "import random").Success);
        Assert.False(Execute(host, "import * from console\nreadLine()").Success);
        Assert.False(Execute(host, "import io").Success);
        Assert.False(Execute(host, "import * from http\nGet(\"https://example.test\")").Success);
    }

    [Fact]
    public void HostLibraryRegistersConsoleModule()
    {
        var output = new System.Text.StringBuilder();
        var environment = new SpellkitEnvironment()
            .UseInput(_ => "input")
            .UseOutput(value => output.Append(value));
        using var instance = new SpellkitHost()
            .AddStandardLibrary()
            .AddHostLibrary()
            .CreateInstance(environment);

        var result = instance.Execute("import * from console\nprint(readLine(), terminator: nil)");

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("input", output.ToString());
    }

    [Fact]
    public void BundledLibrariesPreserveConsoleDistributionModules()
    {
        var host = new SpellkitHost().AddBundledLibraries();

        Assert.True(Execute(host, "import math").Success);
        Assert.True(Execute(host, "import console").Success);
        Assert.True(Execute(host, "import io").Success);
        Assert.True(Execute(host, "import http").Success);
    }

    private static SpellkitExecutionResult Execute(SpellkitHost host, string source)
    {
        using var instance = host.CreateInstance();
        return instance.Execute(source);
    }
}
