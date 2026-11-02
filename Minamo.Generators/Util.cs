using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Minamo.Generators;

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
            return $"new {Types.MinamoString}({Escape(str)})";
        }
        else if (obj is bool b)
        {
            return b ? $"{Types.MinamoBool}.True" : $"{Types.MinamoBool}.False";
        }
        else if (obj is char c)
        {
            return $"new {Types.MinamoChar}({Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(c, true)})";
        }
        else if (obj is double d)
        {
            return $"new {Types.MinamoFloat}({FloatingLiteral(d)})";
        }
        else if (obj is float f)
        {
            return $"new {Types.MinamoFloat}({FloatingLiteral(f)})";
        }
        else if (obj is long integer)
        {
            return $"{Types.MinamoInteger}.Get({integer.ToString(CultureInfo.InvariantCulture)}L)";
        }
        else if (ReferenceEquals(obj, MinamoTypeGenerator.Nil))
        {
            return Types.MinamoNil + ".Instance";
        }
        else
        {
            return obj?.ToString();
        }
    }

    internal static string FloatingLiteral(double value) =>
        double.IsNaN(value) ? "global::System.Double.NaN"
        : double.IsPositiveInfinity(value) ? "global::System.Double.PositiveInfinity"
        : double.IsNegativeInfinity(value) ? "global::System.Double.NegativeInfinity"
        : value.ToString("R", CultureInfo.InvariantCulture) + "D";

    internal static string FloatingLiteral(float value) =>
        float.IsNaN(value) ? "global::System.Single.NaN"
        : float.IsPositiveInfinity(value) ? "global::System.Single.PositiveInfinity"
        : float.IsNegativeInfinity(value) ? "global::System.Single.NegativeInfinity"
        : value.ToString("R", CultureInfo.InvariantCulture) + "F";

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
