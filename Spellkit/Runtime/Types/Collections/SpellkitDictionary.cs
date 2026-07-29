using System.Collections.Generic;
using Spellkit.Codegen;
using System.Text;
using System.Collections;

namespace Spellkit.Runtime.Types;

public class SpellkitDictionary : SpellkitEnumerable
{
    internal readonly OrderedDictionary<SpellkitObject, SpellkitObject> Dictionary;

    public override string TypeName => nameof(SpellkitTypeCodes.Dictionary);

    public override int Count => Dictionary.Count;

    public SpellkitObject this[SpellkitObject key]
    {
        get => Dictionary[key];
        set => Dictionary[key] = value;
    }

    internal SpellkitDictionary() : base(SpellkitTypeCodes.Dictionary)
    {
        Dictionary = new OrderedDictionary<SpellkitObject, SpellkitObject>();
    }

    internal SpellkitDictionary(IEnumerable<KeyValuePair<SpellkitObject, SpellkitObject>> values) : base(SpellkitTypeCodes.Dictionary)
    {
        Dictionary = new OrderedDictionary<SpellkitObject, SpellkitObject>();
        foreach (var (key, value) in values)
        {
            Dictionary.Add(key, value);
        }
    }

    public void Add(SpellkitObject key, SpellkitObject value)
    {
        Dictionary.Add(key, value);
        Version++;
    }

    public bool TryAdd(SpellkitObject key, SpellkitObject value)
    {
        var added = Dictionary.TryAdd(key, value);
        if (added)
        {
            Version++;
        }

        return added;
    }

    public bool TryGet(SpellkitObject key, out SpellkitObject? value) =>
        Dictionary.TryGetValue(key, out value);

    public SpellkitObject GetAndRemove(SpellkitObject key)
    {
        if (Dictionary.Remove(key, out var value))
        {
            Version++;
        }

        return value ?? Nil;
    }

    public bool Remove(SpellkitObject key)
    {
        var removed = Dictionary.Remove(key);
        if (removed)
        {
            Version++;
        }

        return removed;
    }

    public bool ContainsKey(SpellkitObject key) => Dictionary.ContainsKey(key);

    public bool ContainsValue(SpellkitObject value)
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

    internal SpellkitObject GetItem(SpellkitObject index, ExecutionContext ctx)
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

    internal void SetItem(SpellkitObject index, SpellkitObject value, ExecutionContext _)
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

    public override bool Equals(SpellkitObject? other)
    {
        if (other is not SpellkitDictionary d)
        {
            return false;
        }

        return d.Dictionary.Equals(Dictionary);
    }

    internal SpellkitObject[] GetArrayOfLabels()
    {
        var xs = new List<SpellkitLabel>();

        foreach (var (key, value) in Dictionary)
        {
            if (key is SpellkitString s)
            {
                xs.Add(new(s.Value, value));
            }
        }

        return xs.ToArray();
    }

    public override IEnumerator<SpellkitObject> GetEnumerator() => new SpellkitDictionaryEnumerator(this);

    public override int GetHashCode() => Dictionary.GetHashCode();
}

[SpellkitType]
internal sealed partial class SpellkitDictionaryTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Dictionary);

    public override int ReflectedTypeId => SpellkitTypeCodes.Dictionary;

    public SpellkitDictionaryTypeInfo() => AddMixins(SpellkitTypeCodes.Collection, SpellkitTypeCodes.Container, SpellkitTypeCodes.Sequence);

    #region Operations
    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg)
    {
        var len = ((SpellkitDictionary)arg).Count;
        return SpellkitInteger.Get(len);
    }

    protected override SpellkitObject IterateOp(ExecutionContext ctx, SpellkitObject self) => SpellkitIterator.Create((SpellkitDictionary)self);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        var map = (SpellkitDictionary)arg;
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
        return new SpellkitString(sb.ToString());
    }

    protected override SpellkitObject InOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject field) =>
        ((SpellkitDictionary)self).ContainsKey(field) ? True : False;

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index) => ((SpellkitDictionary)self).GetItem(index, ctx);

    protected override SpellkitObject SetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index, SpellkitObject value)
    {
        ((SpellkitDictionary)self).SetItem(index, value, ctx);
        return Nil;
    }
    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Tuple => new SpellkitTuple(((SpellkitDictionary)self).GetArrayOfLabels()),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [SpellkitMethod(BuiltinMethodNames.Add)]
    internal static void AddItem(ExecutionContext ctx, SpellkitDictionary self, SpellkitObject key, SpellkitObject value)
    {
        if (!self.TryAdd(key, value))
        {
            ctx.KeyAlreadyPresent(key);
        }
    }

    [SpellkitMethod(BuiltinMethodNames.TryAdd)]
    internal static bool TryAddItem(SpellkitDictionary self, SpellkitObject key, SpellkitObject value) =>
        self.TryAdd(key, value);

    [SpellkitMethod(BuiltinMethodNames.TryGet)]
    internal static SpellkitObject? TryGetItem(SpellkitDictionary self, SpellkitObject key)
    {
        if (!self.TryGet(key, out var value))
        {
            return null;
        }

        return value;
    }

    [SpellkitMethod(BuiltinMethodNames.Remove)]
    internal static bool RemoveItem(SpellkitDictionary self, SpellkitObject key) =>
        self.Remove(key);

    [SpellkitMethod(BuiltinMethodNames.Clear)]
    internal static void ClearItems(SpellkitDictionary self) => self.Clear();

    [SpellkitMethod(BuiltinMethodNames.ToTuple)]
    internal static SpellkitObject ToTuple(SpellkitDictionary self) => new SpellkitTuple(self.GetArrayOfLabels());

    [SpellkitMethod(BuiltinMethodNames.Compact)]
    internal static void Compact(ExecutionContext ctx, SpellkitDictionary self, [Default]SpellkitObject predicate)
    {
        var keys = new List<SpellkitObject>();

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
            else if (value.Is(SpellkitTypeCodes.Nil))
            {
                keys.Add(key);
            }
        }

        foreach (var key in keys)
        {
            self.Remove(key);
        }
    }

    [SpellkitMethod]
    internal static bool ContainsKey(SpellkitDictionary self, SpellkitObject key) => self.ContainsKey(key);

    [SpellkitMethod(BuiltinMethodNames.ContainsValue)]
    internal static bool ContainsValue(SpellkitDictionary self, SpellkitObject value) => self.ContainsValue(value);

    [SpellkitMethod(BuiltinMethodNames.GetAndRemove)]
    internal static SpellkitObject GetAndRemove(SpellkitDictionary self, SpellkitObject key) => self.GetAndRemove(key);

    [SpellkitStaticMethod(BuiltinMethodNames.Dictionary)]
    internal static SpellkitObject New([VarArg]SpellkitTuple values)
    {
        if (values.Count == 0)
        {
            return new SpellkitDictionary();
        }

        if (values.Count == 1)
        {
            var el = values[0];

            if (el is SpellkitTuple t)
            {
                return t.ToSpellkitDictionary();
            }
        }

        return values.ToSpellkitDictionary();
    }

    [SpellkitStaticMethod(BuiltinMethodNames.FromTuple)]
    internal static SpellkitObject FromTuple([VarArg]SpellkitTuple values) => New(values);
}

internal sealed class SpellkitDictionaryEnumerator : IEnumerator<SpellkitObject>
{
    private readonly SpellkitDictionary obj;
    private readonly IEnumerator enumerator;
    private readonly int version;

    public SpellkitDictionaryEnumerator(SpellkitDictionary obj)
    {
        this.obj = obj;
        version = obj.Version;
        enumerator = obj.Dictionary.GetEnumerator();
    }

    public SpellkitObject Current
    {
        get
        {
            var obj = (KeyValuePair<SpellkitObject, SpellkitObject>)enumerator.Current;
            return new SpellkitTuple(new SpellkitLabel[] {
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
