using System.Reflection;
using Minamo.Hosting;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using Xunit;
using ExecutionContext = Minamo.Runtime.ExecutionContext;

namespace Minamo.UnitTesting.Runtime;

[Trait("Suite", "Runtime")]
public sealed class RuntimeEfficiencyTests
{
    [Fact]
    public void ByteReadsAndIdentityKeyLookupsDoNotAllocate()
    {
        var info = new MinamoByteArrayTypeInfo();
        var source = new byte[4096];
        source[0] = 1;
        var bytes = info.Create(source);
        source[0] = 2;
        var copy = bytes.GetBytes();
        copy[0] = 3;
        var clone = Assert.IsType<MinamoByteArray>(bytes.Clone());
        var get = (Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject>)typeof(MinamoByteArrayTypeInfo)
            .GetMethod("GetOp", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate(typeof(Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject>), info);
        var array = new MinamoArray(new MinamoObject[] { MinamoInteger.One });
        var set = new MinamoSet(new MinamoObject[] { array });
        var comparer = MinamoObjectKeyComparer.Instance;
        for (var i = 0; i < 10000; i++)
        {
            get(null!, bytes, MinamoInteger.Zero);
            set.Contains(array);
            comparer.GetHashCode(array);
        }
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            get(null!, bytes, MinamoInteger.Zero);
            set.Contains(array);
            comparer.GetHashCode(array);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, allocated);
        Assert.Same(MinamoInteger.One, get(null!, bytes, MinamoInteger.Zero));
        Assert.Equal(1, clone.GetBytes()[0]);
        array.Add(MinamoInteger.Two);
        Assert.True(set.Contains(array));
    }

    [Fact]
    public void ValueFramesReleaseReferencesAndClonesKeepTheirFrames()
    {
        var stack = new CallStack();
        var frame = new Caller(null!, 12, new EvalStack(1), new MinamoObject[] { MinamoInteger.One });
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            stack.Push(new Caller(frame.Function, frame.Offset, frame.EvalStack, frame.Locals));
            stack.Pop();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, allocated);
        stack.Push(frame);
        var clone = stack.Clone();
        Assert.Equal(12, stack.Pop().Offset);
        Assert.Null(stack[0].Locals);
        Assert.Same(frame.Locals, clone[0].Locals);
        stack.Push(Caller.External);
        Assert.True(stack.Pop().IsExternal);
        Assert.Equal(12, clone.Pop().Offset);
        Assert.True(Caller.Root.IsRoot);
        Assert.False(Caller.Root.IsExternal);
    }

    [Fact]
    public void ConsumedArgumentContainersAndCatchSectionsReleaseReferences()
    {
        using var instance = new MinamoHost().CreateInstance();
        Assert.True(instance.Execute("nil").Success);
        var context = MinamoMachine.CreateExecutionContext(instance.RuntimeContext!);
        var locals = new MinamoObject[] { MinamoInteger.One };
        var outer = context.PushArguments(locals, -1);
        var inner = context.PushArguments(locals, 0, locals);
        Assert.Same(locals, context.PopArguments());
        Assert.Null(inner.Locals);
        Assert.Null(inner.VarArgs);
        Assert.Same(outer, context.PeekArguments());
        Assert.Same(locals, context.PopArguments());
        var sections = new SectionStack();
        var marks = new Stack<CatchMark>();
        sections.Push(marks);
        Assert.Same(marks, sections.Pop());
        var storage = (Array)typeof(SectionStack).GetField("array", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sections)!;
        Assert.Null(storage.GetValue(0));
    }

    [Fact]
    public void ArrayCompactRetainsOrderAndClearsRemovedSlotsInPlace()
    {
        MinamoObject[] storage = [MinamoInteger.One, MinamoNil.Instance, MinamoNil.Terminator, MinamoInteger.Two, MinamoNil.Instance];
        var array = new MinamoArray(storage);
        using var before = array.GetEnumerator();

        MinamoArrayTypeInfo.Compact(null!, array);

        Assert.Same(storage, array.UnsafeAccess());
        Assert.Equal(2, array.Count);
        Assert.Equal(3, array.Version);
        Assert.Same(MinamoInteger.One, storage[0]);
        Assert.Same(MinamoInteger.Two, storage[1]);
        Assert.All(storage[2..], item => Assert.Null(item));
        Assert.Throws<IterationException>(() => before.MoveNext());

        using var after = array.GetEnumerator();
        MinamoArrayTypeInfo.Compact(null!, array);
        Assert.True(after.MoveNext());
        Assert.Same(MinamoInteger.One, after.Current);
    }

    [Fact]
    public void ArrayCompactKeepsUnprocessedElementsOnFailure()
    {
        MinamoObject[] storage = [MinamoNil.Instance, MinamoInteger.One, null!, MinamoInteger.Two];
        var array = new MinamoArray(storage);

        Assert.Throws<NullReferenceException>(() => MinamoArrayTypeInfo.Compact(null!, array));

        Assert.Equal(3, array.Count);
        Assert.Equal(1, array.Version);
        Assert.Same(MinamoInteger.One, storage[0]);
        Assert.Null(storage[1]);
        Assert.Same(MinamoInteger.Two, storage[2]);
        Assert.Null(storage[3]);
    }

    [Fact]
    public void ArrayCompactAppliesEarlierRemovalsBeforeReadingACustomTypeId()
    {
        MinamoObject[] storage = [MinamoNil.Instance, MinamoInteger.One, MinamoNil.Instance, MinamoNil.Instance];
        var array = new MinamoArray(storage);
        var observer = new ArrayObserver(array);
        storage[2] = observer;

        MinamoArrayTypeInfo.Compact(null!, array);

        Assert.Equal(3, observer.ObservedCount);
        Assert.Equal(2, array.Count);
        Assert.Same(observer, array[1]);
    }

    private sealed class ArrayObserver(MinamoArray array) : MinamoArray([])
    {
        public int ObservedCount { get; private set; }

        public override int TypeId
        {
            get
            {
                ObservedCount = array.Count;
                return base.TypeId;
            }
        }
    }

    [Fact]
    public void SetFastPathsKeepDuplicateAndIteratorMutationSemantics()
    {
        using var instance = new MinamoHost().CreateInstance();
        Assert.True(instance.Execute("nil").Success);
        var context = MinamoMachine.CreateExecutionContext(instance.RuntimeContext!);
        var set = new MinamoSet(MinamoInteger.One);
        Assert.True(set.IsSupersetOf(context, new MinamoArray(new MinamoObject[] { MinamoInteger.One, MinamoInteger.One })));
        Assert.True(set.Equals(new MinamoSet(MinamoInteger.One)));
        Assert.False(set.Equals(new MinamoSet(MinamoInteger.Two)));
        IEnumerable<MinamoObject> Mutate()
        {
            set.Add(MinamoInteger.Two);
            yield return MinamoInteger.Two;
        }
        Assert.False(set.IsSupersetOf(context, MinamoIterator.Create(Mutate())));
        Assert.True(set.Contains(MinamoInteger.Two));
        var storage = new MinamoObject[1];
        var cyclic = new MinamoTuple(storage);
        storage[0] = cyclic;
        var keys = new MinamoSet(new MinamoObject[] { cyclic });
        Assert.True(keys.Contains(cyclic));
        Assert.False(context.HasErrors);
    }

    [Fact]
    public void JsonAndByteConversionsKeepUnicodeBoundariesAndIsolation()
    {
        using var instance = new MinamoHost().CreateInstance();
        var result = instance.Execute(""""
            let bytes = ByteArray.FromString("日本語 ✓")
            assert("日本語 ✓", bytes.Decode())
            assert(bytes.ToHex(), ByteArray.FromBase64(bytes.ToBase64()).ToHex())
            assert(65, ByteArray.FromString("A")[-1])
            assert(0, ByteArray().Length())
            assert("{}", ByteArray().ToString())
            assert("{0x00,0xFF}", ByteArray.FromHex("00FF").ToString())
            assert("日本語 ✓日本語 ✓", ByteArray.Concat(bytes, bytes).Decode())
            let value = Json.Parse("""{"text":"日本語 ✓","items":[1,true,null]}""")
            assert(Json.Stringify(value), Json.Stringify(Json.Parse(Json.Stringify(value))))
            assert(Json.Stringify(value), Json.Stringify(Json.Parse(Json.Stringify(value, indented: true))))
            mut failed = false
            try { ByteArray.FromString("A")[1] } catch { failed = true }
            assert(true, failed)
            """");
        Assert.True(result.Success, result.Failure?.Message);
    }
}
