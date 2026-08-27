using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;
using Spellkit.Compiler;
using Spellkit.Debug;

namespace Spellkit.Runtime.Types;

public abstract class SpellkitIterator : SpellkitObject
{
    public override string TypeName => nameof(SpellkitTypeCodes.Iterator);

    protected SpellkitIterator() : base(SpellkitTypeCodes.Iterator) { }

    internal static SpellkitIterator Create(int unitId, int handle, FastList<SpellkitObject[]> captures, SpellkitObject[] locals) =>
        new SpellkitNativeIterator(unitId, handle, captures, locals);

    public static SpellkitIterator Create(IEnumerable<SpellkitObject> seq) => new SpellkitForeignIterator(seq);

    public abstract SpellkitFunction GetIteratorFunction();

    public abstract IEnumerable<SpellkitObject> ToEnumerable(ExecutionContext ctx);

    public static IEnumerable<SpellkitObject> ToEnumerable(ExecutionContext ctx, SpellkitObject val)
    {
        if (val is IEnumerable<SpellkitObject> seq)
        {
            return seq;
        }
        else
        {
            var iter = val.GetIterator(ctx);
            return InternalRun(ctx, iter);
        }
    }

    private static IEnumerable<SpellkitObject> InternalRun(ExecutionContext ctx, SpellkitFunction? iter)
    {
        if (iter is null)
        {
            yield break;
        }

        while (true)
        {
            var res = iter.Call(ctx);

            if (!ReferenceEquals(res, SpellkitNil.Terminator))
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

[SpellkitType]
internal sealed partial class SpellkitIteratorTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Iterator);

    public override int ReflectedTypeId => SpellkitTypeCodes.Iterator;

    public SpellkitIteratorTypeInfo() => AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Sequence);

    #region Operations
    protected override SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) => SpellkitIterator.Create(Concat(ctx, left, right));

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject self)
    {
        var seq = SpellkitIterator.ToEnumerable(ctx, self);
        return ctx.HasErrors ? Nil : SpellkitInteger.Get(seq.Count());
    }

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index)
    {
        if (index is not SpellkitInteger ix)
        {
            return ctx.IndexOutOfRange(index);
        }

        if (!ix.TryGetInt32(out var i))
        {
            return ctx.IndexOutOfRange(index);
        }

        try
        {
            var iter = SpellkitIterator.ToEnumerable(ctx, self);
            return i < 0 ? iter.ElementAt(^-i) : iter.ElementAt(i);
        }
        catch (ArgumentOutOfRangeException)
        {
            ctx.Error = ErrorGenerators.RuntimeException(SpellkitError.IndexOutOfRange, index);
            return Nil;
        }
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Tuple => new SpellkitTuple(((SpellkitIterator)self).ToEnumerable(ctx).ToArray()),
            SpellkitTypeCodes.Array => new SpellkitArray(((SpellkitIterator)self).ToEnumerable(ctx).ToArray()),
            SpellkitTypeCodes.Function => ((SpellkitIterator)self).GetIteratorFunction(),
            SpellkitTypeCodes.Set => ConvertToSet(ctx, self),
            _ => base.CastOp(ctx, self, targetType)
        };

    private static SpellkitObject ConvertToSet(ExecutionContext ctx, SpellkitObject self)
    {
        var seq = SpellkitIterator.ToEnumerable(ctx, self);

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return new SpellkitSet(seq);
    }
    #endregion

    [SpellkitMethod]
    internal static bool Contains(ExecutionContext ctx, IEnumerable<SpellkitObject> self, SpellkitObject item) =>
        self.Any(o => o.Equals(item, ctx));

    [SpellkitMethod(BuiltinMethodNames.ToTuple)]
    internal static SpellkitObject ToTuple(IEnumerable<SpellkitObject> self) => new SpellkitTuple(self.ToArray());

    [SpellkitMethod(BuiltinMethodNames.ToDictionary)]
    internal static SpellkitObject ToDictionary(ExecutionContext ctx, IEnumerable<SpellkitObject> self, SpellkitFunction keySelector, SpellkitFunction? valueSelector = null)
    {
        var map = new SpellkitDictionary();

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

    [SpellkitMethod]
    internal static SpellkitObject Fold(ExecutionContext ctx, IEnumerable<SpellkitObject> self, [Default]SpellkitObject seed, SpellkitFunction accumulator)
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

    [SpellkitMethod(BuiltinMethodNames.First)]
    internal static SpellkitObject First(IEnumerable<SpellkitObject> self) => self.FirstOrDefault() ?? Nil;

    [SpellkitMethod]
    internal static SpellkitObject Single(IEnumerable<SpellkitObject> self)
    {
        var two = self.Take(2).ToList();

        if (two.Count > 1 || two.Count == 0)
        {
            return Nil;
        }

        return two[0];
    }

    [SpellkitMethod(BuiltinMethodNames.Last)]
    internal static SpellkitObject Last(ExecutionContext ctx, SpellkitObject self) =>
        SpellkitIterator.ToEnumerable(ctx, self).LastOrDefault() ?? Nil;

    [SpellkitMethod(BuiltinMethodNames.Reverse)]
    internal static IEnumerable<SpellkitObject> Reverse(IEnumerable<SpellkitObject> self) => self.Reverse();

    [SpellkitMethod(BuiltinMethodNames.Slice)]
    internal static IEnumerable<SpellkitObject> Slice(IEnumerable<SpellkitObject> self, int index = 0, int? endIndex = null)
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

    [SpellkitMethod(BuiltinMethodNames.ElementAt)]
    internal static SpellkitObject ElementAt(IEnumerable<SpellkitObject> self, int index)
    {
        try
        {
            return index < 0 ? self.ElementAt(^-index) : self.ElementAt(index);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, index);
        }
    }

    [SpellkitMethod(BuiltinMethodNames.Sort)]
    internal static IEnumerable<SpellkitObject> Sort(ExecutionContext ctx, IEnumerable<SpellkitObject> self, SpellkitFunction? comparer = null)
    {
        var sortComparer = new SortComparer(comparer, ctx);
        return self.OrderBy(item => item, sortComparer);
    }

    [SpellkitMethod(BuiltinMethodNames.Shuffle)]
    internal static IEnumerable<SpellkitObject> Shuffle(IEnumerable<SpellkitObject> self) => ShuffleCore(self);

    private static IEnumerable<SpellkitObject> ShuffleCore(IEnumerable<SpellkitObject> self)
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

    [SpellkitMethod(BuiltinMethodNames.Count)]
    internal static int Count(ExecutionContext ctx, IEnumerable<SpellkitObject> self, SpellkitFunction? predicate = null)
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

    [SpellkitMethod(BuiltinMethodNames.TakeWhile)]
    internal static IEnumerable<SpellkitObject> TakeWhile(ExecutionContext ctx, IEnumerable<SpellkitObject> self, SpellkitFunction predicate) =>
        new TakeWhileEnumerable(ctx, self, predicate);

    [SpellkitMethod(BuiltinMethodNames.SkipWhile)]
    internal static IEnumerable<SpellkitObject> SkipWhile(ExecutionContext ctx, IEnumerable<SpellkitObject> self, SpellkitFunction predicate) =>
        new SkipWhileEnumerable(ctx, self, predicate);

    [SpellkitMethod(BuiltinMethodNames.ForEach)]
    internal static void ForEach(ExecutionContext ctx, IEnumerable<SpellkitObject> self, SpellkitFunction action)
    {
        foreach (var o in self)
        {
            action.Call(ctx, o);
        }
    }

    [SpellkitMethod(BuiltinMethodNames.Distinct)]
    internal static IEnumerable<SpellkitObject> Distinct(ExecutionContext ctx, IEnumerable<SpellkitObject> self, SpellkitFunction? selector = null)
    {
        if (selector is not null)
        {
            return new DistinctByEnumerable(ctx, self, selector);
        }
        else
        {
            return self.Distinct(SpellkitObjectKeyComparer.Instance);
        }
    }

    [SpellkitStaticMethod(BuiltinMethodNames.Concat)]
    internal static IEnumerable<SpellkitObject> Concat(ExecutionContext ctx, params SpellkitObject[] values) =>
        new MultiPartEnumerable(ctx, values);

    [SpellkitStaticMethod(BuiltinMethodNames.Iterator)]
    internal static IEnumerable<SpellkitObject> Iterator(ExecutionContext ctx, params SpellkitObject[] values) => Concat(ctx, values);

    [SpellkitStaticMethod(BuiltinMethodNames.Range)]
    internal static IEnumerable<SpellkitObject> Range(ExecutionContext ctx, [Default(0)]SpellkitObject start, [Default]SpellkitObject end, [Default(1)]SpellkitObject step, bool exclusive = false) =>
        GenerateRange(ctx, start, end ?? Nil, step, exclusive);

    private static IEnumerable<SpellkitObject> GenerateRange(ExecutionContext ctx, SpellkitObject start, SpellkitObject end, SpellkitObject step, bool exclusive)
    {
        var elem = start;
        var inf = end.TypeId is SpellkitTypeCodes.Nil;
        var up = step.Greater(SpellkitInteger.Zero, ctx);
        var down = !up && step.Lesser(SpellkitInteger.Zero, ctx);

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

        Func<SpellkitObject, SpellkitObject, ExecutionContext, bool> predicate =
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
        SpellkitObject current,
        SpellkitObject step,
        out SpellkitObject next)
    {
        if (current is SpellkitInteger integer && step is SpellkitInteger integerStep)
        {
            try
            {
                next = SpellkitInteger.Get(checked(integer.Value + integerStep.Value));
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

    [SpellkitStaticMethod(BuiltinMethodNames.Empty)]
    internal static IEnumerable<SpellkitObject> Empty() => Enumerable.Empty<SpellkitObject>();

    [SpellkitStaticMethod(BuiltinMethodNames.Repeat)]
    internal static IEnumerable<SpellkitObject> Repeat(ExecutionContext ctx, SpellkitObject value) => Repeater(ctx, value);

    private static IEnumerable<SpellkitObject> Repeater(ExecutionContext ctx, SpellkitObject val)
    {
        if (val.TypeId is SpellkitTypeCodes.Iterator)
        {
            val = ((SpellkitIterator)val).GetIteratorFunction();
        }

        if (val is SpellkitFunction func)
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

internal sealed class SpellkitIteratorFunction : SpellkitForeignFunction
{
    private readonly IEnumerable<SpellkitObject> enumerable;
    private IEnumerator<SpellkitObject>? enumerator;

    public SpellkitIteratorFunction(IEnumerable<SpellkitObject> enumerable) : base(Builtins.Iterate, Array.Empty<Par>(), -1) =>
        this.enumerable = enumerable;

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, params SpellkitObject[] args)
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
        return SpellkitNil.Terminator;
    }

    public override int GetHashCode() => enumerable.GetHashCode();

    protected override bool Equals(SpellkitFunction func) => func is SpellkitIteratorFunction f && f.enumerable.Equals(enumerator);

    public override SpellkitObject Clone() => new SpellkitIteratorFunction(enumerable);
}

internal sealed class SpellkitNativeIteratorFunction : SpellkitNativeFunction
{
    public override string FunctionName => "Iterate";

    public SpellkitNativeIteratorFunction(int unitId, int funcId, FastList<SpellkitObject[]> captures)
        : base(null, unitId, funcId, captures, -1) { }

    internal override SpellkitFunction BindToInstance(ExecutionContext ctx, SpellkitObject arg) =>
        new SpellkitNativeIteratorFunction(UnitId, FunctionId, Captures) { Self = arg };
}
