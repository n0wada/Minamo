using System.Collections.Generic;
using Minamo.Codegen;
using System.Linq;

namespace Minamo.Runtime.Types;

public class MinamoArray : MinamoCollection, IEnumerable<MinamoObject>
{
    private const int DefaultSize = 4;

    private MinamoObject[] values;

    public override string TypeName => nameof(MinamoTypeCodes.Array);

    public MinamoObject this[int index]
    {
        get => values[index];
        set => values[index] = value;
    }

    public MinamoArray(MinamoObject[] values) : base(MinamoTypeCodes.Array) =>
        (this.values, Count) = (values, values.Length);

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    private int CorrectIndex(int index) => index < 0 ? values.Length + index : index;

    public void Compact()
    {
        if (Count == values.Length)
        {
            return;
        }

        var arr = new MinamoObject[Count];
        Array.Copy(values, arr, Count);
        values = arr;
    }

    public void RemoveRange(int start, int count)
    {
        var remaining = Count - count;
        var result = new MinamoObject[remaining];
        Array.Copy(values, 0, result, 0, start);
        Array.Copy(values, start + count, result, start, remaining - start);
        values = result;
        Count = remaining;
        Version++;
    }

    public void Add(MinamoObject val)
    {
        if (Count == values.Length)
        {
            var dest = new MinamoObject[values.Length == 0 ? DefaultSize : values.Length * 2];
            Array.Copy(values, 0, dest, 0, Count);
            values = dest;
        }

        values[Count++] = val;
        Version++;
    }

    public void Insert(int index, MinamoObject item)
    {
        index = CorrectIndex(index);

        if (index < 0 || index > Count)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, index);
        }

        if (index == Count && values.Length > index)
        {
            values[index] = item;
            Count++;
            Version++;
            return;
        }

        values = EnsureSize(Count + 1, values);
        Array.Copy(values, index, values, index + 1, Count - index);
        values[index] = item;
        Count++;
        Version++;

        static MinamoObject[] EnsureSize(int size, MinamoObject[] values)
        {
            if (size > values.Length)
            {
                var exp = values.Length * 2;

                if (size > exp)
                {
                    exp = size;
                }

                var arr = new MinamoObject[exp];
                Array.Copy(values, arr, values.Length);
                return arr;
            }

            return values;
        }
    }

    public void RemoveAt(int index)
    {
        index = CorrectIndex(index);

        if (index < 0 || index >= Count)
        {
            throw new IndexOutOfRangeException();
        }

        Count--;
        Array.Copy(values, index + 1, values, index, Count - index);
        values[Count] = null!;
        Version++;
    }

    public void Clear()
    {
        Count = 0;
        values = new MinamoObject[DefaultSize];
        Version++;
    }

    public void Swap(int index, int other)
    {
        if (index == other)
        {
            return;
        }

        (values[index], values[other]) = (values[other], values[index]);
        Version++;
    }

    internal int IndexOf(ExecutionContext ctx, MinamoObject value)
    {
        for (var i = 0; i < Count; i++)
        {
            var e = values[i];

            if (e.Equals(value, ctx))
            {
                return i;
            }
        }

        return -1;
    }

    public int LastIndexOf(ExecutionContext ctx, MinamoObject value)
    {
        var index = -1;

        for (var i = 0; i < Count; i++)
        {
            var e = values[i];

            if (e.Equals(value, ctx))
            {
                index = i;
            }

            if (ctx.HasErrors)
            {
                return -1;
            }
        }

        return index;
    }

    public override IEnumerator<MinamoObject> GetEnumerator() => new MinamoCollectionEnumerator(values, 0, Count, this);

    public override MinamoObject[] ToArray()
    {
        var arr = new MinamoObject[Count];

        for (var i = 0; i < Count; i++)
        {
            arr[i] = values[i];
        }

        return arr;
    }

    internal MinamoObject[] UnsafeAccess() => values;
}

[MinamoType]
internal sealed partial class MinamoArrayTypeInfo : MinamoCollTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Array);

    public override int ReflectedTypeId => MinamoTypeCodes.Array;

    public MinamoArrayTypeInfo() => AddMixins(MinamoTypeCodes.Sequence, MinamoTypeCodes.Collection);

    #region Operations
    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        try
        {
            return new MinamoString("[" + ((IEnumerable<MinamoObject>)arg).ToLiteral(ctx) + "]");
        }
        catch (MinamoCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        var arr = new List<MinamoObject>();
        arr.AddRange(MinamoIterator.ToEnumerable(ctx, left));
        if (ctx.HasErrors)
        {
            return Nil;
        }

        arr.AddRange(MinamoIterator.ToEnumerable(ctx, right));
        if (ctx.HasErrors)
        {
            return Nil;
        }

        return new MinamoArray(arr.ToArray());
    }

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index)
    {
        if (index is MinamoInteger i)
        {
            var arr = (MinamoArray)self;
            if (!i.TryGetInt32(out var ix))
            {
                return ctx.IndexOutOfRange(index);
            }

            if (!CorrectIndex(arr, ref ix, insert: false))
            {
                return ctx.IndexOutOfRange(index);
            }

            return arr[ix];
        }

        return ctx.IndexOutOfRange(index);
    }

    protected override MinamoObject SetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index, MinamoObject value)
    {
        if (index is MinamoInteger i)
        {
            var arr = (MinamoArray)self;
            if (!i.TryGetInt32(out var ix))
            {
                return ctx.IndexOutOfRange(index);
            }

            if (!CorrectIndex(arr, ref ix, insert: false))
            {
                return ctx.IndexOutOfRange(index);
            }

            arr[ix] = value;
            return Nil;
        }

        return ctx.InvalidType(index);
    }
    #endregion

    internal static bool CorrectIndex(MinamoArray arr, ref int index, bool insert = false)
    {
        index = index < 0 ? arr.Count + index : index;
        var max = insert ? arr.Count : arr.Count - 1;

        if (index < 0 || index > max)
        {
            return false;
        }

        return true;
    }

    [MinamoMethod]
    internal static bool Contains(ExecutionContext ctx, MinamoArray self, MinamoObject item) => self.IndexOf(ctx, item) != -1;

    [MinamoMethod(BuiltinMethodNames.Add)]
    internal static void AddItem(MinamoArray self, MinamoObject value) => self.Add(value);

    [MinamoMethod(BuiltinMethodNames.Insert)]
    internal static void InsertItem(MinamoArray self, int index, MinamoObject value)
    {
        if (!CorrectIndex(self, ref index, insert: true))
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, index);
        }

        self.Insert(index, value);
    }

    [MinamoMethod(BuiltinMethodNames.AddRange)]
    internal static void AddRange(MinamoArray self, IEnumerable<MinamoObject> values)
    {
        foreach (var o in values)
        {
            self.Add(o);
        }
    }

    [MinamoMethod(BuiltinMethodNames.InsertRange)]
    internal static void InsertRange(MinamoArray self, int index, IEnumerable<MinamoObject> values)
    {
        if (!CorrectIndex(self, ref index, insert: true))
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, index);
        }

        foreach (var e in values)
        {
            self.Insert(index++, e);
        }
    }

    [MinamoMethod(BuiltinMethodNames.Remove)]
    internal static bool RemoveItem(ExecutionContext ctx, MinamoArray self, MinamoObject value)
    {
        var ix = self.IndexOf(ctx, value);

        if (ctx.HasErrors || ix == -1)
        {
            return false;
        }

        self.RemoveAt(ix);
        return true;
    }

    [MinamoMethod(BuiltinMethodNames.RemoveAt)]
    internal static void RemoveItemAt(MinamoArray self, int index)
    {
        if (!CorrectIndex(self, ref index))
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, index);
        }

        self.RemoveAt(index);
    }

    [MinamoMethod(BuiltinMethodNames.RemoveRange)]
    internal static void RemoveRange(ExecutionContext ctx, MinamoArray self, IEnumerable<MinamoObject> values)
    {
        var strict = values.ToArray();

        foreach (var e in strict)
        {
            var ix = self.IndexOf(ctx, e);

            if (ctx.HasErrors)
            {
                break;
            }

            if (ix >= 0)
            {
                self.RemoveAt(ix);
            }
        }
    }

    [MinamoMethod(BuiltinMethodNames.RemoveRangeAt)]
    internal static void RemoveRangeAt(MinamoArray self, int index, int? count = null)
    {
        if (!CorrectIndex(self, ref index))
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, index);
        }

        count ??= self.Count - index;

        if (count < 0 || count > self.Count - index)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        self.RemoveRange(index, count.Value);
    }

    [MinamoMethod(BuiltinMethodNames.RemoveAll)]
    internal static void RemoveAll(ExecutionContext ctx, MinamoArray self, MinamoFunction predicate)
    {
        var toDelete = new List<int>();

        for (var i = 0; i < self.Count; i++)
        {
            var o = self[i];
            var res = predicate.Call(ctx, o);

            if (!res.IsFalse())
            {
                toDelete.Add(i);
            }
        }

        var shift = 0;

        foreach (var ix in toDelete)
        {
            self.RemoveAt(ix + shift);
            shift--;
        }
    }

    [MinamoMethod(BuiltinMethodNames.Clear)]
    internal static void ClearItems(MinamoArray self) => self.Clear();

    [MinamoMethod(BuiltinMethodNames.IndexOf)]
    internal static int IndexOf(ExecutionContext ctx, MinamoArray self, MinamoObject value) => self.IndexOf(ctx, value);

    [MinamoMethod(BuiltinMethodNames.LastIndexOf)]
    internal static int LastIndexOf(ExecutionContext ctx, MinamoArray self, MinamoObject value) => self.LastIndexOf(ctx, value);

    [MinamoMethod(BuiltinMethodNames.Sort)]
    internal static void SortBy(ExecutionContext ctx, MinamoArray self, MinamoFunction? comparer = null)
    {
        var sortComparer = new SortComparer(comparer, ctx);
        self.Compact();
        if (self.Count > 1)
        {
            self.MarkModified();
        }

        Array.Sort(self.UnsafeAccess(), 0, self.Count, sortComparer);
    }

    [MinamoMethod(BuiltinMethodNames.Swap)]
    internal static void Swap(MinamoArray self, int index, int other)
    {
        if (!CorrectIndex(self, ref index))
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, index);
        }

        if (!CorrectIndex(self, ref other))
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, other);
        }

        self.Swap(index, other);
    }

    [MinamoMethod(BuiltinMethodNames.Compact)]
    internal static void Compact(ExecutionContext ctx, MinamoArray self, MinamoFunction? predicate = null)
    {
        if (self.Count == 0)
        {
            return;
        }

        var idx = 0;

        while (idx < self.Count)
        {
            var e = self[idx];
            bool flag;

            if (predicate is not null)
            {
                var res = predicate.Call(ctx, e);
                flag = res.IsTrue();
            }
            else
            {
                flag = e.TypeId == MinamoTypeCodes.Nil;
            }

            if (flag)
            {
                self.RemoveAt(idx);
            }
            else
            {
                idx++;
            }
        }
    }

    [MinamoMethod(BuiltinMethodNames.Reverse)]
    internal static void Reverse(MinamoArray self)
    {
        self.Compact();
        if (self.Count > 1)
        {
            self.MarkModified();
            Array.Reverse(self.UnsafeAccess());
        }
    }

    [MinamoStaticMethod(BuiltinMethodNames.Array)]
    internal static MinamoObject[] New(params MinamoObject[] values) => values;

    [MinamoStaticMethod(BuiltinMethodNames.Sort)]
    internal static MinamoObject StaticSortBy(ExecutionContext ctx, MinamoObject values, MinamoFunction comparer)
    {
        var arr = values;

        if (values.TypeId != MinamoTypeCodes.Array)
        {
            arr = ctx.RuntimeContext.Types[values.TypeId].Cast(ctx, values, ctx.RuntimeContext.Array);

            if (ctx.HasErrors)
            {
                return Nil;
            }
        }

        SortBy(ctx, (MinamoArray)arr, comparer);
        return arr;
    }

    [MinamoStaticMethod(BuiltinMethodNames.Empty)]
    internal static MinamoObject[] Empty(ExecutionContext ctx, int count, [ParameterName("default")] MinamoObject? def = null)
    {
        var arr = new MinamoObject[count];
        def ??= Nil;

        if (def.TypeId == MinamoTypeCodes.Iterator)
        {
            def = ((MinamoIterator)def).GetIteratorFunction();
        }

        if (def is MinamoFunction func)
        {
            for (var i = 0; i < count; i++)
            {
                var res = func.Call(ctx);

                if (ctx.HasErrors)
                {
                    return Array.Empty<MinamoObject>();
                }

                arr[i] = res;
            }
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                arr[i] = def;
            }
        }

        return arr;
    }

    [MinamoStaticMethod(BuiltinMethodNames.Concat)]
    internal static MinamoObject[] Concat(ExecutionContext ctx, params MinamoObject[] values) =>
        MinamoCollection.ConcatValues(ctx, values);

    [MinamoStaticMethod(BuiltinMethodNames.Copy)]
    internal static MinamoObject Copy(MinamoArray source, int index = 0, MinamoArray? destination = null, int destinationIndex = 0, int? count = null)
    {
        count ??= source.Count - index;
        destination ??= new MinamoArray(new MinamoObject[destinationIndex + count.Value]);

        if (index < 0 || index >= source.Count)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        if (destinationIndex < 0 || destinationIndex >= destination.Count)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        if (index + count < 0 || index + count > source.Count)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        if (destinationIndex + count < 0 || destinationIndex + count > destination.Count)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        Array.Copy(source.UnsafeAccess(), index, destination.UnsafeAccess(), destinationIndex, count.Value);
        return destination;
    }
}

internal sealed class SortComparer : IComparer<MinamoObject>
{
    private readonly MinamoFunction? func;
    private readonly ExecutionContext ctx;

    public SortComparer(MinamoFunction? functor, ExecutionContext ctx)
    {
        this.func = functor;
        this.ctx = ctx;
    }

    public int Compare(MinamoObject? x, MinamoObject? y)
    {
        if (x is null || y is null)
        {
            return 0;
        }

        if (x is MinamoLabel la1)
        {
            x = la1.Value;
        }

        if (y is MinamoLabel la2)
        {
            y = la2.Value;
        }

        if (func is not null)
        {
            var ret = func.Call(ctx, x, y);
            ctx.ThrowIf();
            return ret switch
            {
                MinamoInteger i => i.Value.CompareTo(0),
                MinamoFloat f when !double.IsNaN(f.Value) => f.Value.CompareTo(0),
                _ => 0
            };
        }

        var res = x.Greater(y, ctx);
        ctx.ThrowIf();

        if (res)
        {
            return 1;
        }

        res = x.Equals(y, ctx);
        ctx.ThrowIf();
        return res ? 0 : -1;
    }
}
