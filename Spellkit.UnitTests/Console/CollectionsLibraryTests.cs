using Spellkit.Hosting;
using Spellkit.Library;
using Xunit;

namespace Spellkit.UnitTesting.Cli;

public sealed class CollectionsLibraryTests
{
    [Fact]
    public void PriorityQueueSortedSetAndRingBufferExposeTheirOrderingContracts()
    {
        var source = """
            import * from collections

            let queue = PriorityQueue()
            queue.Enqueue("later", 2)
            queue.Enqueue("first", 1)
            queue.Enqueue("second", 1)
            let first = queue.Dequeue()
            let second = queue.Dequeue()
            let third = queue.Peek()

            let sorted = SortedSet()
            sorted.Add(3)
            sorted.Add(1)
            sorted.Add(2)
            sorted.Add(2)
            let ranged = sorted.Range(2).ToArray()

            let history = RingBuffer(3)
            history.Add(1)
            history.Add(2)
            history.Add(3)
            history.Add(4)
            history.Add(5)

            first.value == "first"
                && first.priority == 1
                && second.value == "second"
                && second.priority == 1
                && third.value == "later"
                && third.priority == 2
                && ranged.Length() == 2
                && ranged[0] == 2
                && ranged[1] == 3
                && history.Length() == 3
                && history.First() == 3
                && history.Last() == 5
                && history.Capacity == 3
            """;

        var compiled = new SpellkitHost().AddStandardLibrary().Compile(source);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Errors));
        using var instance = new SpellkitHost().AddStandardLibrary().CreateInstance();
        var result = instance.Execute(source);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.True(result.GetValue<bool>());
    }

    [Fact]
    public void DequeAndMultiMapPreserveTheirValueOrdering()
    {
        var source = """
            import * from collections

            let queue = Deque([2, 3])
            queue.PushFront(1)
            queue.PushBack(4)
            let first = queue.PopFront()
            let last = queue.PopBack()

            let map = MultiMap()
            map.Add("group", 1)
            map.Add("group", 2)
            map.Add("other", 3)
            map.Remove("group", 1)

            let values = map.Get("group")
            let keys = map.Keys
            first == 1
                && last == 4
                && queue.Length() == 2
                && queue.First() == 2
                && queue.Last() == 3
                && values.Length() == 1
                && values[0] == 2
                && keys.Length() == 2
                && keys[0] == "group"
                && keys[1] == "other"
                && map.Count == 2
            """;

        var compiled = new SpellkitHost().AddStandardLibrary().Compile(source);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Errors));
        using var instance = new SpellkitHost().AddStandardLibrary().CreateInstance();
        var result = instance.Execute(source);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.True(result.GetValue<bool>());
    }

    [Fact]
    public void CollectionConstructorsConsumeInitialSequences()
    {
        var source = """
            import * from collections

            let priority = PriorityQueue([
                (value: "later", priority: 2),
                (value: "first", priority: 1)
            ])
            let sorted = SortedSet([3, 1, 3])
            let groups = MultiMap([
                (key: "group", value: 1),
                (key: "group", value: 2)
            ])
            let history = RingBuffer(2, [1, 2, 3])

            let next = priority.Dequeue()
            let values = groups.Get("group")

            next.value == "first"
                && sorted.First() == 1
                && sorted.Last() == 3
                && values.Length() == 2
                && values[0] == 1
                && values[1] == 2
                && history.First() == 2
                && history.Last() == 3
            """;

        var compiled = new SpellkitHost().AddStandardLibrary().Compile(source);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Errors));
        using var instance = new SpellkitHost().AddStandardLibrary().CreateInstance();
        var result = instance.Execute(source);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.True(result.GetValue<bool>());
    }

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
