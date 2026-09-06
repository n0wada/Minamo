using System.Collections.Generic;
using System.Collections;
using Minamo.Codegen;

namespace Minamo.Runtime.Types;

public abstract class MinamoCollection : MinamoEnumerable
{
    protected MinamoCollection(int typeCode) : base(typeCode) { }

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

    public abstract MinamoObject[] ToArray();

    internal static MinamoObject[] ConcatValues(ExecutionContext ctx, params MinamoObject[] values)
    {
        if (values is null)
        {
            return Array.Empty<MinamoObject>();
        }

        var arr = new List<MinamoObject>();

        for (var i = 0; i < values.Length; i++)
        {
            var seq = MinamoIterator.ToEnumerable(ctx, values[i]);

            if (ctx.HasErrors)
            {
                break;
            }

            arr.AddRange(seq);
        }

        return arr.ToArray();
    }
}

internal sealed class MinamoCollectionEnumerable : IEnumerable<MinamoObject>
{
    private readonly MinamoObject[] arr;
    private readonly int count;
    private readonly MinamoCollection obj;
    private readonly int start;

    public MinamoCollectionEnumerable(MinamoObject[] arr, int start, int count, MinamoCollection obj)
    {
        this.arr = arr;
        this.start = start;
        this.count = count;
        this.obj = obj;
    }

    public IEnumerator<MinamoObject> GetEnumerator() => new MinamoCollectionEnumerator(arr, start, count, obj);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class MinamoCollectionEnumerator : IEnumerator<MinamoObject>
{
    private readonly MinamoObject[] arr;
    private readonly int count;
    private readonly MinamoCollection obj;
    private readonly int version;
    private readonly int start;
    private int index = -1;

    public MinamoCollectionEnumerator(MinamoObject[] arr, int start, int count, MinamoCollection obj)
    {
        this.arr = arr;
        this.start = start;
        this.count = count;
        this.obj = obj;
        version = obj.Version;
    }

    public MinamoObject Current => arr[index + start] is MinamoLabel lab ? lab.Value : arr[index + start];

    object IEnumerator.Current => Current;

    public void Dispose() { }

    public bool MoveNext() =>
        version != obj.Version ? throw new IterationException() : ++index < count;

    public void Reset() => index = -1;
}

[MinamoType]
internal abstract partial class MinamoCollTypeInfo : MinamoTypeInfo
{
    #region Operations
    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject self) =>
        MinamoInteger.Get(((MinamoEnumerable)self).Count);

    protected override MinamoObject IterateOp(ExecutionContext ctx, MinamoObject self)
    {
        if (self is IEnumerable<MinamoObject> seq)
        {
            return MinamoIterator.Create(seq);
        }

        return Nil;
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == self.TypeId)
        {
            return self;
        }

        var xs = (MinamoCollection)self;
        return targetType.ReflectedTypeId switch
        {
            MinamoTypeCodes.Tuple => new MinamoTuple(xs.ToArray()),
            MinamoTypeCodes.Array => new MinamoArray(xs.ToArray()),
            MinamoTypeCodes.Iterator => MinamoIterator.Create(xs),
            MinamoTypeCodes.Set => new MinamoSet(xs.ToArray()),
            _ => base.CastOp(ctx, self, targetType)
        };
    }
    #endregion

    [MinamoMethod(BuiltinMethodNames.Indices)]
    internal static IEnumerable<MinamoObject> Indices(MinamoCollection self)
    {
        IEnumerable<MinamoObject> Iterate()
        {
            for (var i = 0; i < self.Count; i++)
            {
                yield return MinamoInteger.Get(i);
            }
        }

        return Iterate();
    }

    [MinamoMethod(BuiltinMethodNames.Slice)]
    internal static IEnumerable<MinamoObject> Slice(MinamoCollection self, int index, [Default]int? size)
    {
        var arr = self switch
        {
            MinamoArray array => array.UnsafeAccess(),
            MinamoTuple tuple => tuple.UnsafeAccess(),
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
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        if (size < 0)
        {
            size = self.Count + size - 1;
        }

        if (size >= self.Count || size < 0)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        var len = size.Value - index + 1;

        if (len < 0)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        return new MinamoCollectionEnumerable(arr, index, len, self);
    }

    [MinamoMethod(BuiltinMethodNames.ToSet)]
    internal static HashSet<MinamoObject> ToSet(MinamoCollection self) =>
        new(self.ToArray(), MinamoObjectKeyComparer.Instance);
}

public abstract class MinamoEnumerable : MinamoObject, IEnumerable<MinamoObject>, IMeasurable
{
    internal protected int Version { get; protected set; }

    internal void MarkModified() => Version++;

    public virtual int Count { get; protected set; }

    protected MinamoEnumerable(int typeCode) : base(typeCode) { }

    public abstract IEnumerator<MinamoObject> GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override int GetHashCode() => HashCode.Combine(TypeId, Count, Version);
}

public interface IMeasurable
{
    int Count { get; }
}
