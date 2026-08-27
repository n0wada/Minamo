using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;
using System.Collections;

namespace Spellkit.Runtime.Types;

public class SpellkitSet : SpellkitEnumerable
{
    // An ordered map gives the set stable, insertion-ordered enumeration while
    // retaining constant-time membership checks.
    internal readonly OrderedDictionary<SpellkitObject, byte> Set;

    public override string TypeName => nameof(SpellkitTypeCodes.Set);
    
    public SpellkitSet() : base(SpellkitTypeCodes.Set) => Set = new(SpellkitObjectKeyComparer.Instance);

    public SpellkitSet(params SpellkitObject[] args) : this((IEnumerable<SpellkitObject>)args) { }

    public SpellkitSet(IEnumerable<SpellkitObject> values) : this()
    {
        foreach (var value in values)
        {
            Set.TryAdd(value, default);
        }
    }
    
    public override IEnumerator<SpellkitObject> GetEnumerator() => new SpellkitSetEnumerator(this);

    public override object ToObject() => new HashSet<SpellkitObject>(Set.Keys, SpellkitObjectKeyComparer.Instance);

    public override int Count => Set.Count;

    public bool Equals(ExecutionContext ctx, SpellkitObject other)
    {
        var seq = SpellkitIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        return SetEquals(seq);
    }

    public bool Add(SpellkitObject value)
    {
        var added = Set.TryAdd(value, default);
        if (added)
        {
            Version++;
        }

        return added;
    }

    public bool Remove(SpellkitObject value)
    {
        var removed = Set.Remove(value);
        if (removed)
        {
            Version++;
        }

        return removed;
    }

    public bool Contains(SpellkitObject value) => Set.ContainsKey(value);

    public void Clear()
    {
        if (Set.Count == 0)
        {
            return;
        }

        Set.Clear();
        Version++;
    }

    private SpellkitObject[] InternalToArray()
    {
        var arr = new SpellkitObject[Set.Count];
        var count = 0;
        
        foreach (var v in Set.Keys)
        {
            arr[count++] = v;
        }

        return arr;
    }
    
    public SpellkitArray ToArray(ExecutionContext _) => new(InternalToArray());

    public SpellkitTuple ToTuple(ExecutionContext _) => new(InternalToArray());

    public void IntersectWith(ExecutionContext ctx, SpellkitObject other)
    {
        var seq = SpellkitIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return;
        }

        var values = new HashSet<SpellkitObject>(seq, SpellkitObjectKeyComparer.Instance);
        var removed = false;
        foreach (var value in Set.Keys.ToArray())
        {
            if (values.Contains(value))
            {
                continue;
            }

            Set.Remove(value);
            removed = true;
        }

        if (removed)
        {
            Version++;
        }
    }

    public void UnionWith(ExecutionContext ctx, SpellkitObject other)
    {
        var seq = SpellkitIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return;
        }

        var added = false;
        foreach (var value in seq)
        {
            if (!Set.TryAdd(value, default))
            {
                continue;
            }

            added = true;
        }

        if (added)
        {
            Version++;
        }
    }

    public void ExceptWith(ExecutionContext ctx, SpellkitObject other)
    {
        var seq = SpellkitIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return;
        }

        var values = new HashSet<SpellkitObject>(seq, SpellkitObjectKeyComparer.Instance);
        var removed = false;
        foreach (var value in Set.Keys.ToArray())
        {
            if (!values.Contains(value))
            {
                continue;
            }

            Set.Remove(value);
            removed = true;
        }

        if (removed)
        {
            Version++;
        }
    }

    public bool Overlaps(ExecutionContext ctx, SpellkitObject other)
    {
        var seq = SpellkitIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        foreach (var value in seq)
        {
            if (Set.ContainsKey(value))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsSubsetOf(ExecutionContext ctx, SpellkitObject other)
    {
        var seq = SpellkitIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        var values = new HashSet<SpellkitObject>(seq, SpellkitObjectKeyComparer.Instance);
        return Set.Keys.All(values.Contains);
    }

    public bool IsSupersetOf(ExecutionContext ctx, SpellkitObject other)
    {
        var seq = SpellkitIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        return new HashSet<SpellkitObject>(Set.Keys, SpellkitObjectKeyComparer.Instance).IsSupersetOf(seq);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var sum = 0;
            var xor = 0;
            foreach (var v in Set.Keys)
            {
                var valueHash = SpellkitObjectKeyComparer.Instance.GetHashCode(v);
                sum += valueHash;
                xor ^= valueHash;
            }

            return HashCode.Combine(TypeId, Set.Count, sum, xor);
        }
    }

    public override bool Equals(SpellkitObject? other)
    {
        if (other is not IEnumerable<SpellkitObject> seq)
        {
            return false;
        }

        return SetEquals(seq);
    }

    private bool SetEquals(IEnumerable<SpellkitObject> values) =>
        new HashSet<SpellkitObject>(Set.Keys, SpellkitObjectKeyComparer.Instance).SetEquals(values);
}

[SpellkitType]
internal sealed partial class SpellkitSetTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Set);

    public override int ReflectedTypeId => SpellkitTypeCodes.Set;

    public SpellkitSetTypeInfo() => AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Sequence, SpellkitTypeCodes.Equatable);

    #region Operations
    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        var self = (SpellkitSet)left;
        return self.Equals(ctx, right) ? True : False;
    }

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg)
    {
        var self = (SpellkitSet)arg;
        return SpellkitInteger.Get(self.Count);
    }

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        try
        {
            return new SpellkitString("Set(" + ((IEnumerable<SpellkitObject>)arg).ToLiteral(ctx) + ")");
        }
        catch (SpellkitCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Array => new SpellkitArray(((SpellkitSet)self).ToArray()),
            SpellkitTypeCodes.Tuple => new SpellkitTuple(((SpellkitSet)self).ToArray()),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion


    [SpellkitMethod]
    internal static bool Contains(SpellkitSet self, SpellkitObject field) => self.Contains(field);

    [SpellkitMethod(BuiltinMethodNames.Add)]
    internal static bool AddItem(SpellkitSet self, SpellkitObject value) => self.Add(value);

    [SpellkitMethod(BuiltinMethodNames.Remove)]
    internal static bool Remove(SpellkitSet self, SpellkitObject value) => self.Remove(value);

    [SpellkitMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(SpellkitSet self) => self.Clear();

    [SpellkitMethod(BuiltinMethodNames.ToArray)]
    internal static SpellkitObject ToArray(ExecutionContext ctx, SpellkitSet self) => self.ToArray(ctx);

    [SpellkitMethod(BuiltinMethodNames.ToTuple)]
    internal static SpellkitObject ToTuple(ExecutionContext ctx, SpellkitSet self) => self.ToTuple(ctx);

    [SpellkitMethod(BuiltinMethodNames.IntersectWith)]
    internal static void IntersectWith(ExecutionContext ctx, SpellkitSet self, SpellkitObject other) =>
        self.IntersectWith(ctx, other);

    [SpellkitMethod(BuiltinMethodNames.UnionWith)]
    internal static void UnionWith(ExecutionContext ctx, SpellkitSet self, SpellkitObject other) =>
        self.UnionWith(ctx, other);

    [SpellkitMethod(BuiltinMethodNames.ExceptOf)]
    internal static void ExceptOf(ExecutionContext ctx, SpellkitSet self, SpellkitObject other) =>
        self.ExceptWith(ctx, other);

    [SpellkitMethod(BuiltinMethodNames.OverlapsWith)]
    internal static bool OverlapsWith(ExecutionContext ctx, SpellkitSet self, SpellkitObject other) =>
        self.Overlaps(ctx, other);

    [SpellkitMethod(BuiltinMethodNames.IsSubsetOf)]
    internal static bool IsSubsetOf(ExecutionContext ctx, SpellkitSet self, SpellkitObject other) =>
        self.IsSubsetOf(ctx, other);

    [SpellkitMethod(BuiltinMethodNames.IsSupersetOf)]
    internal static bool IsSupersetOf(ExecutionContext ctx, SpellkitSet self, SpellkitObject other) =>
        self.IsSupersetOf(ctx, other);

    [SpellkitStaticMethod(BuiltinMethodNames.Set)]
    internal static SpellkitObject New([VarArg]SpellkitObject values) => new SpellkitSet(((SpellkitTuple)values).ToArray());
}

internal sealed class SpellkitSetEnumerator : IEnumerator<SpellkitObject>
{
    private readonly SpellkitSet obj;
    private readonly IEnumerator<SpellkitObject> enumerator;
    private readonly int version;

    public SpellkitSetEnumerator(SpellkitSet obj)
    {
        this.obj = obj;
        version = obj.Version;
        enumerator = obj.Set.Keys.GetEnumerator();
    }

    public SpellkitObject Current => enumerator.Current;

    object IEnumerator.Current => Current;

    public void Dispose() { }

    public bool MoveNext() =>
        version != obj.Version ? throw new IterationException() : enumerator.MoveNext();

    public void Reset() => enumerator.Reset();
}
