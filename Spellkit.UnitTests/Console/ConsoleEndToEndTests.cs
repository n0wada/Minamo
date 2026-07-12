using System.Diagnostics;
using System.Text;
using Xunit;

namespace Spellkit.UnitTesting.Cli;

public sealed class ConsoleEndToEndTests
{
    [Fact]
    public async Task PrintsHelpAndVersion()
    {
        var help = await RunAsync("--help");
        var version = await RunAsync("--version");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Usage: spk", help.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(0, version.ExitCode);
        Assert.StartsWith("spk ", version.StandardOutput.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutesSourceFileAndReportsCompilationFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "spellkit-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var valid = Path.Combine(root, "valid script.kit");
            var invalid = Path.Combine(root, "invalid.kit");
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

    private static async Task<ProcessResult> RunAsync(params string[] arguments)
    {
        var assembly = typeof(CommandLine).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(assembly);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Spellkit console process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
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
