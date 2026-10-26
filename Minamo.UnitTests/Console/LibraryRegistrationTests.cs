using Minamo.Hosting;
using Xunit;

namespace Minamo.UnitTesting.Cli;

public sealed class LibraryRegistrationTests
{
    [Fact]
    public void CoreJsonDoesNotRequireALibraryModule()
    {
        var host = new MinamoHost();

        Assert.True(Execute(host, "Json.Parse(\"null\")").Success);
        Assert.False(Execute(host, "import json").Success);
        Assert.False(Execute(host, "import * from http\nGet(\"https://example.test\")").Success);
    }

    [Theory]
    [InlineData("collections")]
    [InlineData("math")]
    [InlineData("random")]
    [InlineData("uuid")]
    [InlineData("time")]
    [InlineData("readline")]
    [InlineData("io")]
    [InlineData("text")]
    public void HostDoesNotProvideRemovedModules(string name)
    {
        var host = new MinamoHost();

        Assert.False(Execute(host, $"import {name}").Success);
    }

    private static MinamoExecutionResult Execute(MinamoHost host, string source)
    {
        using var instance = host.CreateInstance();
        return instance.Execute(source);
    }
}
