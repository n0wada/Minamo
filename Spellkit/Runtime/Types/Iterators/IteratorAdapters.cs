using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Collections;

namespace Spellkit.Runtime.Types;

internal sealed class SpellkitForeignIterator : SpellkitIterator
{
    private readonly IEnumerable<SpellkitObject> seq;

    public SpellkitForeignIterator(IEnumerable<SpellkitObject> seq) => this.seq = seq;

    public override SpellkitFunction GetIteratorFunction() => new SpellkitIteratorFunction(seq);

    public override IEnumerable<SpellkitObject> ToEnumerable(ExecutionContext _) => seq;

    public override object ToObject() => seq;

    public override int GetHashCode() => seq.GetHashCode();

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);
}

internal sealed class SpellkitNativeIterator : SpellkitIterator
{
    private readonly int unitId;
    private readonly int handle;
    private readonly FastList<SpellkitObject[]> captures;

    public SpellkitNativeIterator(int unitId, int handle, FastList<SpellkitObject[]> captures, SpellkitObject[] locals)
    {
        var vars = new FastList<SpellkitObject[]>(captures) { locals };
        (this.unitId, this.handle, this.captures) = (unitId, handle, vars);
    }

    public override SpellkitFunction GetIteratorFunction() => new SpellkitNativeIteratorFunction(unitId, handle, captures);

    public override object ToObject() => this;

    public override IEnumerable<SpellkitObject> ToEnumerable(ExecutionContext ctx) => new MultiPartEnumerable(ctx, GetIteratorFunction());

    public override int GetHashCode() => HashCode.Combine(unitId, handle, captures);

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);
}

internal sealed class DistinctByEnumerable : IEnumerable<SpellkitObject>
{
    private readonly ExecutionContext ctx;
    private readonly IEnumerable<SpellkitObject> source;
    private readonly SpellkitFunction selector;

    public DistinctByEnumerable(ExecutionContext ctx, IEnumerable<SpellkitObject> source, SpellkitFunction selector) =>
        (this.ctx, this.source, this.selector) = (ctx, source, selector);

    public IEnumerator<SpellkitObject> GetEnumerator() => Iterate().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<SpellkitObject> Iterate()
    {
        var keys = new HashSet<SpellkitObject>(SpellkitObjectKeyComparer.Instance);

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

internal abstract class FunctionEnumerable : IEnumerable<SpellkitObject>
{
    protected readonly ExecutionContext Context;
    protected readonly IEnumerable<SpellkitObject> Source;
    protected readonly SpellkitFunction Function;

    protected FunctionEnumerable(ExecutionContext context, IEnumerable<SpellkitObject> source, SpellkitFunction function) =>
        (Context, Source, Function) = (context, source, function);

    public abstract IEnumerator<SpellkitObject> GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class MapEnumerable : FunctionEnumerable
{
    public MapEnumerable(ExecutionContext context, IEnumerable<SpellkitObject> source, SpellkitFunction function)
        : base(context, source, function) { }

    public override IEnumerator<SpellkitObject> GetEnumerator()
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
    public FilterEnumerable(ExecutionContext context, IEnumerable<SpellkitObject> source, SpellkitFunction function)
        : base(context, source, function) { }

    public override IEnumerator<SpellkitObject> GetEnumerator()
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
    public TakeWhileEnumerable(ExecutionContext context, IEnumerable<SpellkitObject> source, SpellkitFunction function)
        : base(context, source, function) { }

    public override IEnumerator<SpellkitObject> GetEnumerator()
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
    public SkipWhileEnumerable(ExecutionContext context, IEnumerable<SpellkitObject> source, SpellkitFunction function)
        : base(context, source, function) { }

    public override IEnumerator<SpellkitObject> GetEnumerator()
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
internal sealed class MultiPartEnumerable : IEnumerable<SpellkitObject>
{
    private readonly SpellkitObject[] iterators;
    private readonly ExecutionContext ctx;

    public MultiPartEnumerable(ExecutionContext ctx, params SpellkitObject[] iterators) =>
        (this.ctx, this.iterators) = (ctx, iterators);

    public IEnumerator<SpellkitObject> GetEnumerator() => new MultiPartEnumerator(ctx, iterators);

    IEnumerator IEnumerable.GetEnumerator() => new MultiPartEnumerator(ctx, iterators);
}

//Used to implement "concat" method when several iterators are combined in one
internal sealed class MultiPartEnumerator : IEnumerator<SpellkitObject>
{
    private readonly SpellkitObject[] iterators;
    private int nextIterator = 0;
    private IEnumerator<SpellkitObject>? current;
    private readonly ExecutionContext ctx;

    public MultiPartEnumerator(ExecutionContext ctx, params SpellkitObject[] iterators) =>
        (this.ctx, this.iterators) = (ctx, iterators);

    public SpellkitObject Current => current!.Current;

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

            var next = SpellkitIterator.ToEnumerable(ctx, iterators[nextIterator++]);
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
