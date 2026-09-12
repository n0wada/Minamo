using System.Diagnostics;
using System.Text;
using Xunit;

namespace Minamo.UnitTesting.Cli;

public sealed class ConsoleEndToEndTests
{
    [Fact]
    public async Task PrintsHelpAndVersion()
    {
        var help = await RunAsync("--help");
        var version = await RunAsync("--version");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Usage: minamo", help.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(0, version.ExitCode);
        Assert.StartsWith("minamo ", version.StandardOutput.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutesSourceFileAndReportsCompilationFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "minamo-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var valid = Path.Combine(root, "valid script.nami");
            var invalid = Path.Combine(root, "invalid.nami");
            await File.WriteAllTextAsync(valid, "print(40 + 2)", Encoding.UTF8);
            await File.WriteAllTextAsync(invalid, "let =", Encoding.UTF8);

            var success = await RunAsync(valid, "-nologo");
            var failure = await RunAsync(invalid, "-nologo");

            Assert.Equal(0, success.ExitCode);
            Assert.Contains("42", success.StandardOutput, StringComparison.Ordinal);
            Assert.Equal(1, failure.ExitCode);
            Assert.NotEmpty(failure.StandardError + failure.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ChecksSourceFilesWithoutExecutingThem()
    {
        var root = Path.Combine(Path.GetTempPath(), "minamo-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var valid = Path.Combine(root, "valid.nami");
            var invalid = Path.Combine(root, "invalid.nami");
            await File.WriteAllTextAsync(valid, "print(40 + 2)", Encoding.UTF8);
            await File.WriteAllTextAsync(invalid, "let =", Encoding.UTF8);

            var success = await RunAsync(valid, "--check", "-nologo");
            var failure = await RunAsync(invalid, "--check", "-nologo");

            Assert.Equal(0, success.ExitCode);
            Assert.DoesNotContain("42", success.StandardOutput, StringComparison.Ordinal);
            Assert.Equal(1, failure.ExitCode);
            Assert.NotEmpty(failure.StandardError + failure.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsoleCanTestSelectsWithoutScriptInvocation(bool useRepl)
    {
        var root = Path.Combine(Path.GetTempPath(), "minamo-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "choices.nami");
            await File.WriteAllTextAsync(source, """
                select counter {
                    mut count = 0
                    case "add" when count == 0 ["text": "Add one"] => { count += 1 }
                    case "finish" when count == 1 ["text": "Finish counting"] => {
                        print(fmt("Count: {0}", count))
                        exit count
                    }
                }
                """, Encoding.UTF8);

            var result = useRepl
                ? await RunWithInputAsync("do counter\nadd\nfinish\n#exit\n", source, "-i", "-nologo")
                : await RunWithInputAsync("add\nfinish\n", source, "--do", "counter", "-nologo");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Add one", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Finish counting", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Count: 1", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ConsoleCanRespondToASelectRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), "minamo-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "request.nami");
            await File.WriteAllTextAsync(source, """
                select profile {
                    case "rename" => {
                        let name = request("text", ["prompt": "Name"])
                        print(fmt("Hello, {0}", name))
                        exit name
                    }
                }
                """, Encoding.UTF8);

            var result = await RunWithInputAsync("rename\nMinamo\n", source, "--do", "profile", "-nologo");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("request[text]>", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Hello, Minamo", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task<ProcessResult> RunAsync(params string[] arguments) =>
        RunWithInputAsync(string.Empty, arguments);

    private static async Task<ProcessResult> RunWithInputAsync(string input, params string[] arguments)
    {
        var assembly = typeof(CommandLine).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(assembly);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Minamo console process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return new(process.ExitCode, await standardOutput, await standardError);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
