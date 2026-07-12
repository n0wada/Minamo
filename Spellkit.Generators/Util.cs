using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Spellkit.Generators;

internal static class StringUtil
{
    private static readonly Dictionary<string, string> replaceDict =
        new()
        {
            { "\a", @"\a" },
            { "\b", @"\b" },
            { "\f", @"\f" },
            { "\n", @"\n" },
            { "\r", @"\r" },
            { "\t", @"\t" },
            { "\v", @"\v" },
            { "\\", @"\\" },
            { "\0", @"\0" },
            { "\"", @"\""" }
        };

    private const string regexEscapes = @"[\a\b\f\n\r\t\v\\""]";

    private static string Escape(string value, string quote = "\"") =>
        quote + Regex.Replace(value, regexEscapes, Match) + quote;

    public static string ToLiteral(this object obj)
    {
        if (obj is string str)
        {
            return $"new {Types.SpkString}({Escape(str)})";
        }
        else if (obj is bool b)
        {
            return b ? $"{Types.SpkBool}.True" : $"{Types.SpkBool}.False";
        }
        else if (obj is char c)
        {
            return $"new {Types.SpkChar}({Escape(c.ToString(), "'")})";
        }
        else if (obj is double d)
        {
            return $"new {Types.SpkFloat}({d.ToString(CultureInfo.InvariantCulture)})";
        }
        else if (obj is float f)
        {
            return $"new {Types.SpkFloat}({f.ToString(CultureInfo.InvariantCulture)})";
        }
        else if (ReferenceEquals(obj, SpkTypeGenerator.Nil))
        {
            return Types.SpkNil + ".Instance";
        }
        else
        {
            return obj?.ToString();
        }
    }

    private static string Match(Match m)
    {
        var match = m.ToString();

        if (replaceDict.ContainsKey(match))
        {
            return replaceDict[match];
        }

        return string.Empty;
    }
}
