using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;

namespace Spellkit.Runtime.Types;

public class SpellkitArray : SpellkitCollection, IEnumerable<SpellkitObject>
{
    private const int DefaultSize = 4;

    private SpellkitObject[] values;

    public override string TypeName => nameof(SpellkitTypeCodes.Array);

    public SpellkitObject this[int index]
    {
        get => values[index];
        set => values[index] = value;
    }

    public SpellkitArray(SpellkitObject[] values) : base(SpellkitTypeCodes.Array) =>
        (this.values, Count) = (values, values.Length);

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    private int CorrectIndex(int index) => index < 0 ? values.Length + index : index;

    public void Compact()
    {
        if (Count == values.Length)
        {
            return;
        }

        var arr = new SpellkitObject[Count];
        Array.Copy(values, arr, Count);
        values = arr;
    }

    public void RemoveRange(int start, int count)
    {
        var xs = new List<SpellkitObject>(values);
        xs.RemoveRange(start, count);
        values = xs.ToArray();
        Count = values.Length;
        Version++;
    }

    public void Add(SpellkitObject val)
    {
        if (Count == values.Length)
        {
            var dest = new SpellkitObject[values.Length == 0 ? DefaultSize : values.Length * 2];
            Array.Copy(values, 0, dest, 0, Count);
            values = dest;
        }

        values[Count++] = val;
        Version++;
    }

    public void Insert(int index, SpellkitObject item)
    {
        index = CorrectIndex(index);

        if (index < 0 || index > Count)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, index);
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

        static SpellkitObject[] EnsureSize(int size, SpellkitObject[] values)
        {
            if (size > values.Length)
            {
                var exp = values.Length * 2;

                if (size > exp)
                {
                    exp = size;
                }

                var arr = new SpellkitObject[exp];
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
        values = new SpellkitObject[DefaultSize];
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

    internal int IndexOf(ExecutionContext ctx, SpellkitObject value)
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

    public int LastIndexOf(ExecutionContext ctx, SpellkitObject value)
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

    public override IEnumerator<SpellkitObject> GetEnumerator() => new SpellkitCollectionEnumerator(values, 0, Count, this);

    public override SpellkitObject[] ToArray()
    {
        var arr = new SpellkitObject[Count];

        for (var i = 0; i < Count; i++)
        {
            arr[i] = values[i];
        }

        return arr;
    }

    internal SpellkitObject[] UnsafeAccess() => values;
}

[SpellkitType]
internal sealed partial class SpellkitArrayTypeInfo : SpellkitCollTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Array);

    public override int ReflectedTypeId => SpellkitTypeCodes.Array;

    public SpellkitArrayTypeInfo() => AddMixins(SpellkitTypeCodes.Sequence, SpellkitTypeCodes.Collection);

    #region Operations
    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        try
        {
            return new SpellkitString("[" + ((IEnumerable<SpellkitObject>)arg).ToLiteral(ctx) + "]");
        }
        catch (SpellkitCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        var arr = new List<SpellkitObject>();
        arr.AddRange(SpellkitIterator.ToEnumerable(ctx, left));
        if (ctx.HasErrors)
        {
            return Nil;
        }

        arr.AddRange(SpellkitIterator.ToEnumerable(ctx, right));
        if (ctx.HasErrors)
        {
            return Nil;
        }

        return new SpellkitArray(arr.ToArray());
    }

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index)
    {
        if (index is SpellkitInteger i)
        {
            var arr = (SpellkitArray)self;
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

    protected override SpellkitObject SetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index, SpellkitObject value)
    {
        if (index is SpellkitInteger i)
        {
            var arr = (SpellkitArray)self;
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

    internal static bool CorrectIndex(SpellkitArray arr, ref int index, bool insert = false)
    {
        index = index < 0 ? arr.Count + index : index;
        var max = insert ? arr.Count : arr.Count - 1;

        if (index < 0 || index > max)
        {
            return false;
        }

        return true;
    }

    [SpellkitMethod]
    internal static bool Contains(ExecutionContext ctx, SpellkitArray self, SpellkitObject item) => self.IndexOf(ctx, item) != -1;

    [SpellkitMethod(BuiltinMethodNames.Add)]
    internal static void AddItem(SpellkitArray self, SpellkitObject value) => self.Add(value);

    [SpellkitMethod(BuiltinMethodNames.Insert)]
    internal static void InsertItem(SpellkitArray self, int index, SpellkitObject value)
    {
        if (!CorrectIndex(self, ref index, insert: true))
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, index);
        }

        self.Insert(index, value);
    }

    [SpellkitMethod(BuiltinMethodNames.AddRange)]
    internal static void AddRange(SpellkitArray self, IEnumerable<SpellkitObject> values)
    {
        foreach (var o in values)
        {
            self.Add(o);
        }
    }

    [SpellkitMethod(BuiltinMethodNames.InsertRange)]
    internal static void InsertRange(SpellkitArray self, int index, IEnumerable<SpellkitObject> values)
    {
        if (!CorrectIndex(self, ref index, insert: true))
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, index);
        }

        foreach (var e in values)
        {
            self.Insert(index++, e);
        }
    }

    [SpellkitMethod(BuiltinMethodNames.Remove)]
    internal static bool RemoveItem(ExecutionContext ctx, SpellkitArray self, SpellkitObject value)
    {
        var ix = self.IndexOf(ctx, value);

        if (ctx.HasErrors || ix == -1)
        {
            return false;
        }

        self.RemoveAt(ix);
        return true;
    }

    [SpellkitMethod(BuiltinMethodNames.RemoveAt)]
    internal static void RemoveItemAt(SpellkitArray self, int index)
    {
        if (!CorrectIndex(self, ref index))
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, index);
        }

        self.RemoveAt(index);
    }

    [SpellkitMethod(BuiltinMethodNames.RemoveRange)]
    internal static void RemoveRange(ExecutionContext ctx, SpellkitArray self, IEnumerable<SpellkitObject> values)
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

    [SpellkitMethod(BuiltinMethodNames.RemoveRangeAt)]
    internal static void RemoveRangeAt(SpellkitArray self, int index, int? count = null)
    {
        if (!CorrectIndex(self, ref index))
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, index);
        }

        count ??= self.Count - index;

        if (count < 0 || count > self.Count - index)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        self.RemoveRange(index, count.Value);
    }

    [SpellkitMethod(BuiltinMethodNames.RemoveAll)]
    internal static void RemoveAll(ExecutionContext ctx, SpellkitArray self, SpellkitFunction predicate)
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

    [SpellkitMethod(BuiltinMethodNames.Clear)]
    internal static void ClearItems(SpellkitArray self) => self.Clear();

    [SpellkitMethod(BuiltinMethodNames.IndexOf)]
    internal static int IndexOf(ExecutionContext ctx, SpellkitArray self, SpellkitObject value) => self.IndexOf(ctx, value);

    [SpellkitMethod(BuiltinMethodNames.LastIndexOf)]
    internal static int LastIndexOf(ExecutionContext ctx, SpellkitArray self, SpellkitObject value) => self.LastIndexOf(ctx, value);

    [SpellkitMethod(BuiltinMethodNames.Sort)]
    internal static void SortBy(ExecutionContext ctx, SpellkitArray self, SpellkitFunction? comparer = null)
    {
        var sortComparer = new SortComparer(comparer, ctx);
        self.Compact();
        if (self.Count > 1)
        {
            self.MarkModified();
        }

        Array.Sort(self.UnsafeAccess(), 0, self.Count, sortComparer);
    }

    [SpellkitMethod(BuiltinMethodNames.Swap)]
    internal static void Swap(SpellkitArray self, int index, int other)
    {
        if (!CorrectIndex(self, ref index))
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, index);
        }

        if (!CorrectIndex(self, ref other))
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, other);
        }

        self.Swap(index, other);
    }

    [SpellkitMethod(BuiltinMethodNames.Compact)]
    internal static void Compact(ExecutionContext ctx, SpellkitArray self, SpellkitFunction? predicate = null)
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
                flag = e.TypeId == SpellkitTypeCodes.Nil;
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

    [SpellkitMethod(BuiltinMethodNames.Reverse)]
    internal static void Reverse(SpellkitArray self)
    {
        self.Compact();
        if (self.Count > 1)
        {
            self.MarkModified();
            Array.Reverse(self.UnsafeAccess());
        }
    }

    [SpellkitStaticMethod(BuiltinMethodNames.Array)]
    internal static SpellkitObject[] New(params SpellkitObject[] values) => values;

    [SpellkitStaticMethod(BuiltinMethodNames.Sort)]
    internal static SpellkitObject StaticSortBy(ExecutionContext ctx, SpellkitObject values, SpellkitFunction comparer)
    {
        var arr = values;

        if (values.TypeId != SpellkitTypeCodes.Array)
        {
            arr = ctx.RuntimeContext.Types[values.TypeId].Cast(ctx, values, ctx.RuntimeContext.Array);

            if (ctx.HasErrors)
            {
                return Nil;
            }
        }

        SortBy(ctx, (SpellkitArray)arr, comparer);
        return arr;
    }

    [SpellkitStaticMethod(BuiltinMethodNames.Empty)]
    internal static SpellkitObject[] Empty(ExecutionContext ctx, int count, [ParameterName("default")] SpellkitObject? def = null)
    {
        var arr = new SpellkitObject[count];
        def ??= Nil;

        if (def.TypeId == SpellkitTypeCodes.Iterator)
        {
            def = ((SpellkitIterator)def).GetIteratorFunction();
        }

        if (def is SpellkitFunction func)
        {
            for (var i = 0; i < count; i++)
            {
                var res = func.Call(ctx);

                if (ctx.HasErrors)
                {
                    return Array.Empty<SpellkitObject>();
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

    [SpellkitStaticMethod(BuiltinMethodNames.Concat)]
    internal static SpellkitObject[] Concat(ExecutionContext ctx, params SpellkitObject[] values) =>
        SpellkitCollection.ConcatValues(ctx, values);

    [SpellkitStaticMethod(BuiltinMethodNames.Copy)]
    internal static SpellkitObject Copy(SpellkitArray source, int index = 0, SpellkitArray? destination = null, int destinationIndex = 0, int? count = null)
    {
        count ??= source.Count - index;
        destination ??= new SpellkitArray(new SpellkitObject[destinationIndex + count.Value]);

        if (index < 0 || index >= source.Count)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        if (destinationIndex < 0 || destinationIndex >= destination.Count)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        if (index + count < 0 || index + count > source.Count)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        if (destinationIndex + count < 0 || destinationIndex + count > destination.Count)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        Array.Copy(source.UnsafeAccess(), index, destination.UnsafeAccess(), destinationIndex, count.Value);
        return destination;
    }
}

internal sealed class SortComparer : IComparer<SpellkitObject>
{
    private readonly SpellkitFunction? func;
    private readonly ExecutionContext ctx;

    public SortComparer(SpellkitFunction? functor, ExecutionContext ctx)
    {
        this.func = functor;
        this.ctx = ctx;
    }

    public int Compare(SpellkitObject? x, SpellkitObject? y)
    {
        if (x is null || y is null)
        {
            return 0;
        }

        if (x is SpellkitLabel la1)
        {
            x = la1.Value;
        }

        if (y is SpellkitLabel la2)
        {
            y = la2.Value;
        }

        if (func is not null)
        {
            var ret = func.Call(ctx, x, y);
            ctx.ThrowIf();
            return ret switch
            {
                SpellkitInteger i => i.Value.CompareTo(0),
                SpellkitFloat f when !double.IsNaN(f.Value) => f.Value.CompareTo(0),
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
