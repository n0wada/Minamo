using System.Collections.Generic;
using System.Collections;
using Spellkit.Codegen;

namespace Spellkit.Runtime.Types;

public abstract class SpkCollection : SpkEnumerable
{
    protected SpkCollection(int typeCode) : base(typeCode) { }

    public override object ToObject() => ToTypedArray();

    private Array ToTypedArray()
    {
        if (Count is 0)
        {
            return Array.Empty<object>();
        }

        var xs = ToArray();
        var fe = xs[0].ToObject();

        if (fe is not null && TypeConverter.TryCreateTypedArray(xs, fe.GetType(), out var result))
        {
            return result!;
        }

        var newArr = new object[Count];

        for (var i = 0; i < newArr.Length; i++)
        {
            newArr[i] = xs[i].ToObject();
        }

        return newArr;
    }

    public abstract SpkObject[] ToArray();

    internal protected abstract SpkObject[] UnsafeAccess();

    internal static SpkObject[] ConcatValues(ExecutionContext ctx, params SpkObject[] values)
    {
        if (values is null)
        {
            return Array.Empty<SpkObject>();
        }

        var arr = new List<SpkObject>();

        for (var i = 0; i < values.Length; i++)
        {
            var seq = SpkIterator.ToEnumerable(ctx, values[i]);

            if (ctx.HasErrors)
            {
                break;
            }

            arr.AddRange(seq);
        }

        return arr.ToArray();
    }
}

internal sealed class SpkCollectionEnumerable : IEnumerable<SpkObject>
{
    private readonly SpkObject[] arr;
    private readonly int count;
    private readonly SpkCollection obj;
    private readonly int start;

    public SpkCollectionEnumerable(SpkObject[] arr, int start, int count, SpkCollection obj)
    {
        this.arr = arr;
        this.start = start;
        this.count = count;
        this.obj = obj;
    }

    public IEnumerator<SpkObject> GetEnumerator() => new SpkCollectionEnumerator(arr, start, count, obj);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class SpkCollectionEnumerator : IEnumerator<SpkObject>
{
    private readonly SpkObject[] arr;
    private readonly int count;
    private readonly SpkCollection obj;
    private readonly int version;
    private readonly int start;
    private int index = -1;

    public SpkCollectionEnumerator(SpkObject[] arr, int start, int count, SpkCollection obj)
    {
        this.arr = arr;
        this.start = start;
        this.count = count;
        this.obj = obj;
        version = obj.Version;
    }

    public SpkObject Current => arr[index + start] is SpkLabel lab ? lab.Value : arr[index + start];

    object IEnumerator.Current => Current;

    public void Dispose() { }

    public bool MoveNext() =>
        version != obj.Version ? throw new IterationException() : ++index < count;

    public void Reset() => index = -1;
}

[SpkType]
internal abstract partial class SpkCollTypeInfo : SpkTypeInfo
{
    #region Operations
    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject self) =>
        SpkInteger.Get(((SpkEnumerable)self).Count);

    protected override SpkObject IterateOp(ExecutionContext ctx, SpkObject self)
    {
        if (self is IEnumerable<SpkObject> seq)
        {
            return SpkIterator.Create(seq);
        }

        return Nil;
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == self.TypeId)
        {
            return self;
        }

        var xs = (SpkCollection)self;
        return targetType.ReflectedTypeId switch
        {
            Spk.Tuple => new SpkTuple(xs.ToArray()),
            Spk.Array => new SpkArray(xs.ToArray()),
            Spk.Iterator => SpkIterator.Create(xs),
            Spk.Set => new SpkSet(new HashSet<SpkObject>(xs.ToArray())),
            _ => base.CastOp(ctx, self, targetType)
        };
    }
    #endregion

    [SpkMethod]
    internal static IEnumerable<SpkObject> Indices(SpkCollection self)
    {
        IEnumerable<SpkObject> Iterate()
        {
            for (var i = 0; i < self.Count; i++)
            {
                yield return SpkInteger.Get(i);
            }
        }

        return Iterate();
    }

    [SpkMethod]
    internal static IEnumerable<SpkObject> Slice(SpkCollection self, int index, [Default]int? size)
    {
        var arr = self.UnsafeAccess();

        if (size is null)
        {
            size = self.Count - 1;
        }

        if (index == 0 && size == arr.Length - 1)
        {
            return self;
        }

        if (index < 0)
        {
            index = self.Count + index;
        }

        if (index >= self.Count)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        if (size < 0)
        {
            size = self.Count + size - 1;
        }

        if (size >= self.Count || size < 0)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        var len = size.Value - index + 1;

        if (len < 0)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        return new SpkCollectionEnumerable(arr, index, len, self);
    }

    [SpkMethod]
    internal static HashSet<SpkObject> ToSet(SpkCollection self) => new (self.ToArray());
}

public abstract class SpkEnumerable : SpkObject, IEnumerable<SpkObject>, IMeasurable
{
    internal protected int Version { get; protected set; }

    internal void MarkModified() => Version++;

    public virtual int Count { get; protected set; }

    protected SpkEnumerable(int typeCode) : base(typeCode) { }

    public abstract IEnumerator<SpkObject> GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override int GetHashCode() => HashCode.Combine(TypeId, Count, Version);
}

public interface IMeasurable
{
    int Count { get; }
}
