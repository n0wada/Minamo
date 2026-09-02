using Xunit;

namespace Spellkit.UnitTesting.Cli;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("-v")]
    [InlineData("--version")]
    public void RecognizesVersionOptions(string argument)
    {
        var options = CommandLine.Read(new[] { argument });

        Assert.True(options.ShowVersion);
        Assert.False(options.ShowHelp);
        Assert.Null(options.UserArguments);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void RecognizesHelpOptions(string argument)
    {
        var options = CommandLine.Read(new[] { argument });

        Assert.True(options.ShowHelp);
        Assert.False(options.ShowVersion);
        Assert.Null(options.UserArguments);
    }

    [Fact]
    public void KeepsUnknownLongOptionsAsScriptArguments()
    {
        var options = CommandLine.Read(new[] { "--theme", "dark" });

        Assert.False(options.ShowHelp);
        Assert.False(options.ShowVersion);
        Assert.NotNull(options.UserArguments);
    }

    [Fact]
    public void GeneratesConventionalOptionNames()
    {
        var help = CommandLine.GenerateHelp<CommandLineOptions>();

        Assert.Contains("-h, --help", help);
        Assert.Contains("-v, --version", help);
    }

    [Fact]
    public void RecognizesSelectStartupOption()
    {
        var options = CommandLine.Read(new[] { "Player.kit", "--do", "music.player" });

        Assert.Equal("music.player", options.SelectName);
        Assert.Equal(new[] { "Player.kit" }, options.FileNames);
    }

    [Theory]
    [InlineData("-check")]
    [InlineData("--check")]
    public void RecognizesCheckOption(string argument)
    {
        var options = CommandLine.Read(new[] { "script.kit", argument });

        Assert.True(options.CheckOnly);
        Assert.Equal(new[] { "script.kit" }, options.FileNames);
    }
}
