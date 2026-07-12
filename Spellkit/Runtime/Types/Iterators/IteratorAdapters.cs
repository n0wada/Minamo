using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Collections;

namespace Spellkit.Runtime.Types;

internal sealed class SpkForeignIterator : SpkIterator
{
    private readonly IEnumerable<SpkObject> seq;

    public SpkForeignIterator(IEnumerable<SpkObject> seq) => this.seq = seq;

    public override SpkFunction GetIteratorFunction() => new SpkIteratorFunction(seq);

    public override IEnumerable<SpkObject> ToEnumerable(ExecutionContext _) => seq;

    public override object ToObject() => seq;

    public override int GetHashCode() => seq.GetHashCode();

    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);
}

internal sealed class SpkNativeIterator : SpkIterator
{
    private readonly int unitId;
    private readonly int handle;
    private readonly FastList<SpkObject[]> captures;

    public SpkNativeIterator(int unitId, int handle, FastList<SpkObject[]> captures, SpkObject[] locals)
    {
        var vars = new FastList<SpkObject[]>(captures) { locals };
        (this.unitId, this.handle, this.captures) = (unitId, handle, vars);
    }

    public override SpkFunction GetIteratorFunction() => new SpkNativeIteratorFunction(unitId, handle, captures);

    public override object ToObject() => this;

    public override IEnumerable<SpkObject> ToEnumerable(ExecutionContext ctx) => new MultiPartEnumerable(ctx, GetIteratorFunction());

    public override int GetHashCode() => HashCode.Combine(unitId, handle, captures);

    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);
}

public sealed class EqualityComparer : IEqualityComparer<SpkObject>
{
    private readonly ExecutionContext ctx;
    private readonly SpkFunction func;

    public EqualityComparer(ExecutionContext ctx, SpkObject functor)
    {
        this.ctx = ctx;
        func = functor.ToFunction(ctx)!;
        ctx.ThrowIf();
    }

    public bool Equals(SpkObject? x, SpkObject? y)
    {
        var fst = func.Call(ctx, x!);
        var snd = func.Call(ctx, y!);
        return fst.Equals(snd, ctx);
    }

    public int GetHashCode([DisallowNull] SpkObject obj)
    {
        var x = func.Call(ctx, obj);
        return x.GetHashCode();
    }
}

//Generated when a currently traversed iterator was changed
internal sealed class IterationException : Exception { }

internal abstract class FunctionEnumerable : IEnumerable<SpkObject>
{
    protected readonly ExecutionContext Context;
    protected readonly IEnumerable<SpkObject> Source;
    protected readonly SpkFunction Function;

    protected FunctionEnumerable(ExecutionContext context, IEnumerable<SpkObject> source, SpkFunction function) =>
        (Context, Source, Function) = (context, source, function);

    public abstract IEnumerator<SpkObject> GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class MapEnumerable : FunctionEnumerable
{
    public MapEnumerable(ExecutionContext context, IEnumerable<SpkObject> source, SpkFunction function)
        : base(context, source, function) { }

    public override IEnumerator<SpkObject> GetEnumerator()
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
    public FilterEnumerable(ExecutionContext context, IEnumerable<SpkObject> source, SpkFunction function)
        : base(context, source, function) { }

    public override IEnumerator<SpkObject> GetEnumerator()
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
    public TakeWhileEnumerable(ExecutionContext context, IEnumerable<SpkObject> source, SpkFunction function)
        : base(context, source, function) { }

    public override IEnumerator<SpkObject> GetEnumerator()
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
    public SkipWhileEnumerable(ExecutionContext context, IEnumerable<SpkObject> source, SpkFunction function)
        : base(context, source, function) { }

    public override IEnumerator<SpkObject> GetEnumerator()
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
internal sealed class MultiPartEnumerable : IEnumerable<SpkObject>
{
    private readonly SpkObject[] iterators;
    private readonly ExecutionContext ctx;

    public MultiPartEnumerable(ExecutionContext ctx, params SpkObject[] iterators) =>
        (this.ctx, this.iterators) = (ctx, iterators);

    public IEnumerator<SpkObject> GetEnumerator() => new MultiPartEnumerator(ctx, iterators);

    IEnumerator IEnumerable.GetEnumerator() => new MultiPartEnumerator(ctx, iterators);
}

//Used to implement "concat" method when several iterators are combined in one
internal sealed class MultiPartEnumerator : IEnumerator<SpkObject>
{
    private readonly SpkObject[] iterators;
    private int nextIterator = 0;
    private IEnumerator<SpkObject>? current;
    private readonly ExecutionContext ctx;

    public MultiPartEnumerator(ExecutionContext ctx, params SpkObject[] iterators) =>
        (this.ctx, this.iterators) = (ctx, iterators);

    public SpkObject Current => current!.Current;

    object IEnumerator.Current => current!.Current;

    public void Dispose() { }

    public bool MoveNext()
    {
        if (current is null || !current.MoveNext())
        {
            if (iterators.Length > nextIterator)
            {
                var it = SpkIterator.ToEnumerable(ctx, iterators[nextIterator]);
                ctx.ThrowIf();
                nextIterator++;
                current = it.GetEnumerator();
                return current.MoveNext();
            }
            else
            {
                return false;
            }
        }
        else
        {
            return true;
        }
    }

    public void Reset()
    {
        current = null;
        nextIterator = 0;
    }
}
