using Spellkit.Compiler;
using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;

namespace Spellkit.Runtime.Types;

public class SpkTuple : SpkCollection
{
    public static readonly SpkTuple Empty = new(Array.Empty<SpkObject>());

    public override string TypeName => nameof(Spk.Tuple);

    private readonly int length;
    private bool? mutable;
    private readonly SpkObject[] values;

    public override int Count => length;

    public bool IsVarArg { get; }

    public SpkObject this[int index]
    {
        get => values[index] is SpkLabel la ? la.Value : values[index];
        set
        {
            if (values[index] is SpkLabel la)
            {
                la.Value = value;
            }

            values[index] = value;
        }
    }

    public SpkTuple(SpkObject[] values) : this(values, values.Length) { }

    internal SpkTuple(SpkObject[] values, bool mutable, bool vararg) : this(values, values.Length) =>
        (this.mutable, IsVarArg) = (mutable, vararg);

    public SpkTuple(SpkObject[] values, int length) : base(Spk.Tuple)
    {
        this.length = length;
        this.values = values ?? throw new SpkException("Unable to create a tuple with no values.");
    }

    public static SpkTuple Create(params SpkLabel[] values) => new(values, values.Length);

    public override IEnumerator<SpkObject> GetEnumerator() => new SpkCollectionEnumerator(values, 0, Count, this);

    public override SpkObject Clone()
    {
        if (IsMutable())
        {
            return base.Clone();
        }

        return this;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;

            for (var i = 0; i < length; i++)
            {
                var v = values[i];
                hash = hash * 31 + v.GetHashCode();
            }

            return hash;
        }
    }

    public override bool Equals(SpkObject? other)
    {
        if (other is null || other is not SpkTuple xs)
        {
            return false;
        }

        if (xs.Count != length)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            if (!values[i].Equals(xs.values[i]))
            {
                return false;
            }
        }

        return true;
    }

    public Dictionary<SpkObject, SpkObject> ConvertToDictionary()
    {
        var dict = new Dictionary<SpkObject, SpkObject>();

        for (var i = 0; i < Count; i++)
        {
            var ki = GetKeyInfo(i);
            var v = this[i];
            var key = new SpkString(ki is null ? DefaultKey() : ki.Label);
            dict[key] = v;
        }

        return dict;
    }

    internal bool TryGetItem(string name, out SpkObject item)
    {
        item = null!;
        var i = GetOrdinal(name);

        if (i is -1)
        {
            return false;
        }

        item = this[i];
        return true;
    }

    internal bool TrySetItem(string name, SpkObject value)
    {
        var i = GetOrdinal(name);

        if (i is -1)
        {
            return false;
        }

        var item = values[i];

        if (item is SpkLabel lab)
        {
            lab.Value = value;
        }
        else
        {
            values[i] = value;
        }

        return true;
    }

    internal SpkObject GetItem(ExecutionContext ctx, SpkObject index)
    {
        if (index is SpkInteger ix)
        {
            return ix.TryGetInt32(out var value)
                ? GetItem(ctx, value)
                : ctx.IndexOutOfRange(index);
        }

        if (index.TypeId is Spk.String or Spk.Char && TryGetItem(index.ToString(), out var item))
        {
            return item;
        }

        return ctx.IndexOutOfRange(index);
    }

    internal SpkObject GetItem(ExecutionContext ctx, int index)
    {
        index = index < 0 ? Count + index : index;

        if (index < 0 || index >= Count)
        {
            return ctx.IndexOutOfRange(index);
        }

        var item = values[index];

        if (item is SpkLabel lab)
        {
            item = lab.Value;
        }

        return item;
    }

    internal void SetItem(ExecutionContext ctx, SpkObject index, SpkObject value)
    {
        int ix = -1;

        if (index.TypeId is Spk.String or Spk.Char)
        {
            ix = GetOrdinal(index.ToString());
        }
        else if (index is SpkInteger i)
        {
            if (!i.TryGetInt32(out ix))
            {
                ctx.IndexOutOfRange(index);
                return;
            }

            ix = ix < 0 ? Count + ix : ix;
        }

        if (ix < 0 || ix >= Count)
        {
            ctx.IndexOutOfRange(index);
            return;
        }

        if (values[ix] is SpkLabel lab && lab.Mutable)
        {
            if (!lab.VerifyType(value.TypeId))
            {
                ctx.InvalidType(value);
                return;
            }

            lab.Value = value;
        }
        else
        {
            ctx.IndexReadOnly(index);
        }
    }

    public virtual int GetOrdinal(string name)
    {
        for (var i = 0; i < Count; i++)
        {
            if (values[i] is SpkLabel la && la.Label == name)
            {
                return i;
            }
        }

        return -1;
    }

    public virtual bool IsReadOnly(int index) => values[index] is SpkLabel lab && !lab.Mutable;

    internal virtual string? GetKey(int index) => values[index] is SpkLabel la ? la.Label : null;

    private static string DefaultKey() => Guid.NewGuid().ToString();

    internal virtual void SetValue(int index, SpkObject value)
    {
        if (values[index] is SpkLabel lab)
        {
            lab.Value = value;
        }
        else
        {
            values[index] = value;
        }
    }

    internal virtual SpkLabel? GetKeyInfo(int index) => values[index] is SpkLabel lab ? lab : null;

    public override SpkObject[] ToArray()
    {
        if (Count != values.Length)
        {
            return CopyTuple();
        }

        for (var i = 0; i < Count; i++)
        {
            if (values[i].TypeId == Spk.Label)
            {
                return CopyTuple();
            }
        }

        return values;
    }

    internal SpkObject[] GetValuesWithLabels()
    {
        if (mutable != null)
        {
            if (!mutable.Value && Count == values.Length)
            {
                return values;
            }
            else
            {
                return CopyTupleWithLabels();
            }
        }

        if (Count != values.Length)
        {
            return CopyTupleWithLabels();
        }

        if (IsMutable())
        {
            return CopyTupleWithLabels();
        }

        return values;
    }

    private SpkObject[] CopyTuple()
    {
        var arr = new SpkObject[Count];

        for (var i = 0; i < Count; i++)
        {
            arr[i] = values[i] is SpkLabel la ? la.Value : values[i];
        }

        return arr;
    }

    private bool IsMutable()
    {
        if (mutable is not null)
        {
            return mutable.Value;
        }

        for (var i = 0; i < Count; i++)
        {
            if (values[i] is SpkLabel la && la.Mutable)
            {
                mutable = true;
                return true;
            }
        }

        mutable = false;
        return false;
    }

    private SpkObject[] CopyTupleWithLabels()
    {
        var arr = new SpkObject[Count];

        for (var i = 0; i < Count; i++)
        {
            arr[i] = values[i] is SpkLabel la ? new SpkLabel(la.Label, la.Value) : values[i];
        }

        return arr;
    }

    internal bool HasItem(string name) => GetOrdinal(name) is not -1;

    internal protected override SpkObject[] UnsafeAccess() => values;

    private static SpkObject Compare(bool gt, SpkTuple xs, SpkTuple ys, ExecutionContext ctx)
    {
        var xsv = xs.UnsafeAccess();
        var ysv = ys.UnsafeAccess();
        var len = xs.Count > ys.Count ? ys.Count : xs.Count;

        for (var i = 0; i < len; i++)
        {
            var x = xsv[i] is SpkLabel lx ? lx.Value : xsv[i];
            var y = ysv[i] is SpkLabel ly ? ly.Value : ysv[i];
            var res = gt ? x.Greater(y, ctx) : x.Lesser(y, ctx);

            if (res)
            {
                return True;
            }

            res = x.Equals(y, ctx);

            if (!res)
            {
                return False;
            }
        }

        return False;
    }

    internal static SpkObject Greater(ExecutionContext ctx, SpkTuple xs, SpkTuple ys) => Compare(true, xs, ys, ctx);

    internal static SpkObject Lesser(ExecutionContext ctx, SpkTuple xs, SpkTuple ys) => Compare(false, xs, ys, ctx);

    internal static SpkObject Equals(ExecutionContext ctx, SpkTuple xs, SpkTuple ys)
    {
        if (xs.Count != ys.Count)
        {
            return False;
        }

        var t1v = xs.UnsafeAccess();
        var t2v = ys.UnsafeAccess();

        for (var i = 0; i < xs.Count; i++)
        {
            var x = t1v[i] is SpkLabel lx ? lx.Value : t1v[i];
            var y = t2v[i] is SpkLabel ly ? ly.Value : t2v[i];

            if (x.NotEquals(y, ctx))
            {
                return False;
            }
        }

        return True;
    }
}

[SpkType]
internal sealed partial class SpkTupleTypeInfo : SpkCollTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Tuple);

    public override int ReflectedTypeId => Spk.Tuple;

    public SpkTupleTypeInfo()
    {
        AddMixins(Spk.Container, Spk.Order, Spk.Collection, Spk.Equatable, Spk.Sequence);
        SetSupportedOperations(Ops.Add);
    }

    #region Operations
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

        return new SpkTuple(arr.ToArray());
    }

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        IEnumerable<SpkObject> Iterate()
        {
            var tuple = (SpkTuple)arg;
            var xs = tuple.UnsafeAccess();
            for (var i = 0; i < tuple.Count; i++)
            {
                yield return xs[i];
            }
        }

        try
        {
            return new SpkString("(" + Iterate().ToLiteral(ctx) + ")");
        }
        catch (SpkCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return False;
        }

        var (xs, ys) = ((SpkTuple)left, (SpkTuple)right);

        try
        {
            return SpkTuple.Equals(ctx, xs, ys);
        }
        catch (SpkCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpkObject GtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.OperationNotSupported(Builtins.Gt, left, right);
        }

        try
        {
            return SpkTuple.Greater(ctx, (SpkTuple)left, (SpkTuple)right);
        }
        catch (SpkCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpkObject LtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.OperationNotSupported(Builtins.Lt, left, right);
        }

        try
        {
            return SpkTuple.Lesser(ctx, (SpkTuple)left, (SpkTuple)right);
        }
        catch (SpkCodeException e)
        {
            ctx.Error = e.Error;
            return Nil;
        }
    }

    protected override SpkObject InOp(ExecutionContext ctx, SpkObject self, SpkObject field)
    {
        if (field.TypeId is not Spk.String and not Spk.Char)
        {
            return ctx.InvalidType(field);
        }

        return ((SpkTuple)self).GetOrdinal(field.ToString()) is not -1 ? True : False;
    }

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index) =>
        ((SpkTuple)self).GetItem(ctx, index);

    protected override SpkObject SetOp(ExecutionContext ctx, SpkObject self, SpkObject index, SpkObject value)
    {
        ((SpkTuple)self).SetItem(ctx, index, value);
        return Nil;
    }

    internal override void SetInstanceMember(ExecutionContext ctx, HashString name, SpkFunction func)
    {
        if ((string)name is Builtins.Get or Builtins.Set or Builtins.Length)
        {
            ctx.OverloadProhibited(this, (string)name);
            return;
        }

        base.SetInstanceMember(ctx, name, func);
    }
    #endregion

    [SpkMethod]
    internal static bool ContainsField(SpkTuple self, string field) =>
        self.GetOrdinal(field.ToString()) is not -1;

    [SpkMethod(BuiltinMethodNames.Add)]
    internal static SpkObject AddItem(SpkTuple self, SpkObject value)
    {
        var arr = new SpkObject[self.Count + 1];
        Array.Copy(self.UnsafeAccess(), arr, self.Count);
        arr[^1] = value;
        return new SpkTuple(arr);
    }

    [SpkMethod(BuiltinMethodNames.Remove)]
    internal static SpkObject Remove(ExecutionContext ctx, SpkTuple self, SpkObject value)
    {
        var tv = self.UnsafeAccess();

        for (var i = 0; i < tv.Length; i++)
        {
            var e = tv[i] is SpkLabel la ? la.Value : tv[i];

            if (e.Equals(value, ctx))
            {
                return InternalRemoveAt(self, i);
            }
        }

        return self;
    }

    [SpkMethod]
    internal static SpkObject RemoveField(SpkTuple self, string field)
    {
        var tv = self.UnsafeAccess();

        for (var i = 0; i < tv.Length; i++)
        {
            if (tv[i] is SpkLabel la && la.Label == field)
            {
                return InternalRemoveAt(self, i);
            }
        }

        return self;
    }

    [SpkMethod(BuiltinMethodNames.RemoveAt)]
    internal static SpkObject RemoveAt(SpkTuple self, int index)
    {
        index = index < 0 ? self.Count + index : index;

        if (index < 0 || index >= self.Count)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, index);
        }

        return InternalRemoveAt(self, index);
    }

    internal static SpkTuple InternalRemoveAt(SpkTuple self, int index)
    {
        var arr = new SpkObject[self.Count - 1];
        var c = 0;
        var sv = self.UnsafeAccess();

        for (var i = 0; i < self.Count; i++)
        {
            if (i != index)
            {
                arr[c++] = sv[i];
            }
        }

        return new SpkTuple(arr);
    }

    [SpkMethod(BuiltinMethodNames.Insert)]
    internal static SpkObject Insert(SpkTuple self, int index, SpkObject value)
    {
        index = index < 0 ? self.Count + index : index;

        if (index < 0 || index > self.Count)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, index);
        }

        var arr = new SpkObject[self.Count + 1];
        arr[index] = value;

        if (index == 0)
        {
            Array.Copy(self.UnsafeAccess(), 0, arr, 1, self.Count);
        }
        else if (index == self.Count)
        {
            Array.Copy(self.UnsafeAccess(), 0, arr, 0, self.Count);
        }
        else
        {
            Array.Copy(self.UnsafeAccess(), 0, arr, 0, index);
            Array.Copy(self.UnsafeAccess(), index, arr, index + 1, self.Count - index);
        }

        return new SpkTuple(arr);
    }

    [SpkMethod(BuiltinMethodNames.Keys)]
    internal static SpkObject Keys(SpkTuple self)
    {
        IEnumerable<SpkObject> Iterate()
        {
            for (var i = 0; i < self.Count; i++)
            {
                var k = self.GetKey(i);
                if (k is not null)
                {
                    yield return new SpkString(k);
                }
            }
        }

        return SpkIterator.Create(Iterate());
    }

    [SpkMethod(BuiltinMethodNames.First)]
    internal static SpkObject First(ExecutionContext ctx, SpkTuple self)
    {
        var ret = self.GetItem(ctx, 0);
        ctx.ThrowIf();
        return ret;
    }

    [SpkMethod(BuiltinMethodNames.Second)]
    internal static SpkObject Second(ExecutionContext ctx, SpkTuple self)
    {
        var ret = self.GetItem(ctx, 1);
        ctx.ThrowIf();
        return ret;
    }

    [SpkMethod(BuiltinMethodNames.Sort)]
    internal static SpkObject Sort(ExecutionContext ctx, SpkTuple self, SpkFunction? comparer = null)
    {
        var sortComparer = new SortComparer(comparer, ctx);
        var newArr = new SpkObject[self.Count];
        Array.Copy(self.UnsafeAccess(), newArr, newArr.Length);
        Array.Sort(newArr, 0, newArr.Length, sortComparer);
        return new SpkTuple(newArr);
    }

    [SpkMethod(BuiltinMethodNames.ToDictionary)]
    internal static SpkObject ToDictionary(SpkTuple self) =>
        new SpkDictionary(self.ConvertToDictionary());

    [SpkMethod(BuiltinMethodNames.ToArray)]
    internal static SpkObject[] ToArray(SpkCollection self) => self.ToArray();

    [SpkMethod(BuiltinMethodNames.Compact)]
    internal static SpkObject Compact(ExecutionContext ctx, SpkTuple self, SpkFunction? predicate = null)
    {
        var xs = new List<SpkObject>();

        foreach (var val in self.ToArray())
        {
            if (predicate is not null)
            {
                var res = predicate.Invoke(ctx, val);

                if (ctx.HasErrors)
                {
                    return Nil;
                }

                if (res.IsFalse())
                {
                    xs.Add(val);
                }
            }
            else if (!val.Is(Spk.Nil))
            {
                xs.Add(val);
            }
        }

        return new SpkTuple(xs.ToArray());
    }

    [SpkMethod(BuiltinMethodNames.Alter)]
    internal static SpkObject Alter(SpkTuple self, [VarArg]SpkTuple values)
    {
        var xs = new List<SpkObject>(self.UnsafeAccess());

        foreach (var o in values.UnsafeAccess())
        {
            if (o is SpkLabel lab)
            {
                var exist = xs.OfType<SpkLabel>().FirstOrDefault(i => i.Label == lab.Label);

                if (exist is not null)
                {
                    exist.Value = lab.Value;
                    continue;
                }
            }

            xs.Add(o);
        }

        return new SpkTuple(xs.ToArray());
    }

    [SpkStaticMethod(BuiltinMethodNames.Sort)]
    internal static SpkObject StaticSort(ExecutionContext ctx, SpkTuple value, SpkFunction? comparer = null) =>
        Sort(ctx, value, comparer);

    [SpkStaticMethod(BuiltinMethodNames.Pair)]
    internal static SpkObject Pair(SpkObject first, SpkObject second) =>
        new SpkTuple(new[] { first, second });

    [SpkStaticMethod(BuiltinMethodNames.Triple)]
    internal static SpkObject Triple(SpkObject first, SpkObject second, SpkObject third) =>
        new SpkTuple(new[] { first, second, third });

    [SpkStaticMethod(BuiltinMethodNames.Concat)]
    internal static SpkObject StaticConcat(ExecutionContext ctx, params SpkObject[] values) =>
        new SpkTuple(SpkCollection.ConcatValues(ctx, values));

    [SpkStaticMethod(BuiltinMethodNames.Tuple)]
    internal static SpkObject MakeNew([VarArg]SpkObject values) => values;
}
