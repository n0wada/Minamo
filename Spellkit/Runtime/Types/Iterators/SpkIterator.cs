using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;
using Spellkit.Compiler;
using Spellkit.Debug;

namespace Spellkit.Runtime.Types;

public abstract class SpkIterator : SpkObject
{
    public override string TypeName => nameof(Spk.Iterator);

    protected SpkIterator() : base(Spk.Iterator) { }

    internal static SpkIterator Create(int unitId, int handle, FastList<SpkObject[]> captures, SpkObject[] locals) =>
        new SpkNativeIterator(unitId, handle, captures, locals);

    public static SpkIterator Create(IEnumerable<SpkObject> seq) => new SpkForeignIterator(seq);

    public abstract SpkFunction GetIteratorFunction();

    public abstract IEnumerable<SpkObject> ToEnumerable(ExecutionContext ctx);

    public static IEnumerable<SpkObject> ToEnumerable(ExecutionContext ctx, SpkObject val)
    {
        if (val is IEnumerable<SpkObject> seq)
        {
            return seq;
        }
        else
        {
            var iter = val.GetIterator(ctx);
            return InternalRun(ctx, iter);
        }
    }

    private static IEnumerable<SpkObject> InternalRun(ExecutionContext ctx, SpkFunction? iter)
    {
        if (iter is null)
        {
            yield break;
        }

        while (true)
        {
            var res = iter.Call(ctx);

            if (!ReferenceEquals(res, SpkNil.Terminator))
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

[SpkType]
internal sealed partial class SpkIteratorTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Iterator);

    public override int ReflectedTypeId => Spk.Iterator;

    public SpkIteratorTypeInfo() => AddMixins(Spk.Lookup);

    #region Operations
    protected override SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right) => SpkIterator.Create(Concat(ctx, left, right));

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject self)
    {
        var seq = SpkIterator.ToEnumerable(ctx, self);
        return ctx.HasErrors ? Nil : SpkInteger.Get(seq.Count());
    }

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index)
    {
        if (index is not SpkInteger ix)
        {
            return ctx.IndexOutOfRange(index);
        }

        if (!ix.TryGetInt32(out var i))
        {
            return ctx.IndexOutOfRange(index);
        }

        try
        {
            var iter = SpkIterator.ToEnumerable(ctx, self);
            return i < 0 ? iter.ElementAt(^-i) : iter.ElementAt(i);
        }
        catch (ArgumentOutOfRangeException)
        {
            ctx.Error = ErrorGenerators.RuntimeException(SpkError.IndexOutOfRange, index);
            return Nil;
        }
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            Spk.Tuple => new SpkTuple(((SpkIterator)self).ToEnumerable(ctx).ToArray()),
            Spk.Array => new SpkArray(((SpkIterator)self).ToEnumerable(ctx).ToArray()),
            Spk.Function => ((SpkIterator)self).GetIteratorFunction(),
            Spk.Set => ConvertToSet(ctx, self),
            _ => base.CastOp(ctx, self, targetType)
        };

    private static SpkObject ConvertToSet(ExecutionContext ctx, SpkObject self)
    {
        var seq = SpkIterator.ToEnumerable(ctx, self);

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return ToSet(seq);
    }
    #endregion

    [SpkMethod]
    internal static bool Contains(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkObject item) =>
        self.Any(o => o.Equals(item, ctx));

    [SpkMethod(BuiltinMethodNames.ToArray)]
    internal static SpkObject ToArray(IEnumerable<SpkObject> self) => new SpkArray(self.ToArray());

    [SpkMethod(BuiltinMethodNames.ToTuple)]
    internal static SpkObject ToTuple(IEnumerable<SpkObject> self) => new SpkTuple(self.ToArray());

    [SpkMethod(BuiltinMethodNames.ToDictionary)]
    internal static SpkObject ToDictionary(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction keySelector, SpkFunction? valueSelector = null)
    {
        try
        {
            var map =
                valueSelector is not null
                ? self.ToDictionary(item => keySelector.Call(ctx, item), item => valueSelector.Call(ctx, item))
                : self.ToDictionary(item => keySelector.Call(ctx, item));
            return new SpkDictionary(map);
        }
        catch (ArgumentException)
        {
            return ctx.KeyAlreadyPresent();
        }
    }

    [SpkMethod]
    internal static SpkObject Fold(ExecutionContext ctx, IEnumerable<SpkObject> self, [Default]SpkObject seed, SpkFunction accumulator)
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

    [SpkMethod(BuiltinMethodNames.Take)]
    internal static IEnumerable<SpkObject> Take(IEnumerable<SpkObject> self, int count) => self.Take(count < 0 ? 0 : count);

    [SpkMethod(BuiltinMethodNames.Skip)]
    internal static IEnumerable<SpkObject> Skip(IEnumerable<SpkObject> self, int count) => self.Skip(count < 0 ? 0 : count);

    [SpkMethod(BuiltinMethodNames.First)]
    internal static SpkObject First(IEnumerable<SpkObject> self) => self.FirstOrDefault() ?? Nil;

    [SpkMethod]
    internal static SpkObject Single(IEnumerable<SpkObject> self)
    {
        var two = self.Take(2).ToList();

        if (two.Count > 1 || two.Count == 0)
        {
            return Nil;
        }

        return two[0];
    }

    [SpkMethod(BuiltinMethodNames.Last)]
    internal static SpkObject Last(ExecutionContext ctx, SpkObject self) =>
        SpkIterator.ToEnumerable(ctx, self).LastOrDefault() ?? Nil;

    [SpkMethod(BuiltinMethodNames.Reverse)]
    internal static IEnumerable<SpkObject> Reverse(IEnumerable<SpkObject> self) => self.Reverse();

    [SpkMethod(BuiltinMethodNames.Slice)]
    internal static IEnumerable<SpkObject> Slice(IEnumerable<SpkObject> self, int index = 0, int? endIndex = null)
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

    [SpkMethod(BuiltinMethodNames.ElementAt)]
    internal static SpkObject ElementAt(IEnumerable<SpkObject> self, int index)
    {
        try
        {
            return index < 0 ? self.ElementAt(^-index) : self.ElementAt(index);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, index);
        }
    }

    [SpkMethod(BuiltinMethodNames.Sort)]
    internal static IEnumerable<SpkObject> Sort(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction? comparer = null)
    {
        var sortComparer = new SortComparer(comparer, ctx);
        return self.OrderBy(item => item, sortComparer);
    }

    [SpkMethod(BuiltinMethodNames.Shuffle)]
    internal static IEnumerable<SpkObject> Shuffle(IEnumerable<SpkObject> self)
    {
        var rnd = new Random();
        var last = 0;

        int sorter(SpkObject _)
        {
            var n = rnd.Next();
            if (last != 0 && n > last)
            {
                n = -n;
            }

            last = n;
            return n;
        }

        return self.OrderBy(sorter);
    }

    [SpkMethod(BuiltinMethodNames.Count)]
    internal static int Count(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction? predicate = null)
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

    [SpkMethod(BuiltinMethodNames.Map)]
    internal static IEnumerable<SpkObject> Map(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction converter) =>
        new MapEnumerable(ctx, self, converter);

    [SpkMethod(BuiltinMethodNames.Filter)]
    internal static IEnumerable<SpkObject> Filter(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction predicate) =>
        new FilterEnumerable(ctx, self, predicate);

    [SpkMethod(BuiltinMethodNames.TakeWhile)]
    internal static IEnumerable<SpkObject> TakeWhile(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction predicate) =>
        new TakeWhileEnumerable(ctx, self, predicate);

    [SpkMethod(BuiltinMethodNames.SkipWhile)]
    internal static IEnumerable<SpkObject> SkipWhile(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction predicate) =>
        new SkipWhileEnumerable(ctx, self, predicate);

    [SpkMethod(BuiltinMethodNames.Reduce)]
    internal static SpkObject Reduce(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction converter, [Default(0)]SpkObject initial)
    {
        var result = initial;

        foreach (var item in self)
        {
            result = converter.Call(ctx, result, item);
            if (ctx.HasErrors)
            {
                return Nil;
            }
        }

        return result;
    }

    [SpkMethod(BuiltinMethodNames.Any)]
    internal static bool Any(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction predicate)
    {
        foreach (var item in self)
        {
            var result = predicate.Call(ctx, item);
            if (ctx.HasErrors)
            {
                return false;
            }

            if (result.IsTrue())
            {
                return true;
            }
        }

        return false;
    }

    [SpkMethod(BuiltinMethodNames.All)]
    internal static bool All(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction predicate)
    {
        foreach (var item in self)
        {
            var result = predicate.Call(ctx, item);
            if (ctx.HasErrors || !result.IsTrue())
            {
                return false;
            }
        }

        return true;
    }

    [SpkMethod(BuiltinMethodNames.ForEach)]
    internal static void ForEach(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction action)
    {
        foreach (var o in self)
        {
            action.Call(ctx, o);
        }
    }

    [SpkMethod(BuiltinMethodNames.ToSet)]
    internal static SpkObject ToSet(IEnumerable<SpkObject> self)
    {
        var set = new HashSet<SpkObject>();
        set.UnionWith(self);
        return new SpkSet(set);
    }

    [SpkMethod(BuiltinMethodNames.Distinct)]
    internal static IEnumerable<SpkObject> Distinct(ExecutionContext ctx, IEnumerable<SpkObject> self, SpkFunction? selector = null)
    {
        if (selector is not null)
        {
            return self.Distinct(new EqualityComparer(ctx, selector));
        }
        else
        {
            return self.Distinct();
        }
    }

    [SpkStaticMethod(BuiltinMethodNames.Concat)]
    internal static IEnumerable<SpkObject> Concat(ExecutionContext ctx, params SpkObject[] values) =>
        new MultiPartEnumerable(ctx, values);

    [SpkStaticMethod(BuiltinMethodNames.Iterator)]
    internal static IEnumerable<SpkObject> Iterator(ExecutionContext ctx, params SpkObject[] values) => Concat(ctx, values);

    [SpkStaticMethod(BuiltinMethodNames.Range)]
    internal static IEnumerable<SpkObject> Range(ExecutionContext ctx, [Default(0)]SpkObject start, [Default]SpkObject end, [Default(1)]SpkObject step, bool exclusive = false) =>
        GenerateRange(ctx, start, end ?? Nil, step, exclusive);

    private static IEnumerable<SpkObject> GenerateRange(ExecutionContext ctx, SpkObject start, SpkObject end, SpkObject step, bool exclusive)
    {
        var elem = start;
        var inf = end.TypeId is Spk.Nil;
        var up = step.Greater(SpkInteger.Zero, ctx);
        var down = !up && step.Lesser(SpkInteger.Zero, ctx);

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

        Func<SpkObject, SpkObject, ExecutionContext, bool> predicate =
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
        SpkObject current,
        SpkObject step,
        out SpkObject next)
    {
        if (current is SpkInteger integer && step is SpkInteger integerStep)
        {
            try
            {
                next = SpkInteger.Get(checked(integer.Value + integerStep.Value));
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

    [SpkStaticMethod(BuiltinMethodNames.Empty)]
    internal static IEnumerable<SpkObject> Empty() => Enumerable.Empty<SpkObject>();

    [SpkStaticMethod(BuiltinMethodNames.Repeat)]
    internal static IEnumerable<SpkObject> Repeat(ExecutionContext ctx, SpkObject value) => Repeater(ctx, value);

    private static IEnumerable<SpkObject> Repeater(ExecutionContext ctx, SpkObject val)
    {
        if (val.TypeId is Spk.Iterator)
        {
            val = ((SpkIterator)val).GetIteratorFunction();
        }

        if (val is SpkFunction func)
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

internal sealed class SpkIteratorFunction : SpkForeignFunction
{
    private readonly IEnumerable<SpkObject> enumerable;
    private IEnumerator<SpkObject>? enumerator;

    public SpkIteratorFunction(IEnumerable<SpkObject> enumerable) : base(Builtins.Iterate, Array.Empty<Par>(), -1) =>
        this.enumerable = enumerable;

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, params SpkObject[] args)
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
        return SpkNil.Terminator;
    }

    public override int GetHashCode() => enumerable.GetHashCode();

    protected override bool Equals(SpkFunction func) => func is SpkIteratorFunction f && f.enumerable.Equals(enumerator);

    public override SpkObject Clone() => new SpkIteratorFunction(enumerable);
}

internal sealed class SpkNativeIteratorFunction : SpkNativeFunction
{
    public override string FunctionName => "Iterate";

    public SpkNativeIteratorFunction(int unitId, int funcId, FastList<SpkObject[]> captures)
        : base(null, unitId, funcId, captures, -1) { }

    internal override SpkFunction BindToInstance(ExecutionContext ctx, SpkObject arg) =>
        new SpkNativeIteratorFunction(UnitId, FunctionId, Captures) { Self = arg };
}
