using System.Collections.Generic;
using System.Collections;
using Spellkit.Codegen;

namespace Spellkit.Runtime.Types;

public abstract class SpellkitCollection : SpellkitEnumerable
{
    protected SpellkitCollection(int typeCode) : base(typeCode) { }

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

    public abstract SpellkitObject[] ToArray();

    internal static SpellkitObject[] ConcatValues(ExecutionContext ctx, params SpellkitObject[] values)
    {
        if (values is null)
        {
            return Array.Empty<SpellkitObject>();
        }

        var arr = new List<SpellkitObject>();

        for (var i = 0; i < values.Length; i++)
        {
            var seq = SpellkitIterator.ToEnumerable(ctx, values[i]);

            if (ctx.HasErrors)
            {
                break;
            }

            arr.AddRange(seq);
        }

        return arr.ToArray();
    }
}

internal sealed class SpellkitCollectionEnumerable : IEnumerable<SpellkitObject>
{
    private readonly SpellkitObject[] arr;
    private readonly int count;
    private readonly SpellkitCollection obj;
    private readonly int start;

    public SpellkitCollectionEnumerable(SpellkitObject[] arr, int start, int count, SpellkitCollection obj)
    {
        this.arr = arr;
        this.start = start;
        this.count = count;
        this.obj = obj;
    }

    public IEnumerator<SpellkitObject> GetEnumerator() => new SpellkitCollectionEnumerator(arr, start, count, obj);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class SpellkitCollectionEnumerator : IEnumerator<SpellkitObject>
{
    private readonly SpellkitObject[] arr;
    private readonly int count;
    private readonly SpellkitCollection obj;
    private readonly int version;
    private readonly int start;
    private int index = -1;

    public SpellkitCollectionEnumerator(SpellkitObject[] arr, int start, int count, SpellkitCollection obj)
    {
        this.arr = arr;
        this.start = start;
        this.count = count;
        this.obj = obj;
        version = obj.Version;
    }

    public SpellkitObject Current => arr[index + start] is SpellkitLabel lab ? lab.Value : arr[index + start];

    object IEnumerator.Current => Current;

    public void Dispose() { }

    public bool MoveNext() =>
        version != obj.Version ? throw new IterationException() : ++index < count;

    public void Reset() => index = -1;
}

[SpellkitType]
internal abstract partial class SpellkitCollTypeInfo : SpellkitTypeInfo
{
    #region Operations
    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject self) =>
        SpellkitInteger.Get(((SpellkitEnumerable)self).Count);

    protected override SpellkitObject IterateOp(ExecutionContext ctx, SpellkitObject self)
    {
        if (self is IEnumerable<SpellkitObject> seq)
        {
            return SpellkitIterator.Create(seq);
        }

        return Nil;
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == self.TypeId)
        {
            return self;
        }

        var xs = (SpellkitCollection)self;
        return targetType.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Tuple => new SpellkitTuple(xs.ToArray()),
            SpellkitTypeCodes.Array => new SpellkitArray(xs.ToArray()),
            SpellkitTypeCodes.Iterator => SpellkitIterator.Create(xs),
            SpellkitTypeCodes.Set => new SpellkitSet(new HashSet<SpellkitObject>(xs.ToArray())),
            _ => base.CastOp(ctx, self, targetType)
        };
    }
    #endregion

    [SpellkitMethod(BuiltinMethodNames.Indices)]
    internal static IEnumerable<SpellkitObject> Indices(SpellkitCollection self)
    {
        IEnumerable<SpellkitObject> Iterate()
        {
            for (var i = 0; i < self.Count; i++)
            {
                yield return SpellkitInteger.Get(i);
            }
        }

        return Iterate();
    }

    [SpellkitMethod(BuiltinMethodNames.Slice)]
    internal static IEnumerable<SpellkitObject> Slice(SpellkitCollection self, int index, [Default]int? size)
    {
        var arr = self switch
        {
            SpellkitArray array => array.UnsafeAccess(),
            SpellkitTuple tuple => tuple.UnsafeAccess(),
            _ => self.ToArray()
        };

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

        if (index < 0 || index >= self.Count)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        if (size < 0)
        {
            size = self.Count + size - 1;
        }

        if (size >= self.Count || size < 0)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        var len = size.Value - index + 1;

        if (len < 0)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        return new SpellkitCollectionEnumerable(arr, index, len, self);
    }

    [SpellkitMethod(BuiltinMethodNames.ToSet)]
    internal static HashSet<SpellkitObject> ToSet(SpellkitCollection self) => new (self.ToArray());
}

public abstract class SpellkitEnumerable : SpellkitObject, IEnumerable<SpellkitObject>, IMeasurable
{
    internal protected int Version { get; protected set; }

    internal void MarkModified() => Version++;

    public virtual int Count { get; protected set; }

    protected SpellkitEnumerable(int typeCode) : base(typeCode) { }

    public abstract IEnumerator<SpellkitObject> GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override int GetHashCode() => HashCode.Combine(TypeId, Count, Version);
}

public interface IMeasurable
{
    int Count { get; }
}
