using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Linq;
using System.Text;

namespace Minamo.Library.Text;

[MinamoType]
public sealed partial class MinamoStringBuilderTypeInfo : MinamoForeignTypeInfo
{
    private const string StringBuilder = nameof(StringBuilder);

    public override string ReflectedTypeName => StringBuilder;

    public MinamoStringBuilderTypeInfo() => AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Equatable);

    #region Operations
    public MinamoStringBuilder Create(StringBuilder sb) => new(this, sb);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString(((MinamoStringBuilder)arg).ToString());

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject arg, MinamoObject index)
    {
        if (index is not MinamoInteger spki)
        {
            return ctx.InvalidType(index);
        }

        var i = (int)spki.Value;
        var self = (MinamoStringBuilder)arg;
        i = i < 0 ? i + self.Builder.Length: i;

        if (i < 0 || i >= self.Builder.Length)
        {
            return ctx.IndexOutOfRange(index);
        }

        return new MinamoChar(self.Builder[i]);
    }

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        MinamoInteger.Get(((MinamoStringBuilder)arg).Builder.Length);

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        var a = ((MinamoStringBuilder)left).Builder;
        var b = ((MinamoStringBuilder)right).Builder;
        return a.ToString() == b.ToString() ? True : False;
    }
    #endregion

    [MinamoMethod]
    internal static MinamoObject Append(ExecutionContext ctx, MinamoStringBuilder self, MinamoObject value)
    {
        var str = value.ToString(ctx).Value;

        if (ctx.HasErrors)
        {
            return Nil;
        }

        self.Builder.Append(str);
        return self;
    }

    [MinamoMethod]
    internal static MinamoObject AppendLine(ExecutionContext ctx, MinamoStringBuilder self, [Default("")]MinamoObject value)
    {
        var str = value.ToString(ctx).Value;

        if (ctx.HasErrors)
        {
            return Nil;
        }

        self.Builder.AppendLine(str);
        return self;
    }

    [MinamoMethod]
    internal static MinamoObject Replace(ExecutionContext ctx, MinamoStringBuilder self, MinamoObject value, MinamoObject other)
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

    [MinamoMethod]
    internal static MinamoObject Remove(ExecutionContext ctx, MinamoStringBuilder self, int index, int count)
    {
        if (index + count >= self.Builder.Length)
        {
            return ctx.IndexOutOfRange();
        }

        self.Builder.Remove(index, count);
        return self;
    }

    [MinamoMethod]
    internal static MinamoObject Insert(ExecutionContext ctx, MinamoStringBuilder self, int index, MinamoObject value)
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

    [MinamoStaticMethod(StringBuilder)]
    internal static MinamoObject New(ExecutionContext ctx, [VarArg]MinamoTuple values)
    {
        if (values.Count > 0)
        {
            var vals = MinamoIterator.ToEnumerable(ctx, values);
            var arr = vals.Select(o => o.ToString(ctx).Value).ToArray();
            var sb = new StringBuilder(string.Join("", arr));
            return new MinamoStringBuilder(ctx.Type<MinamoStringBuilderTypeInfo>(), sb);
        }
        else
        {
            return new MinamoStringBuilder(ctx.Type<MinamoStringBuilderTypeInfo>(), new StringBuilder());
        }
    }
}
