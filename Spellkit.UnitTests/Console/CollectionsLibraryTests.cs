using Spellkit.Hosting;
using Spellkit.Library;
using Xunit;

namespace Spellkit.UnitTesting.Cli;

public sealed class CollectionsLibraryTests
{
    [Fact]
    public void SortedDictionaryKeepsKeysOrdered()
    {
        var source = """
            import * from collections

            let map = SortedDictionary()
            map.Add("ben", 2)
            map.Add("ada", 1)
            map["cy"] = 3

            let first = map.First()
            let last = map.Last()
            fmt("{0}|{1}|{2}|{3}|{4}|{5}",
                first.key,
                first.value,
                last.key,
                last.value,
                map.Count,
                map.TryGet("ben"))
            """;

        using var instance = new SpellkitHost().AddStandardLibrary().CreateInstance();
        var result = instance.Execute(source);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("ada|1|cy|3|3|2", result.GetValue<string>());
    }

    [Fact]
    public void SortedDictionaryRangesAndConvertsToDictionary()
    {
        var source = """
            import * from collections

            let map = SortedDictionary()
            map.Add("a", 1)
            map.Add("b", 2)
            map.Add("c", 3)
            map.Add("d", 4)

            mut text = ""
            for item in map.Range("b", "d", includeTo: false) {
                text += fmt("{0}:{1};", item.key, item.value)
            }

            let plain = map.ToDictionary()
            fmt("{0}|{1}|{2}", text, plain["a"], map.Get("missing", default: 99))
            """;

        using var instance = new SpellkitHost().AddStandardLibrary().CreateInstance();
        var result = instance.Execute(source);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("b:2;c:3;|1|99", result.GetValue<string>());
    }
}
