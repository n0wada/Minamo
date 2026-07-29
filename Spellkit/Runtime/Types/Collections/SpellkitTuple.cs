using Spellkit.Compiler;
using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;

namespace Spellkit.Runtime.Types;

public class SpellkitTuple : SpellkitCollection
{
    public static readonly SpellkitTuple Empty = new(Array.Empty<SpellkitObject>());

    public override string TypeName => nameof(SpellkitTypeCodes.Tuple);

    private readonly int length;
    private bool? mutable;
    private readonly SpellkitObject[] values;

    public override int Count => length;

    public bool IsVarArg { get; }

    public SpellkitObject this[int index]
    {
        get => values[index] is SpellkitLabel la ? la.Value : values[index];
        set
        {
            if (values[index] is SpellkitLabel la)
            {
                la.Value = value;
            }

            values[index] = value;
        }
    }

    public SpellkitTuple(SpellkitObject[] values) : this(values, values.Length) { }

    internal SpellkitTuple(SpellkitObject[] values, bool mutable, bool vararg) : this(values, values.Length) =>
        (this.mutable, IsVarArg) = (mutable, vararg);

    public SpellkitTuple(SpellkitObject[] values, int length) : base(SpellkitTypeCodes.Tuple)
    {
        this.length = length;
        this.values = values ?? throw new SpellkitException("Unable to create a tuple with no values.");
    }

    public static SpellkitTuple Create(params SpellkitLabel[] values) => new(values, values.Length);

    public override IEnumerator<SpellkitObject> GetEnumerator() => new SpellkitCollectionEnumerator(values, 0, Count, this);

    public override SpellkitObject Clone()
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

    public override bool Equals(SpellkitObject? other)
    {
        if (other is null || other is not SpellkitTuple xs)
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

    public Dictionary<SpellkitObject, SpellkitObject> ConvertToDictionary()
    {
        var dict = new Dictionary<SpellkitObject, SpellkitObject>();

        for (var i = 0; i < Count; i++)
        {
            var ki = GetKeyInfo(i);
            var v = this[i];
            var key = new SpellkitString(ki is null ? DefaultKey() : ki.Label);
            dict[key] = v;
        }

        return dict;
    }

    internal SpellkitDictionary ToSpellkitDictionary()
    {
        var dictionary = new SpellkitDictionary();

        for (var i = 0; i < Count; i++)
        {
            var keyInfo = GetKeyInfo(i);
            var key = new SpellkitString(keyInfo is null ? DefaultKey() : keyInfo.Label);
            dictionary.Dictionary[key] = this[i];
        }

        return dictionary;
    }

    internal bool TryGetItem(string name, out SpellkitObject item)
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

    internal bool TrySetItem(string name, SpellkitObject value)
    {
        var i = GetOrdinal(name);

        if (i is -1)
        {
            return false;
        }

        var item = values[i];

        if (item is SpellkitLabel lab)
        {
            lab.Value = value;
        }
        else
        {
            values[i] = value;
        }

        return true;
    }

    internal SpellkitObject GetItem(ExecutionContext ctx, SpellkitObject index)
    {
        if (index is SpellkitInteger ix)
        {
            return ix.TryGetInt32(out var value)
                ? GetItem(ctx, value)
                : ctx.IndexOutOfRange(index);
        }

        if (index.TypeId is SpellkitTypeCodes.String or SpellkitTypeCodes.Char && TryGetItem(index.ToString(), out var item))
        {
            return item;
        }

        return ctx.IndexOutOfRange(index);
    }

    internal SpellkitObject GetItem(ExecutionContext ctx, int index)
    {
        index = index < 0 ? Count + index : index;

        if (index < 0 || index >= Count)
        {
            return ctx.IndexOutOfRange(index);
        }

        var item = values[index];

        if (item is SpellkitLabel lab)
        {
            item = lab.Value;
        }

        return item;
    }

    internal void SetItem(ExecutionContext ctx, SpellkitObject index, SpellkitObject value)
    {
        int ix = -1;

        if (index.TypeId is SpellkitTypeCodes.String or SpellkitTypeCodes.Char)
        {
            ix = GetOrdinal(index.ToString());
        }
        else if (index is SpellkitInteger i)
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

        if (values[ix] is SpellkitLabel lab && lab.Mutable)
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
            if (values[i] is SpellkitLabel la && la.Label == name)
            {
                return i;
            }
        }

        return -1;
    }

    public virtual bool IsReadOnly(int index) => values[index] is SpellkitLabel lab && !lab.Mutable;

    internal virtual string? GetKey(int index) => values[index] is SpellkitLabel la ? la.Label : null;

    private static string DefaultKey() => Guid.NewGuid().ToString();

    internal virtual void SetValue(int index, SpellkitObject value)
    {
        if (values[index] is SpellkitLabel lab)
        {
            lab.Value = value;
        }
        else
        {
            values[index] = value;
        }
    }

    internal virtual SpellkitLabel? GetKeyInfo(int index) => values[index] is SpellkitLabel lab ? lab : null;

    public override SpellkitObject[] ToArray()
    {
        if (Count != values.Length)
        {
            return CopyTuple();
        }

        for (var i = 0; i < Count; i++)
        {
            if (values[i].TypeId == SpellkitTypeCodes.Label)
            {
                return CopyTuple();
            }
        }

        return values;
    }

    internal SpellkitObject[] GetValuesWithLabels()
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

    private SpellkitObject[] CopyTuple()
    {
        var arr = new SpellkitObject[Count];

        for (var i = 0; i < Count; i++)
        {
            arr[i] = values[i] is SpellkitLabel la ? la.Value : values[i];
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
            if (values[i] is SpellkitLabel la && la.Mutable)
            {
                mutable = true;
                return true;
            }
        }

        mutable = false;
        return false;
    }

    private SpellkitObject[] CopyTupleWithLabels()
    {
        var arr = new SpellkitObject[Count];

        for (var i = 0; i < Count; i++)
        {
            arr[i] = values[i] is SpellkitLabel la ? new SpellkitLabel(la.Label, la.Value) : values[i];
        }

        return arr;
    }

    internal bool HasItem(string name) => GetOrdinal(name) is not -1;

    internal protected override SpellkitObject[] UnsafeAccess() => values;

    private static SpellkitObject Compare(bool gt, SpellkitTuple xs, SpellkitTuple ys, ExecutionContext ctx)
    {
        var xsv = xs.UnsafeAccess();
        var ysv = ys.UnsafeAccess();
        var len = xs.Count > ys.Count ? ys.Count : xs.Count;

        for (var i = 0; i < len; i++)
        {
            var x = xsv[i] is SpellkitLabel lx ? lx.Value : xsv[i];
            var y = ysv[i] is SpellkitLabel ly ? ly.Value : ysv[i];
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

    internal static SpellkitObject Greater(ExecutionContext ctx, SpellkitTuple xs, SpellkitTuple ys) => Compare(true, xs, ys, ctx);

    internal static SpellkitObject Lesser(ExecutionContext ctx, SpellkitTuple xs, SpellkitTuple ys) => Compare(false, xs, ys, ctx);

    internal static SpellkitObject Equals(ExecutionContext ctx, SpellkitTuple xs, SpellkitTuple ys)
    {
        if (xs.Count != ys.Count)
        {
            return False;
        }

        var t1v = xs.UnsafeAccess();
        var t2v = ys.UnsafeAccess();

        for (var i = 0; i < xs.Count; i++)
        {
            var x = t1v[i] is SpellkitLabel lx ? lx.Value : t1v[i];
            var y = t2v[i] is SpellkitLabel ly ? ly.Value : t2v[i];

            if (x.NotEquals(y, ctx))
            {
                return False;
            }
        }

        return True;
    }
}

[SpellkitType]
internal sealed partial class SpellkitTupleTypeInfo : SpellkitCollTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Tuple);

    public override int ReflectedTypeId => SpellkitTypeCodes.Tuple;

    public SpellkitTupleTypeInfo()
    {
        AddMixins(SpellkitTypeCodes.Container, SpellkitTypeCodes.Order, SpellkitTypeCodes.Collection, SpellkitTypeCodes.Equatable, SpellkitTypeCodes.Sequence);
        SetSupportedOperations(Ops.Add);
    }

    #region Operations
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

        return new SpellkitTuple(arr.ToArray());
    }

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        IEnumerable<SpellkitObject> Iterate()
        {
            var tuple = (SpellkitTuple)arg;
            var xs = tuple.UnsafeAccess();
            for (var i = 0; i < tuple.Count; i++)
            {
                yield return xs[i];
            }
        }

        try
        {
            return new SpellkitString("(" + Iterate().ToLiteral(ctx) + ")");
        }
        catch (SpellkitCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return False;
        }

        var (xs, ys) = ((SpellkitTuple)left, (SpellkitTuple)right);

        try
        {
            return SpellkitTuple.Equals(ctx, xs, ys);
        }
        catch (SpellkitCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpellkitObject GtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.OperationNotSupported(Builtins.Gt, left, right);
        }

        try
        {
            return SpellkitTuple.Greater(ctx, (SpellkitTuple)left, (SpellkitTuple)right);
        }
        catch (SpellkitCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpellkitObject LtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.OperationNotSupported(Builtins.Lt, left, right);
        }

        try
        {
            return SpellkitTuple.Lesser(ctx, (SpellkitTuple)left, (SpellkitTuple)right);
        }
        catch (SpellkitCodeException e)
        {
            ctx.Error = e.Error;
            return Nil;
        }
    }

    protected override SpellkitObject InOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject field)
    {
        if (field.TypeId is not SpellkitTypeCodes.String and not SpellkitTypeCodes.Char)
        {
            return ctx.InvalidType(field);
        }

        return ((SpellkitTuple)self).GetOrdinal(field.ToString()) is not -1 ? True : False;
    }

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index) =>
        ((SpellkitTuple)self).GetItem(ctx, index);

    protected override SpellkitObject SetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index, SpellkitObject value)
    {
        ((SpellkitTuple)self).SetItem(ctx, index, value);
        return Nil;
    }

    internal override void SetInstanceMember(ExecutionContext ctx, HashString name, SpellkitFunction func)
    {
        if ((string)name is Builtins.Get or Builtins.Set or Builtins.Length)
        {
            ctx.OverloadProhibited(this, (string)name);
            return;
        }

        base.SetInstanceMember(ctx, name, func);
    }
    #endregion

    [SpellkitMethod]
    internal static bool ContainsField(SpellkitTuple self, string field) =>
        self.GetOrdinal(field.ToString()) is not -1;

    [SpellkitMethod(BuiltinMethodNames.Add)]
    internal static SpellkitObject AddItem(SpellkitTuple self, SpellkitObject value)
    {
        var arr = new SpellkitObject[self.Count + 1];
        Array.Copy(self.UnsafeAccess(), arr, self.Count);
        arr[^1] = value;
        return new SpellkitTuple(arr);
    }

    [SpellkitMethod(BuiltinMethodNames.Remove)]
    internal static SpellkitObject Remove(ExecutionContext ctx, SpellkitTuple self, SpellkitObject value)
    {
        var tv = self.UnsafeAccess();

        for (var i = 0; i < tv.Length; i++)
        {
            var e = tv[i] is SpellkitLabel la ? la.Value : tv[i];

            if (e.Equals(value, ctx))
            {
                return InternalRemoveAt(self, i);
            }
        }

        return self;
    }

    [SpellkitMethod]
    internal static SpellkitObject RemoveField(SpellkitTuple self, string field)
    {
        var tv = self.UnsafeAccess();

        for (var i = 0; i < tv.Length; i++)
        {
            if (tv[i] is SpellkitLabel la && la.Label == field)
            {
                return InternalRemoveAt(self, i);
            }
        }

        return self;
    }

    [SpellkitMethod(BuiltinMethodNames.RemoveAt)]
    internal static SpellkitObject RemoveAt(SpellkitTuple self, int index)
    {
        index = index < 0 ? self.Count + index : index;

        if (index < 0 || index >= self.Count)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, index);
        }

        return InternalRemoveAt(self, index);
    }

    internal static SpellkitTuple InternalRemoveAt(SpellkitTuple self, int index)
    {
        var arr = new SpellkitObject[self.Count - 1];
        var c = 0;
        var sv = self.UnsafeAccess();

        for (var i = 0; i < self.Count; i++)
        {
            if (i != index)
            {
                arr[c++] = sv[i];
            }
        }

        return new SpellkitTuple(arr);
    }

    [SpellkitMethod(BuiltinMethodNames.Insert)]
    internal static SpellkitObject Insert(SpellkitTuple self, int index, SpellkitObject value)
    {
        index = index < 0 ? self.Count + index : index;

        if (index < 0 || index > self.Count)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, index);
        }

        var arr = new SpellkitObject[self.Count + 1];
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

        return new SpellkitTuple(arr);
    }

    [SpellkitMethod(BuiltinMethodNames.Keys)]
    internal static SpellkitObject Keys(SpellkitTuple self)
    {
        IEnumerable<SpellkitObject> Iterate()
        {
            for (var i = 0; i < self.Count; i++)
            {
                var k = self.GetKey(i);
                if (k is not null)
                {
                    yield return new SpellkitString(k);
                }
            }
        }

        return SpellkitIterator.Create(Iterate());
    }

    [SpellkitMethod(BuiltinMethodNames.First)]
    internal static SpellkitObject First(ExecutionContext ctx, SpellkitTuple self)
    {
        var ret = self.GetItem(ctx, 0);
        ctx.ThrowIf();
        return ret;
    }

    [SpellkitMethod(BuiltinMethodNames.Second)]
    internal static SpellkitObject Second(ExecutionContext ctx, SpellkitTuple self)
    {
        var ret = self.GetItem(ctx, 1);
        ctx.ThrowIf();
        return ret;
    }

    [SpellkitMethod(BuiltinMethodNames.Sort)]
    internal static SpellkitObject Sort(ExecutionContext ctx, SpellkitTuple self, SpellkitFunction? comparer = null)
    {
        var sortComparer = new SortComparer(comparer, ctx);
        var newArr = new SpellkitObject[self.Count];
        Array.Copy(self.UnsafeAccess(), newArr, newArr.Length);
        Array.Sort(newArr, 0, newArr.Length, sortComparer);
        return new SpellkitTuple(newArr);
    }

    [SpellkitMethod(BuiltinMethodNames.ToDictionary)]
    internal static SpellkitObject ToDictionary(SpellkitTuple self) =>
        self.ToSpellkitDictionary();

    [SpellkitMethod(BuiltinMethodNames.ToArray)]
    internal static SpellkitObject[] ToArray(SpellkitCollection self) => self.ToArray();

    [SpellkitMethod(BuiltinMethodNames.Compact)]
    internal static SpellkitObject Compact(ExecutionContext ctx, SpellkitTuple self, SpellkitFunction? predicate = null)
    {
        var xs = new List<SpellkitObject>();

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
            else if (!val.Is(SpellkitTypeCodes.Nil))
            {
                xs.Add(val);
            }
        }

        return new SpellkitTuple(xs.ToArray());
    }

    [SpellkitMethod(BuiltinMethodNames.Alter)]
    internal static SpellkitObject Alter(SpellkitTuple self, [VarArg]SpellkitTuple values)
    {
        var xs = new List<SpellkitObject>(self.UnsafeAccess());

        foreach (var o in values.UnsafeAccess())
        {
            if (o is SpellkitLabel lab)
            {
                var exist = xs.OfType<SpellkitLabel>().FirstOrDefault(i => i.Label == lab.Label);

                if (exist is not null)
                {
                    exist.Value = lab.Value;
                    continue;
                }
            }

            xs.Add(o);
        }

        return new SpellkitTuple(xs.ToArray());
    }

    [SpellkitStaticMethod(BuiltinMethodNames.Sort)]
    internal static SpellkitObject StaticSort(ExecutionContext ctx, SpellkitTuple value, SpellkitFunction? comparer = null) =>
        Sort(ctx, value, comparer);

    [SpellkitStaticMethod(BuiltinMethodNames.Pair)]
    internal static SpellkitObject Pair(SpellkitObject first, SpellkitObject second) =>
        new SpellkitTuple(new[] { first, second });

    [SpellkitStaticMethod(BuiltinMethodNames.Triple)]
    internal static SpellkitObject Triple(SpellkitObject first, SpellkitObject second, SpellkitObject third) =>
        new SpellkitTuple(new[] { first, second, third });

    [SpellkitStaticMethod(BuiltinMethodNames.Concat)]
    internal static SpellkitObject StaticConcat(ExecutionContext ctx, params SpellkitObject[] values) =>
        new SpellkitTuple(SpellkitCollection.ConcatValues(ctx, values));

    [SpellkitStaticMethod(BuiltinMethodNames.Tuple)]
    internal static SpellkitObject MakeNew([VarArg]SpellkitObject values) => values;
}
