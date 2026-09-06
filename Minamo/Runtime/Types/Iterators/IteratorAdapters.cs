using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Collections;

namespace Minamo.Runtime.Types;

internal sealed class MinamoForeignIterator : MinamoIterator
{
    private readonly IEnumerable<MinamoObject> seq;

    public MinamoForeignIterator(IEnumerable<MinamoObject> seq) => this.seq = seq;

    public override MinamoFunction GetIteratorFunction() => new MinamoIteratorFunction(seq);

    public override IEnumerable<MinamoObject> ToEnumerable(ExecutionContext _) => seq;

    public override object ToObject() => seq;

    public override int GetHashCode() => seq.GetHashCode();

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);
}

internal sealed class MinamoNativeIterator : MinamoIterator
{
    private readonly int unitId;
    private readonly int handle;
    private readonly FastList<MinamoObject[]> captures;

    public MinamoNativeIterator(int unitId, int handle, FastList<MinamoObject[]> captures, MinamoObject[] locals)
    {
        var vars = new FastList<MinamoObject[]>(captures) { locals };
        (this.unitId, this.handle, this.captures) = (unitId, handle, vars);
    }

    public override MinamoFunction GetIteratorFunction() => new MinamoNativeIteratorFunction(unitId, handle, captures);

    public override object ToObject() => this;

    public override IEnumerable<MinamoObject> ToEnumerable(ExecutionContext ctx) => new MultiPartEnumerable(ctx, GetIteratorFunction());

    public override int GetHashCode() => HashCode.Combine(unitId, handle, captures);

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);
}

internal sealed class DistinctByEnumerable : IEnumerable<MinamoObject>
{
    private readonly ExecutionContext ctx;
    private readonly IEnumerable<MinamoObject> source;
    private readonly MinamoFunction selector;

    public DistinctByEnumerable(ExecutionContext ctx, IEnumerable<MinamoObject> source, MinamoFunction selector) =>
        (this.ctx, this.source, this.selector) = (ctx, source, selector);

    public IEnumerator<MinamoObject> GetEnumerator() => Iterate().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<MinamoObject> Iterate()
    {
        var keys = new HashSet<MinamoObject>(MinamoObjectKeyComparer.Instance);

        foreach (var item in source)
        {
            var key = selector.Call(ctx, item);
            if (ctx.HasErrors)
            {
                yield break;
            }

            if (keys.Add(key))
            {
                yield return item;
            }
        }
    }
}

//Generated when a currently traversed iterator was changed
internal sealed class IterationException : Exception { }

internal abstract class FunctionEnumerable : IEnumerable<MinamoObject>
{
    protected readonly ExecutionContext Context;
    protected readonly IEnumerable<MinamoObject> Source;
    protected readonly MinamoFunction Function;

    protected FunctionEnumerable(ExecutionContext context, IEnumerable<MinamoObject> source, MinamoFunction function) =>
        (Context, Source, Function) = (context, source, function);

    public abstract IEnumerator<MinamoObject> GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class MapEnumerable : FunctionEnumerable
{
    public MapEnumerable(ExecutionContext context, IEnumerable<MinamoObject> source, MinamoFunction function)
        : base(context, source, function) { }

    public override IEnumerator<MinamoObject> GetEnumerator()
    {
        foreach (var item in Source)
        {
            var result = Function.Call(Context, item);
            if (Context.HasErrors)
            {
                yield break;
            }

            yield return result;
        }
    }
}

internal sealed class FilterEnumerable : FunctionEnumerable
{
    public FilterEnumerable(ExecutionContext context, IEnumerable<MinamoObject> source, MinamoFunction function)
        : base(context, source, function) { }

    public override IEnumerator<MinamoObject> GetEnumerator()
    {
        foreach (var item in Source)
        {
            var result = Function.Call(Context, item);
            if (Context.HasErrors)
            {
                yield break;
            }

            if (result.IsTrue())
            {
                yield return item;
            }
        }
    }
}

internal sealed class TakeWhileEnumerable : FunctionEnumerable
{
    public TakeWhileEnumerable(ExecutionContext context, IEnumerable<MinamoObject> source, MinamoFunction function)
        : base(context, source, function) { }

    public override IEnumerator<MinamoObject> GetEnumerator()
    {
        foreach (var item in Source)
        {
            var result = Function.Call(Context, item);
            if (Context.HasErrors || !result.IsTrue())
            {
                yield break;
            }

            yield return item;
        }
    }
}

internal sealed class SkipWhileEnumerable : FunctionEnumerable
{
    public SkipWhileEnumerable(ExecutionContext context, IEnumerable<MinamoObject> source, MinamoFunction function)
        : base(context, source, function) { }

    public override IEnumerator<MinamoObject> GetEnumerator()
    {
        using var enumerator = Source.GetEnumerator();

        while (enumerator.MoveNext())
        {
            var result = Function.Call(Context, enumerator.Current);
            if (Context.HasErrors)
            {
                yield break;
            }

            if (result.IsTrue())
            {
                continue;
            }

            yield return enumerator.Current;
            break;
        }

        while (enumerator.MoveNext())
        {
            yield return enumerator.Current;
        }
    }
}

//Used to create MultiPartEnumerator
internal sealed class MultiPartEnumerable : IEnumerable<MinamoObject>
{
    private readonly MinamoObject[] iterators;
    private readonly ExecutionContext ctx;

    public MultiPartEnumerable(ExecutionContext ctx, params MinamoObject[] iterators) =>
        (this.ctx, this.iterators) = (ctx, iterators);

    public IEnumerator<MinamoObject> GetEnumerator() => new MultiPartEnumerator(ctx, iterators);

    IEnumerator IEnumerable.GetEnumerator() => new MultiPartEnumerator(ctx, iterators);
}

//Used to implement "concat" method when several iterators are combined in one
internal sealed class MultiPartEnumerator : IEnumerator<MinamoObject>
{
    private readonly MinamoObject[] iterators;
    private int nextIterator = 0;
    private IEnumerator<MinamoObject>? current;
    private readonly ExecutionContext ctx;

    public MultiPartEnumerator(ExecutionContext ctx, params MinamoObject[] iterators) =>
        (this.ctx, this.iterators) = (ctx, iterators);

    public MinamoObject Current => current!.Current;

    object IEnumerator.Current => current!.Current;

    public void Dispose() => current?.Dispose();

    public bool MoveNext()
    {
        while (true)
        {
            if (current is not null && current.MoveNext())
            {
                return true;
            }

            current?.Dispose();
            current = null;

            if (nextIterator >= iterators.Length)
            {
                return false;
            }

            var next = MinamoIterator.ToEnumerable(ctx, iterators[nextIterator++]);
            ctx.ThrowIf();
            current = next.GetEnumerator();
        }
    }

    public void Reset()
    {
        current?.Dispose();
        current = null;
        nextIterator = 0;
    }
}
