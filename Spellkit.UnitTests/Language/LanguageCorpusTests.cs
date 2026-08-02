using Spellkit.Compiler;
using Spellkit.Hosting;
using Spellkit.Library.IO;
using Spellkit.Library.Text;
using Spellkit.Library.Time;
using Spellkit.Library.Uuid;
using Xunit;

namespace Spellkit.UnitTesting.Language;

[Trait("Suite", "Language")]
public sealed class LanguageCorpusTests
{
    public static IEnumerable<TheoryDataRow<string>> TestFiles()
    {
        var path = Path.GetFullPath(Path.Combine(
            TestRepository.Root,
            "Spellkit.UnitTests",
            "Tests"));
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Language test path not found: {path}");
        }

        return Directory.GetFiles(path, "*.kit")
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => new TheoryDataRow<string>(file)
                .WithTestDisplayName($"Language: {Path.GetFileName(file)}")
                .WithTrait("Suite", "Language"));
    }

    [Theory]
    [MemberData(nameof(TestFiles))]
    public void LanguageFile(string fileName)
    {
        var options = new TestOptions
        {
            TestPath = fileName,
            ShowOnlyFailures = true,
            UseMarkdown = false
        };

        var runner = new TestRunner(options);

        Assert.True(
            runner.Run(new[] { fileName }),
            $"Language tests failed for {Path.GetFileName(fileName)}.");
    }
}
