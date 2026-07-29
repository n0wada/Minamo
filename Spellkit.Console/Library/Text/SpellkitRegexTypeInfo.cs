using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spellkit.Library.Text;

[SpellkitType]
public sealed partial class SpellkitRegexTypeInfo : SpellkitForeignTypeInfo
{
    public override string ReflectedTypeName => "Regex";

    public SpellkitRegexTypeInfo() => AddMixins(SpellkitTypeCodes.Equatable);

    #region Operations
    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString(((SpellkitRegex)arg).Regex.ToString());

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        left is SpellkitRegex a && right is SpellkitRegex b && a.Regex.ToString() == b.Regex.ToString() ? True : False;
    #endregion

    [SpellkitMethod]
    internal static string? Replace(ExecutionContext ctx, SpellkitRegex self, string input, string replacement)
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

    [SpellkitMethod]
    internal static SpellkitObject Split(ExecutionContext ctx, SpellkitRegex self, string input, int? count = null, int index = 0)
    {
        count ??= int.MaxValue;

        if (index < 0 || index >= input.Length)
        {
            return ctx.IndexOutOfRange(index);
        }

        try
        {
            var arr = self.Regex.Split(input, count.Value, index);
            var objs = new List<SpellkitObject>();

            for (var i = 0; i < arr.Length; i++)
            {
                if (!self.RemoveEmptyEntries || !string.IsNullOrEmpty(arr[i]))
                {
                    objs.Add(new SpellkitString(arr[i]));
                }
            }

            return new SpellkitTuple(objs.ToArray());
        }
        catch (RegexMatchTimeoutException)
        {
            return ctx.Timeout();
        }
    }

    [SpellkitMethod]
    internal static SpellkitObject Match(ExecutionContext ctx, SpellkitRegex self, string input, int index = 0, int? count = null)
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

    [SpellkitMethod]
    internal static SpellkitObject Matches(ExecutionContext ctx, SpellkitRegex self, string input, int index = 0)
    {
        if (index < 0 || index > input.Length)
        {
            return ctx.IndexOutOfRange();
        }

        try
        {
            var ms = self.Regex.Matches(input, index);
            var xs = new List<SpellkitTuple>();

            for (var i = 0; i < ms.Count; i++)
            {
                xs.Add(CreateMatch(ms[i]));
            }

            return new SpellkitArray(xs.ToArray());
        }
        catch (RegexMatchTimeoutException)
        {
            return ctx.Timeout();
        }
    }

    [SpellkitMethod]
    internal static bool IsMatch(ExecutionContext ctx, SpellkitRegex self, string input, int index = 0)
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

    private static SpellkitTuple CreateCapture(Capture capture) =>
        SpellkitTuple.Create
        (
            new ("index", SpellkitInteger.Get(capture.Index)),
            new ("length", SpellkitInteger.Get(capture.Length)),
            new ("value", SpellkitString.Get(capture.Value))
        );

    private static SpellkitTuple CreateMatch(Match match) =>
        SpellkitTuple.Create
        (
            new ("name", SpellkitString.Get(match.Name)),
            new ("success", match.Success ? True : False),
            new ("captures", new SpellkitArray(match.Captures.Select(CreateCapture).ToArray())),
            new ("index", SpellkitInteger.Get(match.Index)),
            new ("length", SpellkitInteger.Get(match.Length)),
            new ("value", SpellkitString.Get(match.Value))
        );


    [SpellkitStaticMethod("Regex")]
    internal static SpellkitObject New(ExecutionContext ctx, string pattern, bool ignoreCase = false, bool singleline = false, bool multiline = false, bool removeEmptyEntries = false)
    {
        return new SpellkitRegex(ctx.Type<SpellkitRegexTypeInfo>(), pattern, ignoreCase, singleline, multiline, removeEmptyEntries);
    }
}
