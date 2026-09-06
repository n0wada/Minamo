using System.Collections.Generic;
using Minamo.Codegen;
using System.Text;
using System.Collections;

namespace Minamo.Runtime.Types;

public class MinamoDictionary : MinamoEnumerable
{
    internal readonly OrderedDictionary<MinamoObject, MinamoObject> Dictionary;

    public override string TypeName => nameof(MinamoTypeCodes.Dictionary);

    public override int Count => Dictionary.Count;

    public MinamoObject this[MinamoObject key]
    {
        get => Dictionary[key];
        set => Dictionary[key] = value;
    }

    internal MinamoDictionary() : base(MinamoTypeCodes.Dictionary)
    {
        Dictionary = new OrderedDictionary<MinamoObject, MinamoObject>(MinamoObjectKeyComparer.Instance);
    }

    internal MinamoDictionary(IEnumerable<KeyValuePair<MinamoObject, MinamoObject>> values) : base(MinamoTypeCodes.Dictionary)
    {
        Dictionary = new OrderedDictionary<MinamoObject, MinamoObject>(MinamoObjectKeyComparer.Instance);
        foreach (var (key, value) in values)
        {
            Dictionary.Add(key, value);
        }
    }

    public void Add(MinamoObject key, MinamoObject value)
    {
        Dictionary.Add(key, value);
        Version++;
    }

    public bool TryAdd(MinamoObject key, MinamoObject value)
    {
        var added = Dictionary.TryAdd(key, value);
        if (added)
        {
            Version++;
        }

        return added;
    }

    public bool TryGet(MinamoObject key, out MinamoObject? value) =>
        Dictionary.TryGetValue(key, out value);

    public MinamoObject GetAndRemove(MinamoObject key)
    {
        if (Dictionary.Remove(key, out var value))
        {
            Version++;
        }

        return value ?? Nil;
    }

    public bool Remove(MinamoObject key)
    {
        var removed = Dictionary.Remove(key);
        if (removed)
        {
            Version++;
        }

        return removed;
    }

    public bool ContainsKey(MinamoObject key) => Dictionary.ContainsKey(key);

    public bool ContainsValue(MinamoObject value)
    {
        foreach (var candidate in Dictionary.Values)
        {
            if (candidate.Equals(value))
            {
                return true;
            }
        }

        return false;
    }

    public void Clear()
    {
        if (Dictionary.Count == 0)
        {
            return;
        }

        Dictionary.Clear();
        Version++;
    }

    public override object ToObject() => Dictionary;

    internal MinamoObject GetItem(MinamoObject index, ExecutionContext ctx)
    {
        if (!Dictionary.TryGetValue(index, out var value))
        {
            return ctx.KeyNotFound(index);
        }
        else
        {
            return value;
        }
    }

    internal void SetItem(MinamoObject index, MinamoObject value, ExecutionContext _)
    {
        if (!Dictionary.TryAdd(index, value))
        {
            Dictionary[index] = value;
        }
        else
        {
            Version++;
        }
    }

    public override bool Equals(MinamoObject? other)
    {
        if (other is not MinamoDictionary d)
        {
            return false;
        }

        if (d.Dictionary.Count != Dictionary.Count)
        {
            return false;
        }

        foreach (var (key, value) in Dictionary)
        {
            if (!d.Dictionary.TryGetValue(key, out var otherValue) || !value.Equals(otherValue))
            {
                return false;
            }
        }

        return true;
    }

    internal MinamoObject[] GetArrayOfLabels()
    {
        var xs = new List<MinamoLabel>();

        foreach (var (key, value) in Dictionary)
        {
            if (key is MinamoString s)
            {
                xs.Add(new(s.Value, value));
            }
        }

        return xs.ToArray();
    }

    public override IEnumerator<MinamoObject> GetEnumerator() => new MinamoDictionaryEnumerator(this);

    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (key, value) in Dictionary)
        {
            hash += HashCode.Combine(MinamoObjectKeyComparer.Instance.GetHashCode(key), value.GetHashCode());
        }

        return HashCode.Combine(TypeId, Dictionary.Count, hash);
    }
}

[MinamoType]
internal sealed partial class MinamoDictionaryTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Dictionary);

    public override int ReflectedTypeId => MinamoTypeCodes.Dictionary;

    public MinamoDictionaryTypeInfo() => AddMixins(MinamoTypeCodes.Collection, MinamoTypeCodes.Container, MinamoTypeCodes.Sequence);

    #region Operations
    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg)
    {
        var len = ((MinamoDictionary)arg).Count;
        return MinamoInteger.Get(len);
    }

    protected override MinamoObject IterateOp(ExecutionContext ctx, MinamoObject self) => MinamoIterator.Create((MinamoDictionary)self);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        var map = (MinamoDictionary)arg;
        var sb = new StringBuilder();
        sb.Append('[');
        var i = 0;

        foreach (var kv in map.Dictionary)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(kv.Key.ToLiteral(ctx) + ": " + kv.Value.ToLiteral(ctx));

            i++;
        }

        sb.Append(']');
        return new MinamoString(sb.ToString());
    }

    protected override MinamoObject InOp(ExecutionContext ctx, MinamoObject self, MinamoObject field) =>
        ((MinamoDictionary)self).ContainsKey(field) ? True : False;

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index) => ((MinamoDictionary)self).GetItem(index, ctx);

    protected override MinamoObject SetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index, MinamoObject value)
    {
        ((MinamoDictionary)self).SetItem(index, value, ctx);
        return Nil;
    }
    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            MinamoTypeCodes.Tuple => new MinamoTuple(((MinamoDictionary)self).GetArrayOfLabels()),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [MinamoMethod(BuiltinMethodNames.Add)]
    internal static void AddItem(ExecutionContext ctx, MinamoDictionary self, MinamoObject key, MinamoObject value)
    {
        if (!self.TryAdd(key, value))
        {
            ctx.KeyAlreadyPresent(key);
        }
    }

    [MinamoMethod(BuiltinMethodNames.TryAdd)]
    internal static bool TryAddItem(MinamoDictionary self, MinamoObject key, MinamoObject value) =>
        self.TryAdd(key, value);

    [MinamoMethod(BuiltinMethodNames.TryGet)]
    internal static MinamoObject? TryGetItem(MinamoDictionary self, MinamoObject key)
    {
        if (!self.TryGet(key, out var value))
        {
            return null;
        }

        return value;
    }

    [MinamoMethod(BuiltinMethodNames.Remove)]
    internal static bool RemoveItem(MinamoDictionary self, MinamoObject key) =>
        self.Remove(key);

    [MinamoMethod(BuiltinMethodNames.Clear)]
    internal static void ClearItems(MinamoDictionary self) => self.Clear();

    [MinamoMethod(BuiltinMethodNames.ToTuple)]
    internal static MinamoObject ToTuple(MinamoDictionary self) => new MinamoTuple(self.GetArrayOfLabels());

    [MinamoMethod(BuiltinMethodNames.Compact)]
    internal static void Compact(ExecutionContext ctx, MinamoDictionary self, [Default]MinamoObject predicate)
    {
        var keys = new List<MinamoObject>();

        foreach (var (key, value) in self.Dictionary)
        {
            if (predicate is not null)
            {
                var res = predicate.Invoke(ctx, value);

                if (ctx.HasErrors)
                {
                    return;
                }

                if (ReferenceEquals(res, True))
                {
                    keys.Add(key);
                }
            }
            else if (value.Is(MinamoTypeCodes.Nil))
            {
                keys.Add(key);
            }
        }

        foreach (var key in keys)
        {
            self.Remove(key);
        }
    }

    [MinamoMethod]
    internal static bool ContainsKey(MinamoDictionary self, MinamoObject key) => self.ContainsKey(key);

    [MinamoMethod(BuiltinMethodNames.ContainsValue)]
    internal static bool ContainsValue(MinamoDictionary self, MinamoObject value) => self.ContainsValue(value);

    [MinamoMethod(BuiltinMethodNames.GetAndRemove)]
    internal static MinamoObject GetAndRemove(MinamoDictionary self, MinamoObject key) => self.GetAndRemove(key);

    [MinamoStaticMethod(BuiltinMethodNames.Dictionary)]
    internal static MinamoObject New([VarArg]MinamoTuple values)
    {
        if (values.Count == 0)
        {
            return new MinamoDictionary();
        }

        if (values.Count == 1)
        {
            var el = values[0];

            if (el is MinamoTuple t)
            {
                return t.ToMinamoDictionary();
            }
        }

        return values.ToMinamoDictionary();
    }

    [MinamoStaticMethod(BuiltinMethodNames.FromTuple)]
    internal static MinamoObject FromTuple([VarArg]MinamoTuple values) => New(values);
}

internal sealed class MinamoDictionaryEnumerator : IEnumerator<MinamoObject>
{
    private readonly MinamoDictionary obj;
    private readonly IEnumerator enumerator;
    private readonly int version;

    public MinamoDictionaryEnumerator(MinamoDictionary obj)
    {
        this.obj = obj;
        version = obj.Version;
        enumerator = obj.Dictionary.GetEnumerator();
    }

    public MinamoObject Current
    {
        get
        {
            var obj = (KeyValuePair<MinamoObject, MinamoObject>)enumerator.Current;
            return new MinamoTuple(new MinamoLabel[] {
                new("key", obj.Key),
                new("value", obj.Value)
            });
        }
    }

    object IEnumerator.Current => Current;

    public void Dispose() { }

    public bool MoveNext() =>
        version != obj.Version ? throw new IterationException() : enumerator.MoveNext();

    public void Reset() => enumerator.Reset();
}
