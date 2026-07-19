using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;

namespace Spellkit.Runtime.Types;

public class SpkArray : SpkCollection, IEnumerable<SpkObject>
{
    private const int DefaultSize = 4;

    private SpkObject[] values;

    public override string TypeName => nameof(Spk.Array);

    public SpkObject this[int index]
    {
        get => values[index];
        set => values[index] = value;
    }

    public SpkArray(SpkObject[] values) : base(Spk.Array) =>
        (this.values, Count) = (values, values.Length);

    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);

    private int CorrectIndex(int index) => index < 0 ? values.Length + index : index;

    public void Compact()
    {
        if (Count == values.Length)
        {
            return;
        }

        var arr = new SpkObject[Count];
        Array.Copy(values, arr, Count);
        values = arr;
    }

    public void RemoveRange(int start, int count)
    {
        var xs = new List<SpkObject>(values);
        xs.RemoveRange(start, count);
        values = xs.ToArray();
        Count = values.Length;
        Version++;
    }

    public void Add(SpkObject val)
    {
        if (Count == values.Length)
        {
            var dest = new SpkObject[values.Length == 0 ? DefaultSize : values.Length * 2];
            Array.Copy(values, 0, dest, 0, Count);
            values = dest;
        }

        values[Count++] = val;
        Version++;
    }

    public void Insert(int index, SpkObject item)
    {
        index = CorrectIndex(index);

        if (index < 0 || index > Count)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, index);
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

        static SpkObject[] EnsureSize(int size, SpkObject[] values)
        {
            if (size > values.Length)
            {
                var exp = values.Length * 2;

                if (size > exp)
                {
                    exp = size;
                }

                var arr = new SpkObject[exp];
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
        values = new SpkObject[DefaultSize];
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

    internal int IndexOf(ExecutionContext ctx, SpkObject value)
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

    public int LastIndexOf(ExecutionContext ctx, SpkObject value)
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

    public override IEnumerator<SpkObject> GetEnumerator() => new SpkCollectionEnumerator(values, 0, Count, this);

    public override SpkObject[] ToArray()
    {
        var arr = new SpkObject[Count];

        for (var i = 0; i < Count; i++)
        {
            arr[i] = values[i];
        }

        return arr;
    }

    internal protected override SpkObject[] UnsafeAccess() => values;
}

[SpkType]
internal sealed partial class SpkArrayTypeInfo : SpkCollTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Array);

    public override int ReflectedTypeId => Spk.Array;

    public SpkArrayTypeInfo() => AddMixins(Spk.Sequence, Spk.Collection);

    #region Operations
    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        try
        {
            return new SpkString("[" + ((IEnumerable<SpkObject>)arg).ToLiteral(ctx) + "]");
        }
        catch (SpkCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        var arr = new List<SpkObject>();
        arr.AddRange(SpkIterator.ToEnumerable(ctx, left));
        if (ctx.HasErrors)
        {
            return Nil;
        }

        arr.AddRange(SpkIterator.ToEnumerable(ctx, right));
        if (ctx.HasErrors)
        {
            return Nil;
        }

        return new SpkArray(arr.ToArray());
    }

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index)
    {
        if (index is SpkInteger i)
        {
            var arr = (SpkArray)self;
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

    protected override SpkObject SetOp(ExecutionContext ctx, SpkObject self, SpkObject index, SpkObject value)
    {
        if (index is SpkInteger i)
        {
            var arr = (SpkArray)self;
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

    internal static bool CorrectIndex(SpkArray arr, ref int index, bool insert = false)
    {
        index = index < 0 ? arr.Count + index : index;
        var max = insert ? arr.Count : arr.Count - 1;

        if (index < 0 || index > max)
        {
            return false;
        }

        return true;
    }

    [SpkMethod]
    internal static bool Contains(ExecutionContext ctx, SpkArray self, SpkObject item) => self.IndexOf(ctx, item) != -1;

    [SpkMethod(BuiltinMethodNames.Add)]
    internal static void AddItem(SpkArray self, SpkObject value) => self.Add(value);

    [SpkMethod(BuiltinMethodNames.Insert)]
    internal static void InsertItem(SpkArray self, int index, SpkObject value)
    {
        if (!CorrectIndex(self, ref index, insert: true))
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, index);
        }

        self.Insert(index, value);
    }

    [SpkMethod(BuiltinMethodNames.AddRange)]
    internal static void AddRange(SpkArray self, IEnumerable<SpkObject> values)
    {
        foreach (var o in values)
        {
            self.Add(o);
        }
    }

    [SpkMethod(BuiltinMethodNames.InsertRange)]
    internal static void InsertRange(SpkArray self, int index, IEnumerable<SpkObject> values)
    {
        if (!CorrectIndex(self, ref index, insert: true))
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, index);
        }

        foreach (var e in values)
        {
            self.Insert(index++, e);
        }
    }

    [SpkMethod(BuiltinMethodNames.Remove)]
    internal static bool RemoveItem(ExecutionContext ctx, SpkArray self, SpkObject value)
    {
        var ix = self.IndexOf(ctx, value);

        if (ctx.HasErrors || ix == -1)
        {
            return false;
        }

        self.RemoveAt(ix);
        return true;
    }

    [SpkMethod(BuiltinMethodNames.RemoveAt)]
    internal static void RemoveItemAt(SpkArray self, int index)
    {
        if (!CorrectIndex(self, ref index))
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, index);
        }

        self.RemoveAt(index);
    }

    [SpkMethod(BuiltinMethodNames.RemoveRange)]
    internal static void RemoveRange(ExecutionContext ctx, SpkArray self, IEnumerable<SpkObject> values)
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

    [SpkMethod(BuiltinMethodNames.RemoveRangeAt)]
    internal static void RemoveRangeAt(SpkArray self, int index, int? count = null)
    {
        if (!CorrectIndex(self, ref index))
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, index);
        }

        count ??= self.Count - index;

        if (count < 0 || count > self.Count - index)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        self.RemoveRange(index, count.Value);
    }

    [SpkMethod(BuiltinMethodNames.RemoveAll)]
    internal static void RemoveAll(ExecutionContext ctx, SpkArray self, SpkFunction predicate)
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

    [SpkMethod(BuiltinMethodNames.Clear)]
    internal static void ClearItems(SpkArray self) => self.Clear();

    [SpkMethod(BuiltinMethodNames.IndexOf)]
    internal static int IndexOf(ExecutionContext ctx, SpkArray self, SpkObject value) => self.IndexOf(ctx, value);

    [SpkMethod(BuiltinMethodNames.LastIndexOf)]
    internal static int LastIndexOf(ExecutionContext ctx, SpkArray self, SpkObject value) => self.LastIndexOf(ctx, value);

    [SpkMethod(BuiltinMethodNames.Sort)]
    internal static void SortBy(ExecutionContext ctx, SpkArray self, SpkFunction? comparer = null)
    {
        var sortComparer = new SortComparer(comparer, ctx);
        self.Compact();
        if (self.Count > 1)
        {
            self.MarkModified();
        }

        Array.Sort(self.UnsafeAccess(), 0, self.Count, sortComparer);
    }

    [SpkMethod(BuiltinMethodNames.Swap)]
    internal static void Swap(SpkArray self, int index, int other)
    {
        if (!CorrectIndex(self, ref index))
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, index);
        }

        if (!CorrectIndex(self, ref other))
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, other);
        }

        self.Swap(index, other);
    }

    [SpkMethod(BuiltinMethodNames.Compact)]
    internal static void Compact(ExecutionContext ctx, SpkArray self, SpkFunction? predicate = null)
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
                flag = e.TypeId == Spk.Nil;
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

    [SpkMethod(BuiltinMethodNames.Reverse)]
    internal static void Reverse(SpkArray self)
    {
        self.Compact();
        if (self.Count > 1)
        {
            self.MarkModified();
            Array.Reverse(self.UnsafeAccess());
        }
    }

    [SpkStaticMethod(BuiltinMethodNames.Array)]
    internal static SpkObject[] New(params SpkObject[] values) => values;

    [SpkStaticMethod(BuiltinMethodNames.Sort)]
    internal static SpkObject StaticSortBy(ExecutionContext ctx, SpkObject values, SpkFunction comparer)
    {
        var arr = values;

        if (values.TypeId != Spk.Array)
        {
            arr = ctx.RuntimeContext.Types[values.TypeId].Cast(ctx, values, ctx.RuntimeContext.Array);

            if (ctx.HasErrors)
            {
                return Nil;
            }
        }

        SortBy(ctx, (SpkArray)arr, comparer);
        return arr;
    }

    [SpkStaticMethod(BuiltinMethodNames.Empty)]
    internal static SpkObject[] Empty(ExecutionContext ctx, int count, [ParameterName("default")] SpkObject? def = null)
    {
        var arr = new SpkObject[count];
        def ??= Nil;

        if (def.TypeId == Spk.Iterator)
        {
            def = ((SpkIterator)def).GetIteratorFunction();
        }

        if (def is SpkFunction func)
        {
            for (var i = 0; i < count; i++)
            {
                var res = func.Call(ctx);

                if (ctx.HasErrors)
                {
                    return Array.Empty<SpkObject>();
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

    [SpkStaticMethod(BuiltinMethodNames.Concat)]
    internal static SpkObject[] Concat(ExecutionContext ctx, params SpkObject[] values) =>
        SpkCollection.ConcatValues(ctx, values);

    [SpkStaticMethod(BuiltinMethodNames.Copy)]
    internal static SpkObject Copy(SpkArray source, int index = 0, SpkArray? destination = null, int destinationIndex = 0, int? count = null)
    {
        count ??= source.Count - index;
        destination ??= new SpkArray(new SpkObject[destinationIndex + count.Value]);

        if (index < 0 || index >= source.Count)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        if (destinationIndex < 0 || destinationIndex >= destination.Count)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        if (index + count < 0 || index + count > source.Count)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        if (destinationIndex + count < 0 || destinationIndex + count > destination.Count)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        Array.Copy(source.UnsafeAccess(), index, destination.UnsafeAccess(), destinationIndex, count.Value);
        return destination;
    }
}

internal sealed class SortComparer : IComparer<SpkObject>
{
    private readonly SpkFunction? func;
    private readonly ExecutionContext ctx;

    public SortComparer(SpkFunction? functor, ExecutionContext ctx)
    {
        this.func = functor;
        this.ctx = ctx;
    }

    public int Compare(SpkObject? x, SpkObject? y)
    {
        if (x is null || y is null)
        {
            return 0;
        }

        if (x is SpkLabel la1)
        {
            x = la1.Value;
        }

        if (y is SpkLabel la2)
        {
            y = la2.Value;
        }

        if (func is not null)
        {
            var ret = func.Call(ctx, x, y);
            ctx.ThrowIf();
            return ret switch
            {
                SpkInteger i => i.Value.CompareTo(0),
                SpkFloat f when !double.IsNaN(f.Value) => f.Value.CompareTo(0),
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
