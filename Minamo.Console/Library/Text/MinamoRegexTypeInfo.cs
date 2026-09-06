using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Minamo.Library.Text;

[MinamoType]
public sealed partial class MinamoRegexTypeInfo : MinamoForeignTypeInfo
{
    public override string ReflectedTypeName => "Regex";

    public MinamoRegexTypeInfo() => AddMixins(MinamoTypeCodes.Equatable);

    #region Operations
    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString(((MinamoRegex)arg).Regex.ToString());

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        left is MinamoRegex a && right is MinamoRegex b && a.Regex.ToString() == b.Regex.ToString() ? True : False;
    #endregion

    [MinamoMethod]
    internal static string? Replace(ExecutionContext ctx, MinamoRegex self, string input, string replacement)
    {
        try
        {
            return self.Regex.Replace(input, replacement);
        }
        catch (RegexMatchTimeoutException)
        {
            ctx.Timeout();
            return default;
        }
    }

    [MinamoMethod]
    internal static MinamoObject Split(ExecutionContext ctx, MinamoRegex self, string input, int? count = null, int index = 0)
    {
        count ??= int.MaxValue;

        if (index < 0 || index >= input.Length)
        {
            return ctx.IndexOutOfRange(index);
        }

        try
        {
            var arr = self.Regex.Split(input, count.Value, index);
            var objs = new List<MinamoObject>();

            for (var i = 0; i < arr.Length; i++)
            {
                if (!self.RemoveEmptyEntries || !string.IsNullOrEmpty(arr[i]))
                {
                    objs.Add(new MinamoString(arr[i]));
                }
            }

            return new MinamoTuple(objs.ToArray());
        }
        catch (RegexMatchTimeoutException)
        {
            return ctx.Timeout();
        }
    }

    [MinamoMethod]
    internal static MinamoObject Match(ExecutionContext ctx, MinamoRegex self, string input, int index = 0, int? count = null)
    {
        count ??= input.Length;

        if (index + count > input.Length)
        {
            return ctx.IndexOutOfRange();
        }

        try
        {
            var m = self.Regex.Match(input, index, count.Value);
            return CreateMatch(m);
        }
        catch (RegexMatchTimeoutException)
        {
            return ctx.Timeout();
        }
    }

    [MinamoMethod]
    internal static MinamoObject Matches(ExecutionContext ctx, MinamoRegex self, string input, int index = 0)
    {
        if (index < 0 || index > input.Length)
        {
            return ctx.IndexOutOfRange();
        }

        try
        {
            var ms = self.Regex.Matches(input, index);
            var xs = new List<MinamoTuple>();

            for (var i = 0; i < ms.Count; i++)
            {
                xs.Add(CreateMatch(ms[i]));
            }

            return new MinamoArray(xs.ToArray());
        }
        catch (RegexMatchTimeoutException)
        {
            return ctx.Timeout();
        }
    }

    [MinamoMethod]
    internal static bool IsMatch(ExecutionContext ctx, MinamoRegex self, string input, int index = 0)
    {
        if (index < 0 || index >= input.Length)
        {
            ctx.IndexOutOfRange(index);
            return default;
        }

        try
        {
            return self.Regex.IsMatch(input, index);
        }
        catch (RegexMatchTimeoutException) 
        {
            ctx.Timeout();
            return default;
        }
    }

    private static MinamoTuple CreateCapture(Capture capture) =>
        MinamoTuple.Create
        (
            new ("index", MinamoInteger.Get(capture.Index)),
            new ("length", MinamoInteger.Get(capture.Length)),
            new ("value", MinamoString.Get(capture.Value))
        );

    private static MinamoTuple CreateMatch(Match match) =>
        MinamoTuple.Create
        (
            new ("name", MinamoString.Get(match.Name)),
            new ("success", match.Success ? True : False),
            new ("captures", new MinamoArray(match.Captures.Select(CreateCapture).ToArray())),
            new ("index", MinamoInteger.Get(match.Index)),
            new ("length", MinamoInteger.Get(match.Length)),
            new ("value", MinamoString.Get(match.Value))
        );


    [MinamoStaticMethod("Regex")]
    internal static MinamoObject New(ExecutionContext ctx, string pattern, bool ignoreCase = false, bool singleline = false, bool multiline = false, bool removeEmptyEntries = false)
    {
        return new MinamoRegex(ctx.Type<MinamoRegexTypeInfo>(), pattern, ignoreCase, singleline, multiline, removeEmptyEntries);
    }
}
