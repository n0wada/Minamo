using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;
using System.Collections;

namespace Spellkit.Runtime.Types;

public class SpkSet : SpkEnumerable
{
    internal readonly HashSet<SpkObject> Set;

    public override string TypeName => nameof(Spk.Set);
    
    public SpkSet() : base(Spk.Set) => Set = new();

    public SpkSet(params SpkObject[] args) : base(Spk.Set) => Set = new(args);

    internal SpkSet(HashSet<SpkObject> set) : base(Spk.Set) => Set = set;
    
    public override IEnumerator<SpkObject> GetEnumerator() => new SpkSetEnumerator(this);

    public override object ToObject() => Set;

    public override int Count => Set.Count;

    public bool Equals(ExecutionContext ctx, SpkObject other)
    {
        var seq = SpkIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        return Set.SetEquals(seq);
    }

    public bool Add(SpkObject value)
    {
        var added = Set.Add(value);
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

    public bool Contains(SpkObject value) => Set.Contains(value);

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
        
        foreach (var v in Set)
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

        var count = Set.Count;
        Set.IntersectWith(seq);
        if (Set.Count != count)
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

        var count = Set.Count;
        Set.UnionWith(seq);
        if (Set.Count != count)
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

        var count = Set.Count;
        Set.ExceptWith(seq);
        if (Set.Count != count)
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

        return Set.Overlaps(seq);
    }

    public bool IsSubsetOf(ExecutionContext ctx, SpkObject other)
    {
        var seq = SpkIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        return Set.IsSubsetOf(seq);
    }

    public bool IsSupersetOf(ExecutionContext ctx, SpkObject other)
    {
        var seq = SpkIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        return Set.IsSupersetOf(seq);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;

            foreach (var v in Set)
            {
                hash = hash * 31 + v.GetHashCode();
            }

            return hash;
        }
    }

    public override bool Equals(SpkObject? other)
    {
        if (other is not IEnumerable<SpkObject> seq)
        {
            return false;
        }

        return Set.SetEquals(seq);
    }
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

    [SpkMethod]
    internal static bool Remove(SpkSet self, SpkObject value) => self.Remove(value);

    [SpkMethod]
    internal static void Clear(SpkSet self) => self.Clear();

    [SpkMethod]
    internal static SpkObject ToArray(ExecutionContext ctx, SpkSet self) => self.ToArray(ctx);

    [SpkMethod]
    internal static SpkObject ToTuple(ExecutionContext ctx, SpkSet self) => self.ToTuple(ctx);

    [SpkMethod]
    internal static void IntersectWith(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.IntersectWith(ctx, other);

    [SpkMethod]
    internal static void UnionWith(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.UnionWith(ctx, other);

    [SpkMethod]
    internal static void ExceptOf(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.ExceptWith(ctx, other);

    [SpkMethod]
    internal static bool OverlapsWith(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.Overlaps(ctx, other);

    [SpkMethod]
    internal static bool IsSubsetOf(ExecutionContext ctx, SpkSet self, SpkObject other) =>
        self.IsSubsetOf(ctx, other);

    [SpkMethod]
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
        enumerator = obj.Set.GetEnumerator();
    }

    public SpkObject Current => enumerator.Current;

    object IEnumerator.Current => Current;

    public void Dispose() { }

    public bool MoveNext() =>
        version != obj.Version ? throw new IterationException() : enumerator.MoveNext();

    public void Reset() => enumerator.Reset();
}
