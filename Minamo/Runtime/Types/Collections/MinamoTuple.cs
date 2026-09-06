using Minamo.Compiler;
using System.Collections.Generic;
using Minamo.Codegen;
using System.Linq;

namespace Minamo.Runtime.Types;

public class MinamoTuple : MinamoCollection
{
    public static readonly MinamoTuple Empty = new(Array.Empty<MinamoObject>());

    public override string TypeName => nameof(MinamoTypeCodes.Tuple);

    private readonly int length;
    private bool? mutable;
    private readonly MinamoObject[] values;

    public override int Count => length;

    public bool IsVarArg { get; }

    public MinamoObject this[int index]
    {
        get => values[index] is MinamoLabel la ? la.Value : values[index];
        set
        {
            if (values[index] is MinamoLabel la)
            {
                la.Value = value;
            }

            values[index] = value;
        }
    }

    public MinamoTuple(MinamoObject[] values) : this(values, values.Length) { }

    internal MinamoTuple(MinamoObject[] values, bool mutable, bool vararg) : this(values, values.Length) =>
        (this.mutable, IsVarArg) = (mutable, vararg);

    public MinamoTuple(MinamoObject[] values, int length) : base(MinamoTypeCodes.Tuple)
    {
        this.length = length;
        this.values = values ?? throw new MinamoException("Unable to create a tuple with no values.");
    }

    public static MinamoTuple Create(params MinamoLabel[] values) => new(values, values.Length);

    public override IEnumerator<MinamoObject> GetEnumerator() => new MinamoCollectionEnumerator(values, 0, Count, this);

    public override MinamoObject Clone()
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

    public override bool Equals(MinamoObject? other)
    {
        if (other is null || other is not MinamoTuple xs)
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

    public Dictionary<MinamoObject, MinamoObject> ConvertToDictionary()
    {
        var dict = new Dictionary<MinamoObject, MinamoObject>();

        for (var i = 0; i < Count; i++)
        {
            var ki = GetKeyInfo(i);
            var v = this[i];
            var key = new MinamoString(ki is null ? DefaultKey() : ki.Label);
            dict[key] = v;
        }

        return dict;
    }

    internal MinamoDictionary ToMinamoDictionary()
    {
        var dictionary = new MinamoDictionary();

        for (var i = 0; i < Count; i++)
        {
            var keyInfo = GetKeyInfo(i);
            var key = new MinamoString(keyInfo is null ? DefaultKey() : keyInfo.Label);
            dictionary.Dictionary[key] = this[i];
        }

        return dictionary;
    }

    internal bool TryGetItem(string name, out MinamoObject item)
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

    internal bool TrySetItem(string name, MinamoObject value)
    {
        var i = GetOrdinal(name);

        if (i is -1)
        {
            return false;
        }

        var item = values[i];

        if (item is MinamoLabel lab)
        {
            lab.Value = value;
        }
        else
        {
            values[i] = value;
        }

        return true;
    }

    internal MinamoObject GetItem(ExecutionContext ctx, MinamoObject index)
    {
        if (index is MinamoInteger ix)
        {
            return ix.TryGetInt32(out var value)
                ? GetItem(ctx, value)
                : ctx.IndexOutOfRange(index);
        }

        if (index.TypeId is MinamoTypeCodes.String or MinamoTypeCodes.Char && TryGetItem(index.ToString(), out var item))
        {
            return item;
        }

        return ctx.IndexOutOfRange(index);
    }

    internal MinamoObject GetItem(ExecutionContext ctx, int index)
    {
        index = index < 0 ? Count + index : index;

        if (index < 0 || index >= Count)
        {
            return ctx.IndexOutOfRange(index);
        }

        var item = values[index];

        if (item is MinamoLabel lab)
        {
            item = lab.Value;
        }

        return item;
    }

    internal void SetItem(ExecutionContext ctx, MinamoObject index, MinamoObject value)
    {
        int ix = -1;

        if (index.TypeId is MinamoTypeCodes.String or MinamoTypeCodes.Char)
        {
            ix = GetOrdinal(index.ToString());
        }
        else if (index is MinamoInteger i)
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

        if (values[ix] is MinamoLabel lab && lab.Mutable)
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
            if (values[i] is MinamoLabel la && la.Label == name)
            {
                return i;
            }
        }

        return -1;
    }

    public virtual bool IsReadOnly(int index) => values[index] is MinamoLabel lab && !lab.Mutable;

    internal virtual string? GetKey(int index) => values[index] is MinamoLabel la ? la.Label : null;

    private static string DefaultKey() => Guid.NewGuid().ToString();

    internal virtual void SetValue(int index, MinamoObject value)
    {
        if (values[index] is MinamoLabel lab)
        {
            lab.Value = value;
        }
        else
        {
            values[index] = value;
        }
    }

    internal virtual MinamoLabel? GetKeyInfo(int index) => values[index] is MinamoLabel lab ? lab : null;

    public override MinamoObject[] ToArray()
    {
        if (Count != values.Length)
        {
            return CopyTuple();
        }

        for (var i = 0; i < Count; i++)
        {
            if (values[i].TypeId == MinamoTypeCodes.Label)
            {
                return CopyTuple();
            }
        }

        return values;
    }

    internal MinamoObject[] GetValuesWithLabels()
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

    private MinamoObject[] CopyTuple()
    {
        var arr = new MinamoObject[Count];

        for (var i = 0; i < Count; i++)
        {
            arr[i] = values[i] is MinamoLabel la ? la.Value : values[i];
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
            if (values[i] is MinamoLabel la && la.Mutable)
            {
                mutable = true;
                return true;
            }
        }

        mutable = false;
        return false;
    }

    private MinamoObject[] CopyTupleWithLabels()
    {
        var arr = new MinamoObject[Count];

        for (var i = 0; i < Count; i++)
        {
            arr[i] = values[i] is MinamoLabel la ? new MinamoLabel(la.Label, la.Value) : values[i];
        }

        return arr;
    }

    internal bool HasItem(string name) => GetOrdinal(name) is not -1;

    internal MinamoObject[] UnsafeAccess() => values;

    private static MinamoObject Compare(bool gt, MinamoTuple xs, MinamoTuple ys, ExecutionContext ctx)
    {
        var xsv = xs.UnsafeAccess();
        var ysv = ys.UnsafeAccess();
        var len = xs.Count > ys.Count ? ys.Count : xs.Count;

        for (var i = 0; i < len; i++)
        {
            var x = xsv[i] is MinamoLabel lx ? lx.Value : xsv[i];
            var y = ysv[i] is MinamoLabel ly ? ly.Value : ysv[i];
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

    internal static MinamoObject Greater(ExecutionContext ctx, MinamoTuple xs, MinamoTuple ys) => Compare(true, xs, ys, ctx);

    internal static MinamoObject Lesser(ExecutionContext ctx, MinamoTuple xs, MinamoTuple ys) => Compare(false, xs, ys, ctx);

    internal static MinamoObject Equals(ExecutionContext ctx, MinamoTuple xs, MinamoTuple ys)
    {
        if (xs.Count != ys.Count)
        {
            return False;
        }

        var t1v = xs.UnsafeAccess();
        var t2v = ys.UnsafeAccess();

        for (var i = 0; i < xs.Count; i++)
        {
            var x = t1v[i] is MinamoLabel lx ? lx.Value : t1v[i];
            var y = t2v[i] is MinamoLabel ly ? ly.Value : t2v[i];

            if (x.NotEquals(y, ctx))
            {
                return False;
            }
        }

        return True;
    }
}

[MinamoType]
internal sealed partial class MinamoTupleTypeInfo : MinamoCollTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Tuple);

    public override int ReflectedTypeId => MinamoTypeCodes.Tuple;

    public MinamoTupleTypeInfo()
    {
        AddMixins(MinamoTypeCodes.Container, MinamoTypeCodes.Order, MinamoTypeCodes.Collection, MinamoTypeCodes.Equatable, MinamoTypeCodes.Sequence);
        SetSupportedOperations(Ops.Add);
    }

    #region Operations
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

        return new MinamoTuple(arr.ToArray());
    }

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        IEnumerable<MinamoObject> Iterate()
        {
            var tuple = (MinamoTuple)arg;
            var xs = tuple.UnsafeAccess();
            for (var i = 0; i < tuple.Count; i++)
            {
                yield return xs[i];
            }
        }

        try
        {
            return new MinamoString("(" + Iterate().ToLiteral(ctx) + ")");
        }
        catch (MinamoCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return False;
        }

        var (xs, ys) = ((MinamoTuple)left, (MinamoTuple)right);

        try
        {
            return MinamoTuple.Equals(ctx, xs, ys);
        }
        catch (MinamoCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override MinamoObject GtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.OperationNotSupported(Builtins.Gt, left, right);
        }

        try
        {
            return MinamoTuple.Greater(ctx, (MinamoTuple)left, (MinamoTuple)right);
        }
        catch (MinamoCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override MinamoObject LtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.OperationNotSupported(Builtins.Lt, left, right);
        }

        try
        {
            return MinamoTuple.Lesser(ctx, (MinamoTuple)left, (MinamoTuple)right);
        }
        catch (MinamoCodeException e)
        {
            ctx.Error = e.Error;
            return Nil;
        }
    }

    protected override MinamoObject InOp(ExecutionContext ctx, MinamoObject self, MinamoObject field)
    {
        if (field.TypeId is not MinamoTypeCodes.String and not MinamoTypeCodes.Char)
        {
            return ctx.InvalidType(field);
        }

        return ((MinamoTuple)self).GetOrdinal(field.ToString()) is not -1 ? True : False;
    }

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index) =>
        ((MinamoTuple)self).GetItem(ctx, index);

    protected override MinamoObject SetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index, MinamoObject value)
    {
        ((MinamoTuple)self).SetItem(ctx, index, value);
        return Nil;
    }

    internal override void SetInstanceMember(ExecutionContext ctx, HashString name, MinamoFunction func)
    {
        if ((string)name is Builtins.Get or Builtins.Set or Builtins.Length)
        {
            ctx.OverloadProhibited(this, (string)name);
            return;
        }

        base.SetInstanceMember(ctx, name, func);
    }
    #endregion

    [MinamoMethod]
    internal static bool ContainsField(MinamoTuple self, string field) =>
        self.GetOrdinal(field.ToString()) is not -1;

    [MinamoMethod(BuiltinMethodNames.Add)]
    internal static MinamoObject AddItem(MinamoTuple self, MinamoObject value)
    {
        var arr = new MinamoObject[self.Count + 1];
        Array.Copy(self.UnsafeAccess(), arr, self.Count);
        arr[^1] = value;
        return new MinamoTuple(arr);
    }

    [MinamoMethod(BuiltinMethodNames.Remove)]
    internal static MinamoObject Remove(ExecutionContext ctx, MinamoTuple self, MinamoObject value)
    {
        var tv = self.UnsafeAccess();

        for (var i = 0; i < tv.Length; i++)
        {
            var e = tv[i] is MinamoLabel la ? la.Value : tv[i];

            if (e.Equals(value, ctx))
            {
                return InternalRemoveAt(self, i);
            }
        }

        return self;
    }

    [MinamoMethod]
    internal static MinamoObject RemoveField(MinamoTuple self, string field)
    {
        var tv = self.UnsafeAccess();

        for (var i = 0; i < tv.Length; i++)
        {
            if (tv[i] is MinamoLabel la && la.Label == field)
            {
                return InternalRemoveAt(self, i);
            }
        }

        return self;
    }

    [MinamoMethod(BuiltinMethodNames.RemoveAt)]
    internal static MinamoObject RemoveAt(MinamoTuple self, int index)
    {
        index = index < 0 ? self.Count + index : index;

        if (index < 0 || index >= self.Count)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, index);
        }

        return InternalRemoveAt(self, index);
    }

    internal static MinamoTuple InternalRemoveAt(MinamoTuple self, int index)
    {
        var arr = new MinamoObject[self.Count - 1];
        var c = 0;
        var sv = self.UnsafeAccess();

        for (var i = 0; i < self.Count; i++)
        {
            if (i != index)
            {
                arr[c++] = sv[i];
            }
        }

        return new MinamoTuple(arr);
    }

    [MinamoMethod(BuiltinMethodNames.Insert)]
    internal static MinamoObject Insert(MinamoTuple self, int index, MinamoObject value)
    {
        index = index < 0 ? self.Count + index : index;

        if (index < 0 || index > self.Count)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, index);
        }

        var arr = new MinamoObject[self.Count + 1];
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

        return new MinamoTuple(arr);
    }

    [MinamoMethod(BuiltinMethodNames.Keys)]
    internal static MinamoObject Keys(MinamoTuple self)
    {
        IEnumerable<MinamoObject> Iterate()
        {
            for (var i = 0; i < self.Count; i++)
            {
                var k = self.GetKey(i);
                if (k is not null)
                {
                    yield return new MinamoString(k);
                }
            }
        }

        return MinamoIterator.Create(Iterate());
    }

    [MinamoMethod(BuiltinMethodNames.First)]
    internal static MinamoObject First(ExecutionContext ctx, MinamoTuple self)
    {
        var ret = self.GetItem(ctx, 0);
        ctx.ThrowIf();
        return ret;
    }

    [MinamoMethod(BuiltinMethodNames.Second)]
    internal static MinamoObject Second(ExecutionContext ctx, MinamoTuple self)
    {
        var ret = self.GetItem(ctx, 1);
        ctx.ThrowIf();
        return ret;
    }

    [MinamoMethod(BuiltinMethodNames.Sort)]
    internal static MinamoObject Sort(ExecutionContext ctx, MinamoTuple self, MinamoFunction? comparer = null)
    {
        var sortComparer = new SortComparer(comparer, ctx);
        var newArr = new MinamoObject[self.Count];
        Array.Copy(self.UnsafeAccess(), newArr, newArr.Length);
        Array.Sort(newArr, 0, newArr.Length, sortComparer);
        return new MinamoTuple(newArr);
    }

    [MinamoMethod(BuiltinMethodNames.ToDictionary)]
    internal static MinamoObject ToDictionary(MinamoTuple self) =>
        self.ToMinamoDictionary();

    [MinamoMethod(BuiltinMethodNames.ToArray)]
    internal static MinamoObject[] ToArray(MinamoCollection self) => self.ToArray();

    [MinamoMethod(BuiltinMethodNames.Compact)]
    internal static MinamoObject Compact(ExecutionContext ctx, MinamoTuple self, MinamoFunction? predicate = null)
    {
        var xs = new List<MinamoObject>();

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
            else if (!val.Is(MinamoTypeCodes.Nil))
            {
                xs.Add(val);
            }
        }

        return new MinamoTuple(xs.ToArray());
    }

    [MinamoMethod(BuiltinMethodNames.Alter)]
    internal static MinamoObject Alter(MinamoTuple self, [VarArg]MinamoTuple values)
    {
        var xs = new List<MinamoObject>(self.UnsafeAccess());

        foreach (var o in values.UnsafeAccess())
        {
            if (o is MinamoLabel lab)
            {
                var exist = xs.OfType<MinamoLabel>().FirstOrDefault(i => i.Label == lab.Label);

                if (exist is not null)
                {
                    exist.Value = lab.Value;
                    continue;
                }
            }

            xs.Add(o);
        }

        return new MinamoTuple(xs.ToArray());
    }

    [MinamoStaticMethod(BuiltinMethodNames.Sort)]
    internal static MinamoObject StaticSort(ExecutionContext ctx, MinamoTuple value, MinamoFunction? comparer = null) =>
        Sort(ctx, value, comparer);

    [MinamoStaticMethod(BuiltinMethodNames.Pair)]
    internal static MinamoObject Pair(MinamoObject first, MinamoObject second) =>
        new MinamoTuple(new[] { first, second });

    [MinamoStaticMethod(BuiltinMethodNames.Triple)]
    internal static MinamoObject Triple(MinamoObject first, MinamoObject second, MinamoObject third) =>
        new MinamoTuple(new[] { first, second, third });

    [MinamoStaticMethod(BuiltinMethodNames.Concat)]
    internal static MinamoObject StaticConcat(ExecutionContext ctx, params MinamoObject[] values) =>
        new MinamoTuple(MinamoCollection.ConcatValues(ctx, values));

    [MinamoStaticMethod(BuiltinMethodNames.Tuple)]
    internal static MinamoObject MakeNew([VarArg]MinamoObject values) => values;
}
