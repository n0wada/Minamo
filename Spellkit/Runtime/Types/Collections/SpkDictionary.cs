using System.Collections.Generic;
using Spellkit.Codegen;
using System.Text;
using System.Collections;

namespace Spellkit.Runtime.Types;

public class SpkDictionary : SpkEnumerable
{
    internal readonly Dictionary<SpkObject, SpkObject> Dictionary;

    public override string TypeName => nameof(Spk.Dictionary);

    public override int Count => Dictionary.Count;

    public SpkObject this[SpkObject key]
    {
        get => Dictionary[key];
        set => Dictionary[key] = value;
    }

    internal SpkDictionary() : base(Spk.Dictionary)
    {
        Dictionary = new Dictionary<SpkObject, SpkObject>();
    }

    internal SpkDictionary(Dictionary<SpkObject, SpkObject> dict) : base(Spk.Dictionary)
    {
        Dictionary = dict;
    }

    public void Add(SpkObject key, SpkObject value)
    {
        Dictionary.Add(key, value);
        Version++;
    }

    public bool TryAdd(SpkObject key, SpkObject value)
    {
        var added = Dictionary.TryAdd(key, value);
        if (added)
        {
            Version++;
        }

        return added;
    }

    public bool TryGet(SpkObject key, out SpkObject? value) =>
        Dictionary.TryGetValue(key, out value);

    public SpkObject GetAndRemove(SpkObject key)
    {
        if (Dictionary.Remove(key, out var value))
        {
            Version++;
        }

        return value ?? Nil;
    }

    public bool Remove(SpkObject key)
    {
        var removed = Dictionary.Remove(key);
        if (removed)
        {
            Version++;
        }

        return removed;
    }

    public bool ContainsKey(SpkObject key) => Dictionary.ContainsKey(key);

    public bool ContainsValue(SpkObject value) => Dictionary.ContainsValue(value);

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

    internal SpkObject GetItem(SpkObject index, ExecutionContext ctx)
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

    internal void SetItem(SpkObject index, SpkObject value, ExecutionContext _)
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

    public override bool Equals(SpkObject? other)
    {
        if (other is not SpkDictionary d)
        {
            return false;
        }

        return d.Dictionary.Equals(Dictionary);
    }

    internal SpkObject[] GetArrayOfLabels()
    {
        var xs = new List<SpkLabel>();

        foreach (var (key, value) in Dictionary)
        {
            if (key is SpkString s)
            {
                xs.Add(new(s.Value, value));
            }
        }

        return xs.ToArray();
    }

    public override IEnumerator<SpkObject> GetEnumerator() => new SpkDictionaryEnumerator(this);

    public override int GetHashCode() => Dictionary.GetHashCode();
}

[SpkType]
internal sealed partial class SpkDictionaryTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Dictionary);

    public override int ReflectedTypeId => Spk.Dictionary;

    public SpkDictionaryTypeInfo() => AddMixins(Spk.Collection, Spk.Container, Spk.Sequence);

    #region Operations
    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg)
    {
        var len = ((SpkDictionary)arg).Count;
        return SpkInteger.Get(len);
    }

    protected override SpkObject IterateOp(ExecutionContext ctx, SpkObject self) => SpkIterator.Create((SpkDictionary)self);

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        var map = (SpkDictionary)arg;
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
        return new SpkString(sb.ToString());
    }

    protected override SpkObject InOp(ExecutionContext ctx, SpkObject self, SpkObject field) =>
        ((SpkDictionary)self).ContainsKey(field) ? True : False;

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index) => ((SpkDictionary)self).GetItem(index, ctx);

    protected override SpkObject SetOp(ExecutionContext ctx, SpkObject self, SpkObject index, SpkObject value)
    {
        ((SpkDictionary)self).SetItem(index, value, ctx);
        return Nil;
    }
    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            Spk.Tuple => new SpkTuple(((SpkDictionary)self).GetArrayOfLabels()),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [SpkMethod(BuiltinMethodNames.Add)]
    internal static void AddItem(ExecutionContext ctx, SpkDictionary self, SpkObject key, SpkObject value)
    {
        if (!self.TryAdd(key, value))
        {
            ctx.KeyAlreadyPresent(key);
        }
    }

    [SpkMethod(BuiltinMethodNames.TryAdd)]
    internal static bool TryAddItem(SpkDictionary self, SpkObject key, SpkObject value) =>
        self.TryAdd(key, value);

    [SpkMethod(BuiltinMethodNames.TryGet)]
    internal static SpkObject? TryGetItem(SpkDictionary self, SpkObject key)
    {
        if (!self.TryGet(key, out var value))
        {
            return null;
        }

        return value;
    }

    [SpkMethod(BuiltinMethodNames.Remove)]
    internal static bool RemoveItem(SpkDictionary self, SpkObject key) =>
        self.Remove(key);

    [SpkMethod(BuiltinMethodNames.Clear)]
    internal static void ClearItems(SpkDictionary self) => self.Clear();

    [SpkMethod(BuiltinMethodNames.ToTuple)]
    internal static SpkObject ToTuple(SpkDictionary self) => new SpkTuple(self.GetArrayOfLabels());

    [SpkMethod(BuiltinMethodNames.Compact)]
    internal static void Compact(ExecutionContext ctx, SpkDictionary self, [Default]SpkObject predicate)
    {
        var keys = new List<SpkObject>();

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
            else if (value.Is(Spk.Nil))
            {
                keys.Add(key);
            }
        }

        foreach (var key in keys)
        {
            self.Remove(key);
        }
    }

    [SpkMethod]
    internal static bool ContainsKey(SpkDictionary self, SpkObject key) => self.ContainsKey(key);

    [SpkMethod]
    internal static bool ContainsValue(SpkDictionary self, SpkObject value) => self.ContainsValue(value);

    [SpkMethod(BuiltinMethodNames.GetAndRemove)]
    internal static SpkObject GetAndRemove(SpkDictionary self, SpkObject key) => self.GetAndRemove(key);

    [SpkStaticMethod(BuiltinMethodNames.Dictionary)]
    internal static SpkObject New([VarArg]SpkTuple values)
    {
        if (values.Count == 0)
        {
            return new SpkDictionary();
        }

        if (values.Count == 1)
        {
            var el = values[0];

            if (el is SpkTuple t)
            {
                return new SpkDictionary(t.ConvertToDictionary());
            }
        }

        return new SpkDictionary(values.ConvertToDictionary());
    }

    [SpkStaticMethod(BuiltinMethodNames.FromTuple)]
    internal static SpkObject FromTuple([VarArg]SpkTuple values) => New(values);
}

internal sealed class SpkDictionaryEnumerator : IEnumerator<SpkObject>
{
    private readonly SpkDictionary obj;
    private readonly IEnumerator enumerator;
    private readonly int version;

    public SpkDictionaryEnumerator(SpkDictionary obj)
    {
        this.obj = obj;
        version = obj.Version;
        enumerator = obj.Dictionary.GetEnumerator();
    }

    public SpkObject Current
    {
        get
        {
            var obj = (KeyValuePair<SpkObject, SpkObject>)enumerator.Current;
            return new SpkTuple(new SpkLabel[] {
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
