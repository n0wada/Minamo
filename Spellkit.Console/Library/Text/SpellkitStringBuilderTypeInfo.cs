using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Linq;
using System.Text;

namespace Spellkit.Library.Text;

[SpellkitType]
public sealed partial class SpellkitStringBuilderTypeInfo : SpellkitForeignTypeInfo
{
    private const string StringBuilder = nameof(StringBuilder);

    public override string ReflectedTypeName => StringBuilder;

    public SpellkitStringBuilderTypeInfo() => AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Equatable);

    #region Operations
    public SpellkitStringBuilder Create(StringBuilder sb) => new(this, sb);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString(((SpellkitStringBuilder)arg).ToString());

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject index)
    {
        if (index is not SpellkitInteger spki)
        {
            return ctx.InvalidType(index);
        }

        var i = (int)spki.Value;
        var self = (SpellkitStringBuilder)arg;
        i = i < 0 ? i + self.Builder.Length: i;

        if (i < 0 || i >= self.Builder.Length)
        {
            return ctx.IndexOutOfRange(index);
        }

        return new SpellkitChar(self.Builder[i]);
    }

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitStringBuilder)arg).Builder.Length);

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        var a = ((SpellkitStringBuilder)left).Builder;
        var b = ((SpellkitStringBuilder)right).Builder;
        return a.ToString() == b.ToString() ? True : False;
    }
    #endregion

    [SpellkitMethod]
    internal static SpellkitObject Append(ExecutionContext ctx, SpellkitStringBuilder self, SpellkitObject value)
    {
        var str = value.ToString(ctx).Value;

        if (ctx.HasErrors)
        {
            return Nil;
        }

        self.Builder.Append(str);
        return self;
    }

    [SpellkitMethod]
    internal static SpellkitObject AppendLine(ExecutionContext ctx, SpellkitStringBuilder self, [Default("")]SpellkitObject value)
    {
        var str = value.ToString(ctx).Value;

        if (ctx.HasErrors)
        {
            return Nil;
        }

        self.Builder.AppendLine(str);
        return self;
    }

    [SpellkitMethod]
    internal static SpellkitObject Replace(ExecutionContext ctx, SpellkitStringBuilder self, SpellkitObject value, SpellkitObject other)
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

    [SpellkitMethod]
    internal static SpellkitObject Remove(ExecutionContext ctx, SpellkitStringBuilder self, int index, int count)
    {
        if (index + count >= self.Builder.Length)
        {
            return ctx.IndexOutOfRange();
        }

        self.Builder.Remove(index, count);
        return self;
    }

    [SpellkitMethod]
    internal static SpellkitObject Insert(ExecutionContext ctx, SpellkitStringBuilder self, int index, SpellkitObject value)
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

    [SpellkitStaticMethod(StringBuilder)]
    internal static SpellkitObject New(ExecutionContext ctx, [VarArg]SpellkitTuple values)
    {
        if (values.Count > 0)
        {
            var vals = SpellkitIterator.ToEnumerable(ctx, values);
            var arr = vals.Select(o => o.ToString(ctx).Value).ToArray();
            var sb = new StringBuilder(string.Join("", arr));
            return new SpellkitStringBuilder(ctx.Type<SpellkitStringBuilderTypeInfo>(), sb);
        }
        else
        {
            return new SpellkitStringBuilder(ctx.Type<SpellkitStringBuilderTypeInfo>(), new StringBuilder());
        }
    }
}
