using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Linq;
using System.Text;

namespace Spellkit.Library.Text;

[SpkType]
public sealed partial class SpkStringBuilderTypeInfo : SpkForeignTypeInfo
{
    private const string StringBuilder = nameof(StringBuilder);

    public override string ReflectedTypeName => StringBuilder;

    public SpkStringBuilderTypeInfo() => AddMixins(Spk.Lookup, Spk.Equatable);

    #region Operations
    public SpkStringBuilder Create(StringBuilder sb) => new(this, sb);

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format) =>
        new SpkString(((SpkStringBuilder)arg).ToString());

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject arg, SpkObject index)
    {
        if (index is not SpkInteger spki)
        {
            return ctx.InvalidType(index);
        }

        var i = (int)spki.Value;
        var self = (SpkStringBuilder)arg;
        i = i < 0 ? i + self.Builder.Length: i;

        if (i < 0 || i >= self.Builder.Length)
        {
            return ctx.IndexOutOfRange(index);
        }

        return new SpkChar(self.Builder[i]);
    }

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg) =>
        SpkInteger.Get(((SpkStringBuilder)arg).Builder.Length);

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        var a = ((SpkStringBuilder)left).Builder;
        var b = ((SpkStringBuilder)right).Builder;
        return a.ToString() == b.ToString() ? True : False;
    }
    #endregion

    [SpkMethod]
    internal static SpkObject Append(ExecutionContext ctx, SpkStringBuilder self, SpkObject value)
    {
        var str = value.ToString(ctx).Value;

        if (ctx.HasErrors)
        {
            return Nil;
        }

        self.Builder.Append(str);
        return self;
    }

    [SpkMethod]
    internal static SpkObject AppendLine(ExecutionContext ctx, SpkStringBuilder self, [Default("")]SpkObject value)
    {
        var str = value.ToString(ctx).Value;

        if (ctx.HasErrors)
        {
            return Nil;
        }

        self.Builder.AppendLine(str);
        return self;
    }

    [SpkMethod]
    internal static SpkObject Replace(ExecutionContext ctx, SpkStringBuilder self, SpkObject value, SpkObject other)
    {
        var a = value.ToString(ctx).Value;
        var b = other.ToString(ctx).Value;

        if (ctx.HasErrors)
        {
            return Nil;
        }

        self.Builder.Replace(a, b);
        return self;
    }

    [SpkMethod]
    internal static SpkObject Remove(ExecutionContext ctx, SpkStringBuilder self, int index, int count)
    {
        if (index + count >= self.Builder.Length)
        {
            return ctx.IndexOutOfRange();
        }

        self.Builder.Remove(index, count);
        return self;
    }

    [SpkMethod]
    internal static SpkObject Insert(ExecutionContext ctx, SpkStringBuilder self, int index, SpkObject value)
    {
        var str = value.ToString(ctx).Value;

        if (ctx.HasErrors)
        {
            return Nil;
        }

        if (index < 0 || index >= self.Builder.Length)
        {
            return ctx.IndexOutOfRange();
        }

        self.Builder.Insert(index, str);
        return self;
    }

    [SpkStaticMethod(StringBuilder)]
    internal static SpkObject New(ExecutionContext ctx, [VarArg]SpkTuple values)
    {
        if (values.Count > 0)
        {
            var vals = SpkIterator.ToEnumerable(ctx, values);
            var arr = vals.Select(o => o.ToString(ctx).Value).ToArray();
            var sb = new StringBuilder(string.Join("", arr));
            return new SpkStringBuilder(ctx.Type<SpkStringBuilderTypeInfo>(), sb);
        }
        else
        {
            return new SpkStringBuilder(ctx.Type<SpkStringBuilderTypeInfo>(), new StringBuilder());
        }
    }
}
