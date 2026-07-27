using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;
using System.Collections;

namespace Spellkit.Runtime.Types;

public class SpkSet : SpkEnumerable
{
    // An ordered map gives the set stable, insertion-ordered enumeration while
    // retaining constant-time membership checks.
    internal readonly OrderedDictionary<SpkObject, byte> Set;

    public override string TypeName => nameof(Spk.Set);
    
    public SpkSet() : base(Spk.Set) => Set = new();

    public SpkSet(params SpkObject[] args) : this((IEnumerable<SpkObject>)args) { }

    public SpkSet(IEnumerable<SpkObject> values) : this()
    {
        foreach (var value in values)
        {
            Set.TryAdd(value, default);
        }
    }
    
    public override IEnumerator<SpkObject> GetEnumerator() => new SpkSetEnumerator(this);

    public override object ToObject() => new HashSet<SpkObject>(Set.Keys);

    public override int Count => Set.Count;

    public bool Equals(ExecutionContext ctx, SpkObject other)
    {
        var seq = SpkIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        return SetEquals(seq);
    }

    public bool Add(SpkObject value)
    {
        var added = Set.TryAdd(value, default);
        if (added)
        {
            Version++;
        }

        return added;
    }

    public bool Remove(SpkObject value)
    {
        var removed = Set.Remove(value);
        if (removed)
        {
            Version++;
        }

        return removed;
    }

    public bool Contains(SpkObject value) => Set.ContainsKey(value);

    public void Clear()
    {
        if (Set.Count == 0)
        {
            return;
        }

        Set.Clear();
        Version++;
    }

    private SpkObject[] InternalToArray()
    {
        var arr = new SpkObject[Set.Count];
        var count = 0;
        
        foreach (var v in Set.Keys)
        {
            arr[count++] = v;
        }

        return arr;
    }
    
    public SpkArray ToArray(ExecutionContext _) => new(InternalToArray());

    public SpkTuple ToTuple(ExecutionContext _) => new(InternalToArray());

    public void IntersectWith(ExecutionContext ctx, SpkObject other)
    {
        var seq = SpkIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return;
        }

        var values = new HashSet<SpkObject>(seq);
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

    public void UnionWith(ExecutionContext ctx, SpkObject other)
    {
        var seq = SpkIterator.ToEnumerable(ctx, other);

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

    public void ExceptWith(ExecutionContext ctx, SpkObject other)
    {
        var seq = SpkIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return;
        }

        var values = new HashSet<SpkObject>(seq);
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

    public bool Overlaps(ExecutionContext ctx, SpkObject other)
    {
        var seq = SpkIterator.ToEnumerable(ctx, other);

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

    public bool IsSubsetOf(ExecutionContext ctx, SpkObject other)
    {
        var seq = SpkIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        var values = new HashSet<SpkObject>(seq);
        return Set.Keys.All(values.Contains);
    }

    public bool IsSupersetOf(ExecutionContext ctx, SpkObject other)
    {
        var seq = SpkIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        return new HashSet<SpkObject>(Set.Keys).IsSupersetOf(seq);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var sum = 0;
            var xor = 0;
            foreach (var v in Set.Keys)
            {
                var valueHash = v.GetHashCode();
                sum += valueHash;
                xor ^= valueHash;
            }

            return HashCode.Combine(TypeId, Set.Count, sum, xor);
        }
    }

    public override bool Equals(SpkObject? other)
    {
        if (other is not IEnumerable<SpkObject> seq)
        {
            return false;
        }

        return SetEquals(seq);
    }

    private bool SetEquals(IEnumerable<SpkObject> values) =>
        new HashSet<SpkObject>(Set.Keys).SetEquals(values);
}

[SpkType]
internal sealed partial class SpkSetTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Set);

    public override int ReflectedTypeId => Spk.Set;

    public SpkSetTypeInfo() => AddMixins(Spk.Lookup, Spk.Sequence, Spk.Equatable);

    #region Operations
    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        var self = (SpkSet)left;
        return self.Equals(ctx, right) ? True : False;
    }

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg)
    {
        var self = (SpkSet)arg;
        return SpkInteger.Get(self.Count);
    }

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        try
        {
            return new SpkString("Set(" + ((IEnumerable<SpkObject>)arg).ToLiteral(ctx) + ")");
        }
        catch (SpkCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            Spk.Array => new SpkArray(((SpkSet)self).ToArray()),
            Spk.Tuple => new SpkTuple(((SpkSet)self).ToArray()),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion


    [SpkMethod]
    internal static bool Contains(SpkSet self, SpkObject field) => self.Contains(field);

    [SpkMethod(BuiltinMethodNames.Add)]
    internal static bool AddItem(SpkSet self, SpkObject value) => self.Add(value);

    [SpkMethod(BuiltinMethodNames.Remove)]
    internal static bool Remove(SpkSet self, SpkObject value) => self.Remove(value);

    [SpkMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(SpkSet self) => self.Clear();

    [SpkMethod(BuiltinMethodNames.ToArray)]
    internal static SpkObject ToArray(ExecutionContext ctx, SpkSet self) => self.ToArray(ctx);

    [SpkMethod(BuiltinMethodNames.ToTuple)]
    internal static SpkObject ToTuple(ExecutionContext ctx, SpkSet self) => self.ToTuple(ctx);

    [SpkMethod(BuiltinMethodNames.IntersectWith)]
    internal static void IntersectWith(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.IntersectWith(ctx, other);

    [SpkMethod(BuiltinMethodNames.UnionWith)]
    internal static void UnionWith(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.UnionWith(ctx, other);

    [SpkMethod(BuiltinMethodNames.ExceptOf)]
    internal static void ExceptOf(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.ExceptWith(ctx, other);

    [SpkMethod(BuiltinMethodNames.OverlapsWith)]
    internal static bool OverlapsWith(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.Overlaps(ctx, other);

    [SpkMethod(BuiltinMethodNames.IsSubsetOf)]
    internal static bool IsSubsetOf(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.IsSubsetOf(ctx, other);

    [SpkMethod(BuiltinMethodNames.IsSupersetOf)]
    internal static bool IsSupersetOf(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.IsSupersetOf(ctx, other);

    [SpkStaticMethod(BuiltinMethodNames.Set)]
    internal static SpkObject New([VarArg]SpkObject values) => new SpkSet(((SpkTuple)values).ToArray());
}

internal sealed class SpkSetEnumerator : IEnumerator<SpkObject>
{
    private readonly SpkSet obj;
    private readonly IEnumerator<SpkObject> enumerator;
    private readonly int version;

    public SpkSetEnumerator(SpkSet obj)
    {
        this.obj = obj;
        version = obj.Version;
        enumerator = obj.Set.Keys.GetEnumerator();
    }

    public SpkObject Current => enumerator.Current;

    object IEnumerator.Current => Current;

    public void Dispose() { }

    public bool MoveNext() =>
        version != obj.Version ? throw new IterationException() : enumerator.MoveNext();

    public void Reset() => enumerator.Reset();
}
