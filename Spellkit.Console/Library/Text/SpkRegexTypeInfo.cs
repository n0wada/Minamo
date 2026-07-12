using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spellkit.Library.Text;

[SpkType]
public sealed partial class SpkRegexTypeInfo : SpkForeignTypeInfo
{
    public override string ReflectedTypeName => "Regex";

    public SpkRegexTypeInfo() => AddMixins(Spk.Equatable);

    #region Operations
    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format) =>
        new SpkString(((SpkRegex)arg).Regex.ToString());

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        left is SpkRegex a && right is SpkRegex b && a.Regex.ToString() == b.Regex.ToString() ? True : False;
    #endregion

    [SpkMethod]
    internal static string? Replace(ExecutionContext ctx, SpkRegex self, string input, string replacement)
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

    [SpkMethod]
    internal static SpkObject Split(ExecutionContext ctx, SpkRegex self, string input, int? count = null, int index = 0)
    {
        count ??= int.MaxValue;

        if (index < 0 || index >= input.Length)
        {
            return ctx.IndexOutOfRange(index);
        }

        try
        {
            var arr = self.Regex.Split(input, count.Value, index);
            var objs = new List<SpkObject>();

            for (var i = 0; i < arr.Length; i++)
            {
                if (!self.RemoveEmptyEntries || !string.IsNullOrEmpty(arr[i]))
                {
                    objs.Add(new SpkString(arr[i]));
                }
            }

            return new SpkTuple(objs.ToArray());
        }
        catch (RegexMatchTimeoutException)
        {
            return ctx.Timeout();
        }
    }

    [SpkMethod]
    internal static SpkObject Match(ExecutionContext ctx, SpkRegex self, string input, int index = 0, int? count = null)
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

    [SpkMethod]
    internal static SpkObject Matches(ExecutionContext ctx, SpkRegex self, string input, int index = 0)
    {
        if (index < 0 || index > input.Length)
        {
            return ctx.IndexOutOfRange();
        }

        try
        {
            var ms = self.Regex.Matches(input, index);
            var xs = new List<SpkTuple>();

            for (var i = 0; i < ms.Count; i++)
            {
                xs.Add(CreateMatch(ms[i]));
            }

            return new SpkArray(xs.ToArray());
        }
        catch (RegexMatchTimeoutException)
        {
            return ctx.Timeout();
        }
    }

    [SpkMethod]
    internal static bool IsMatch(ExecutionContext ctx, SpkRegex self, string input, int index = 0)
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

    private static SpkTuple CreateCapture(Capture capture) =>
        SpkTuple.Create
        (
            new ("index", SpkInteger.Get(capture.Index)),
            new ("length", SpkInteger.Get(capture.Length)),
            new ("value", SpkString.Get(capture.Value))
        );

    private static SpkTuple CreateMatch(Match match) =>
        SpkTuple.Create
        (
            new ("name", SpkString.Get(match.Name)),
            new ("success", match.Success ? True : False),
            new ("captures", new SpkArray(match.Captures.Select(CreateCapture).ToArray())),
            new ("index", SpkInteger.Get(match.Index)),
            new ("length", SpkInteger.Get(match.Length)),
            new ("value", SpkString.Get(match.Value))
        );


    [SpkStaticMethod("Regex")]
    internal static SpkObject New(ExecutionContext ctx, string pattern, bool ignoreCase = false, bool singleline = false, bool multiline = false, bool removeEmptyEntries = false)
    {
        return new SpkRegex(ctx.Type<SpkRegexTypeInfo>(), pattern, ignoreCase, singleline, multiline, removeEmptyEntries);
    }
}
