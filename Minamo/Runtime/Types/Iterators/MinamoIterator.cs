using System.Collections.Generic;
using Minamo.Codegen;
using System.Linq;
using Minamo.Compiler;
using Minamo.Debug;

namespace Minamo.Runtime.Types;

public abstract class MinamoIterator : MinamoObject
{
    public override string TypeName => nameof(MinamoTypeCodes.Iterator);

    protected MinamoIterator() : base(MinamoTypeCodes.Iterator) { }

    internal static MinamoIterator Create(int unitId, int handle, FastList<MinamoObject[]> captures, MinamoObject[] locals) =>
        new MinamoNativeIterator(unitId, handle, captures, locals);

    public static MinamoIterator Create(IEnumerable<MinamoObject> seq) => new MinamoForeignIterator(seq);

    public abstract MinamoFunction GetIteratorFunction();

    public abstract IEnumerable<MinamoObject> ToEnumerable(ExecutionContext ctx);

    public static IEnumerable<MinamoObject> ToEnumerable(ExecutionContext ctx, MinamoObject val)
    {
        if (val is IEnumerable<MinamoObject> seq)
        {
            return seq;
        }
        else
        {
            var iter = val.GetIterator(ctx);
            return InternalRun(ctx, iter);
        }
    }

    private static IEnumerable<MinamoObject> InternalRun(ExecutionContext ctx, MinamoFunction? iter)
    {
        if (iter is null)
        {
            yield break;
        }

        while (true)
        {
            var res = iter.Call(ctx);

            if (!ReferenceEquals(res, MinamoNil.Terminator))
            {
                yield return res;
            }
            else
            {
                yield break;
            }
        }
    }
}

[MinamoType]
internal sealed partial class MinamoIteratorTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Iterator);

    public override int ReflectedTypeId => MinamoTypeCodes.Iterator;

    public MinamoIteratorTypeInfo() => AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Sequence);

    #region Operations
    protected override MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) => MinamoIterator.Create(Concat(ctx, left, right));

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject self)
    {
        var seq = MinamoIterator.ToEnumerable(ctx, self);
        return ctx.HasErrors ? Nil : MinamoInteger.Get(seq.Count());
    }

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index)
    {
        if (index is not MinamoInteger ix)
        {
            return ctx.IndexOutOfRange(index);
        }

        if (!ix.TryGetInt32(out var i))
        {
            return ctx.IndexOutOfRange(index);
        }

        try
        {
            var iter = MinamoIterator.ToEnumerable(ctx, self);
            return i < 0 ? iter.ElementAt(^-i) : iter.ElementAt(i);
        }
        catch (ArgumentOutOfRangeException)
        {
            ctx.Error = ErrorGenerators.RuntimeException(MinamoError.IndexOutOfRange, index);
            return Nil;
        }
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            MinamoTypeCodes.Tuple => new MinamoTuple(((MinamoIterator)self).ToEnumerable(ctx).ToArray()),
            MinamoTypeCodes.Array => new MinamoArray(((MinamoIterator)self).ToEnumerable(ctx).ToArray()),
            MinamoTypeCodes.Function => ((MinamoIterator)self).GetIteratorFunction(),
            MinamoTypeCodes.Set => ConvertToSet(ctx, self),
            _ => base.CastOp(ctx, self, targetType)
        };

    private static MinamoObject ConvertToSet(ExecutionContext ctx, MinamoObject self)
    {
        var seq = MinamoIterator.ToEnumerable(ctx, self);

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return new MinamoSet(seq);
    }
    #endregion

    [MinamoMethod]
    internal static bool Contains(ExecutionContext ctx, IEnumerable<MinamoObject> self, MinamoObject item) =>
        self.Any(o => o.Equals(item, ctx));

    [MinamoMethod(BuiltinMethodNames.ToTuple)]
    internal static MinamoObject ToTuple(IEnumerable<MinamoObject> self) => new MinamoTuple(self.ToArray());

    [MinamoMethod(BuiltinMethodNames.ToDictionary)]
    internal static MinamoObject ToDictionary(ExecutionContext ctx, IEnumerable<MinamoObject> self, MinamoFunction keySelector, MinamoFunction? valueSelector = null)
    {
        var map = new MinamoDictionary();

        foreach (var item in self)
        {
            var key = keySelector.Call(ctx, item);
            if (ctx.HasErrors)
            {
                return Nil;
            }

            var value = valueSelector is null ? item : valueSelector.Call(ctx, item);
            if (ctx.HasErrors)
            {
                return Nil;
            }

            if (!map.TryAdd(key, value))
            {
                return ctx.KeyAlreadyPresent(key);
            }
        }

        return map;
    }

    [MinamoMethod]
    internal static MinamoObject Fold(ExecutionContext ctx, IEnumerable<MinamoObject> self, [Default]MinamoObject seed, MinamoFunction accumulator)
    {
        if (seed is not null)
        {
            return self.Aggregate(seed, (seed, val) => accumulator.Call(ctx, seed, val));
        }
        else
        {
            return self.Aggregate((seed, val) => accumulator.Call(ctx, seed, val));
        }
    }

    [MinamoMethod(BuiltinMethodNames.First)]
    internal static MinamoObject First(IEnumerable<MinamoObject> self) => self.FirstOrDefault() ?? Nil;

    [MinamoMethod]
    internal static MinamoObject Single(IEnumerable<MinamoObject> self)
    {
        var two = self.Take(2).ToList();

        if (two.Count > 1 || two.Count == 0)
        {
            return Nil;
        }

        return two[0];
    }

    [MinamoMethod(BuiltinMethodNames.Last)]
    internal static MinamoObject Last(ExecutionContext ctx, MinamoObject self) =>
        MinamoIterator.ToEnumerable(ctx, self).LastOrDefault() ?? Nil;

    [MinamoMethod(BuiltinMethodNames.Reverse)]
    internal static IEnumerable<MinamoObject> Reverse(IEnumerable<MinamoObject> self) => self.Reverse();

    [MinamoMethod(BuiltinMethodNames.Slice)]
    internal static IEnumerable<MinamoObject> Slice(IEnumerable<MinamoObject> self, int index = 0, int? endIndex = null)
    {
        int? count = null;

        if (index < 0)
        {
            index = (count ??= self.Count()) + index;
        }

        if (endIndex is null)
        {
            if (index == 0)
            {
                return self;
            }

            return self.Skip(index);
        }

        if (endIndex < 0)
        {
            endIndex = (count ?? self.Count()) + endIndex - 1;
        }

        return self.Skip(index).Take(endIndex.Value - index + 1);
    }

    [MinamoMethod(BuiltinMethodNames.ElementAt)]
    internal static MinamoObject ElementAt(IEnumerable<MinamoObject> self, int index)
    {
        try
        {
            return index < 0 ? self.ElementAt(^-index) : self.ElementAt(index);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, index);
        }
    }

    [MinamoMethod(BuiltinMethodNames.Sort)]
    internal static IEnumerable<MinamoObject> Sort(ExecutionContext ctx, IEnumerable<MinamoObject> self, MinamoFunction? comparer = null)
    {
        var sortComparer = new SortComparer(comparer, ctx);
        return self.OrderBy(item => item, sortComparer);
    }

    [MinamoMethod(BuiltinMethodNames.Shuffle)]
    internal static IEnumerable<MinamoObject> Shuffle(IEnumerable<MinamoObject> self) => ShuffleCore(self);

    private static IEnumerable<MinamoObject> ShuffleCore(IEnumerable<MinamoObject> self)
    {
        var values = self.ToArray();
        for (var i = values.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }

        foreach (var value in values)
        {
            yield return value;
        }
    }

    [MinamoMethod(BuiltinMethodNames.Count)]
    internal static int Count(ExecutionContext ctx, IEnumerable<MinamoObject> self, MinamoFunction? predicate = null)
    {
        var count = 0;

        if (predicate is null)
        {
            foreach (var _ in self)
            {
                count++;
            }

            return count;
        }

        foreach (var item in self)
        {
            var result = predicate.Call(ctx, item);
            if (ctx.HasErrors)
            {
                break;
            }

            if (result.IsTrue())
            {
                count++;
            }
        }

        return count;
    }

    [MinamoMethod(BuiltinMethodNames.TakeWhile)]
    internal static IEnumerable<MinamoObject> TakeWhile(ExecutionContext ctx, IEnumerable<MinamoObject> self, MinamoFunction predicate) =>
        new TakeWhileEnumerable(ctx, self, predicate);

    [MinamoMethod(BuiltinMethodNames.SkipWhile)]
    internal static IEnumerable<MinamoObject> SkipWhile(ExecutionContext ctx, IEnumerable<MinamoObject> self, MinamoFunction predicate) =>
        new SkipWhileEnumerable(ctx, self, predicate);

    [MinamoMethod(BuiltinMethodNames.ForEach)]
    internal static void ForEach(ExecutionContext ctx, IEnumerable<MinamoObject> self, MinamoFunction action)
    {
        foreach (var o in self)
        {
            action.Call(ctx, o);
        }
    }

    [MinamoMethod(BuiltinMethodNames.Distinct)]
    internal static IEnumerable<MinamoObject> Distinct(ExecutionContext ctx, IEnumerable<MinamoObject> self, MinamoFunction? selector = null)
    {
        if (selector is not null)
        {
            return new DistinctByEnumerable(ctx, self, selector);
        }
        else
        {
            return self.Distinct(MinamoObjectKeyComparer.Instance);
        }
    }

    [MinamoStaticMethod(BuiltinMethodNames.Concat)]
    internal static IEnumerable<MinamoObject> Concat(ExecutionContext ctx, params MinamoObject[] values) =>
        new MultiPartEnumerable(ctx, values);

    [MinamoStaticMethod(BuiltinMethodNames.Iterator)]
    internal static IEnumerable<MinamoObject> Iterator(ExecutionContext ctx, params MinamoObject[] values) => Concat(ctx, values);

    [MinamoStaticMethod(BuiltinMethodNames.Range)]
    internal static IEnumerable<MinamoObject> Range(ExecutionContext ctx, [Default(0)]MinamoObject start, [Default]MinamoObject end, [Default(1)]MinamoObject step, bool exclusive = false) =>
        GenerateRange(ctx, start, end ?? Nil, step, exclusive);

    private static IEnumerable<MinamoObject> GenerateRange(ExecutionContext ctx, MinamoObject start, MinamoObject end, MinamoObject step, bool exclusive)
    {
        var elem = start;
        var inf = end.TypeId is MinamoTypeCodes.Nil;
        var up = step.Greater(MinamoInteger.Zero, ctx);
        var down = !up && step.Lesser(MinamoInteger.Zero, ctx);

        if (ctx.HasErrors)
        {
            yield break;
        }

        if (!up && !down)
        {
            ctx.InvalidValue(step);
            yield break;
        }

        if (inf)
        {
            while (true)
            {
                yield return elem;
                if (!TryAdvance(ctx, elem, step, out elem))
                {
                    yield break;
                }
            }
        }

        Func<MinamoObject, MinamoObject, ExecutionContext, bool> predicate =
            up && exclusive ? Extensions.Lesser :
                (
                    up ? Extensions.LesserOrEquals
                    : exclusive ? Extensions.Greater : Extensions.GreaterOrEquals
                );

        while (predicate(elem, end, ctx))
        {
            yield return elem;

            if (ctx.HasErrors || (!exclusive && elem.Equals(end, ctx)))
            {
                yield break;
            }

            if (!TryAdvance(ctx, elem, step, out elem))
            {
                yield break;
            }
        }
    }

    private static bool TryAdvance(
        ExecutionContext ctx,
        MinamoObject current,
        MinamoObject step,
        out MinamoObject next)
    {
        if (current is MinamoInteger integer && step is MinamoInteger integerStep)
        {
            try
            {
                next = MinamoInteger.Get(checked(integer.Value + integerStep.Value));
                return true;
            }
            catch (OverflowException)
            {
                ctx.Overflow();
                next = Nil;
                return false;
            }
        }

        next = current.Add(step, ctx);
        return !ctx.HasErrors;
    }

    [MinamoStaticMethod(BuiltinMethodNames.Empty)]
    internal static IEnumerable<MinamoObject> Empty() => Enumerable.Empty<MinamoObject>();

    [MinamoStaticMethod(BuiltinMethodNames.Repeat)]
    internal static IEnumerable<MinamoObject> Repeat(ExecutionContext ctx, MinamoObject value) => Repeater(ctx, value);

    private static IEnumerable<MinamoObject> Repeater(ExecutionContext ctx, MinamoObject val)
    {
        if (val.TypeId is MinamoTypeCodes.Iterator)
        {
            val = ((MinamoIterator)val).GetIteratorFunction();
        }

        if (val is MinamoFunction func)
        {
            while (true)
            {
                var res = func.Call(ctx);
                yield return res;
            }
        }
        else
        {
            while (true)
            {
                yield return val;
            }
        }
    }
}

internal sealed class MinamoIteratorFunction : MinamoForeignFunction
{
    private readonly IEnumerable<MinamoObject> enumerable;
    private IEnumerator<MinamoObject>? enumerator;

    public MinamoIteratorFunction(IEnumerable<MinamoObject> enumerable) : base(Builtins.Iterate, Array.Empty<Par>(), -1) =>
        this.enumerable = enumerable;

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, params MinamoObject[] args)
    {
        if (enumerator is null)
        {
            enumerator = enumerable.GetEnumerator();
        }

        if (enumerator.MoveNext())
        {
            return enumerator.Current;
        }

        enumerator = null;
        return MinamoNil.Terminator;
    }

    public override int GetHashCode() => enumerable.GetHashCode();

    protected override bool Equals(MinamoFunction func) => func is MinamoIteratorFunction f && f.enumerable.Equals(enumerator);

    public override MinamoObject Clone() => new MinamoIteratorFunction(enumerable);
}

internal sealed class MinamoNativeIteratorFunction : MinamoNativeFunction
{
    public override string FunctionName => "Iterate";

    public MinamoNativeIteratorFunction(int unitId, int funcId, FastList<MinamoObject[]> captures)
        : base(null, unitId, funcId, captures, -1) { }

    internal override MinamoFunction BindToInstance(ExecutionContext ctx, MinamoObject arg) =>
        new MinamoNativeIteratorFunction(UnitId, FunctionId, Captures) { Self = arg };
}
