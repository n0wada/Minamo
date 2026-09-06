using System.Collections.Generic;
using Minamo.Codegen;
using System.Linq;
using System.Collections;

namespace Minamo.Runtime.Types;

public class MinamoSet : MinamoEnumerable
{
    // An ordered map gives the set stable, insertion-ordered enumeration while
    // retaining constant-time membership checks.
    internal readonly OrderedDictionary<MinamoObject, byte> Set;

    public override string TypeName => nameof(MinamoTypeCodes.Set);
    
    public MinamoSet() : base(MinamoTypeCodes.Set) => Set = new(MinamoObjectKeyComparer.Instance);

    public MinamoSet(params MinamoObject[] args) : this((IEnumerable<MinamoObject>)args) { }

    public MinamoSet(IEnumerable<MinamoObject> values) : this()
    {
        foreach (var value in values)
        {
            Set.TryAdd(value, default);
        }
    }
    
    public override IEnumerator<MinamoObject> GetEnumerator() => new MinamoSetEnumerator(this);

    public override object ToObject() => new HashSet<MinamoObject>(Set.Keys, MinamoObjectKeyComparer.Instance);

    public override int Count => Set.Count;

    public bool Equals(ExecutionContext ctx, MinamoObject other)
    {
        var seq = MinamoIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        return SetEquals(seq);
    }

    public bool Add(MinamoObject value)
    {
        var added = Set.TryAdd(value, default);
        if (added)
        {
            Version++;
        }

        return added;
    }

    public bool Remove(MinamoObject value)
    {
        var removed = Set.Remove(value);
        if (removed)
        {
            Version++;
        }

        return removed;
    }

    public bool Contains(MinamoObject value) => Set.ContainsKey(value);

    public void Clear()
    {
        if (Set.Count == 0)
        {
            return;
        }

        Set.Clear();
        Version++;
    }

    private MinamoObject[] InternalToArray()
    {
        var arr = new MinamoObject[Set.Count];
        var count = 0;
        
        foreach (var v in Set.Keys)
        {
            arr[count++] = v;
        }

        return arr;
    }
    
    public MinamoArray ToArray(ExecutionContext _) => new(InternalToArray());

    public MinamoTuple ToTuple(ExecutionContext _) => new(InternalToArray());

    public void IntersectWith(ExecutionContext ctx, MinamoObject other)
    {
        var seq = MinamoIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return;
        }

        var values = new HashSet<MinamoObject>(seq, MinamoObjectKeyComparer.Instance);
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

    public void UnionWith(ExecutionContext ctx, MinamoObject other)
    {
        var seq = MinamoIterator.ToEnumerable(ctx, other);

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

    public void ExceptWith(ExecutionContext ctx, MinamoObject other)
    {
        var seq = MinamoIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return;
        }

        var values = new HashSet<MinamoObject>(seq, MinamoObjectKeyComparer.Instance);
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

    public bool Overlaps(ExecutionContext ctx, MinamoObject other)
    {
        var seq = MinamoIterator.ToEnumerable(ctx, other);

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

    public bool IsSubsetOf(ExecutionContext ctx, MinamoObject other)
    {
        var seq = MinamoIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        var values = new HashSet<MinamoObject>(seq, MinamoObjectKeyComparer.Instance);
        return Set.Keys.All(values.Contains);
    }

    public bool IsSupersetOf(ExecutionContext ctx, MinamoObject other)
    {
        var seq = MinamoIterator.ToEnumerable(ctx, other);

        if (ctx.HasErrors)
        {
            return false;
        }

        return new HashSet<MinamoObject>(Set.Keys, MinamoObjectKeyComparer.Instance).IsSupersetOf(seq);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var sum = 0;
            var xor = 0;
            foreach (var v in Set.Keys)
            {
                var valueHash = MinamoObjectKeyComparer.Instance.GetHashCode(v);
                sum += valueHash;
                xor ^= valueHash;
            }

            return HashCode.Combine(TypeId, Set.Count, sum, xor);
        }
    }

    public override bool Equals(MinamoObject? other)
    {
        if (other is not IEnumerable<MinamoObject> seq)
        {
            return false;
        }

        return SetEquals(seq);
    }

    private bool SetEquals(IEnumerable<MinamoObject> values) =>
        new HashSet<MinamoObject>(Set.Keys, MinamoObjectKeyComparer.Instance).SetEquals(values);
}

[MinamoType]
internal sealed partial class MinamoSetTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Set);

    public override int ReflectedTypeId => MinamoTypeCodes.Set;

    public MinamoSetTypeInfo() => AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Sequence, MinamoTypeCodes.Equatable);

    #region Operations
    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        var self = (MinamoSet)left;
        return self.Equals(ctx, right) ? True : False;
    }

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg)
    {
        var self = (MinamoSet)arg;
        return MinamoInteger.Get(self.Count);
    }

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        try
        {
            return new MinamoString("Set(" + ((IEnumerable<MinamoObject>)arg).ToLiteral(ctx) + ")");
        }
        catch (MinamoCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            MinamoTypeCodes.Array => new MinamoArray(((MinamoSet)self).ToArray()),
            MinamoTypeCodes.Tuple => new MinamoTuple(((MinamoSet)self).ToArray()),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion


    [MinamoMethod]
    internal static bool Contains(MinamoSet self, MinamoObject field) => self.Contains(field);

    [MinamoMethod(BuiltinMethodNames.Add)]
    internal static bool AddItem(MinamoSet self, MinamoObject value) => self.Add(value);

    [MinamoMethod(BuiltinMethodNames.Remove)]
    internal static bool Remove(MinamoSet self, MinamoObject value) => self.Remove(value);

    [MinamoMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(MinamoSet self) => self.Clear();

    [MinamoMethod(BuiltinMethodNames.ToArray)]
    internal static MinamoObject ToArray(ExecutionContext ctx, MinamoSet self) => self.ToArray(ctx);

    [MinamoMethod(BuiltinMethodNames.ToTuple)]
    internal static MinamoObject ToTuple(ExecutionContext ctx, MinamoSet self) => self.ToTuple(ctx);

    [MinamoMethod(BuiltinMethodNames.IntersectWith)]
    internal static void IntersectWith(ExecutionContext ctx, MinamoSet self, MinamoObject other) =>
        self.IntersectWith(ctx, other);

    [MinamoMethod(BuiltinMethodNames.UnionWith)]
    internal static void UnionWith(ExecutionContext ctx, MinamoSet self, MinamoObject other) =>
        self.UnionWith(ctx, other);

    [MinamoMethod(BuiltinMethodNames.ExceptOf)]
    internal static void ExceptOf(ExecutionContext ctx, MinamoSet self, MinamoObject other) =>
        self.ExceptWith(ctx, other);

    [MinamoMethod(BuiltinMethodNames.OverlapsWith)]
    internal static bool OverlapsWith(ExecutionContext ctx, MinamoSet self, MinamoObject other) =>
        self.Overlaps(ctx, other);

    [MinamoMethod(BuiltinMethodNames.IsSubsetOf)]
    internal static bool IsSubsetOf(ExecutionContext ctx, MinamoSet self, MinamoObject other) =>
        self.IsSubsetOf(ctx, other);

    [MinamoMethod(BuiltinMethodNames.IsSupersetOf)]
    internal static bool IsSupersetOf(ExecutionContext ctx, MinamoSet self, MinamoObject other) =>
        self.IsSupersetOf(ctx, other);

    [MinamoStaticMethod(BuiltinMethodNames.Set)]
    internal static MinamoObject New([VarArg]MinamoObject values) => new MinamoSet(((MinamoTuple)values).ToArray());
}

internal sealed class MinamoSetEnumerator : IEnumerator<MinamoObject>
{
    private readonly MinamoSet obj;
    private readonly IEnumerator<MinamoObject> enumerator;
    private readonly int version;

    public MinamoSetEnumerator(MinamoSet obj)
    {
        this.obj = obj;
        version = obj.Version;
        enumerator = obj.Set.Keys.GetEnumerator();
    }

    public MinamoObject Current => enumerator.Current;

    object IEnumerator.Current => Current;

    public void Dispose() { }

    public bool MoveNext() =>
        version != obj.Version ? throw new IterationException() : enumerator.MoveNext();

    public void Reset() => enumerator.Reset();
}
