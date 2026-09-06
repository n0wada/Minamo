using Minamo.Hosting;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Library.Mathematics;

[MinamoModule("math")]
public static class MathModule
{
    [MinamoProperty("e")]
    internal static double E => System.Math.E;

    [MinamoProperty("pi")]
    internal static double Pi => System.Math.PI;

    [MinamoProperty("tau")]
    internal static double Tau => System.Math.Tau;

    [MinamoCommand("sqrt")]
    internal static double Sqrt(double value) => System.Math.Sqrt(value);

    [MinamoCommand("pow")]
    internal static double Pow(double value, double power) => System.Math.Pow(value, power);

    [MinamoCommand("min")]
    internal static MinamoObject Min(MinamoCommandContext host, MinamoObject left, MinamoObject right) =>
        left.Lesser(right, host.ExecutionContext) ? left : right;

    [MinamoCommand("max")]
    internal static MinamoObject Max(MinamoCommandContext host, MinamoObject left, MinamoObject right) =>
        left.Greater(right, host.ExecutionContext) ? left : right;

    [MinamoCommand("abs")]
    internal static MinamoObject Abs(MinamoCommandContext host, MinamoObject value) =>
        value.Lesser(MinamoInteger.Zero, host.ExecutionContext) ? value.Negate(host.ExecutionContext) : value;

    [MinamoCommand("round")]
    internal static double Round(double value, int digits = 2) => System.Math.Round(value, digits);

    [MinamoCommand("floor")]
    internal static double Floor(double value) => System.Math.Floor(value);

    [MinamoCommand("ceiling")]
    internal static double Ceiling(double value) => System.Math.Ceiling(value);

    [MinamoCommand("truncate")]
    internal static double Truncate(double value) => System.Math.Truncate(value);

    [MinamoCommand("clamp")]
    internal static MinamoObject Clamp(MinamoCommandContext host, double value, double min, double max) =>
        min > max
            ? host.ExecutionContext.InvalidValue(min, max)
            : new MinamoFloat(System.Math.Clamp(value, min, max));

    [MinamoCommand("exp")]
    internal static double Exp(double value) => System.Math.Exp(value);

    [MinamoCommand("log")]
    internal static double Log(double value, double? @base = null) =>
        @base is null ? System.Math.Log(value) : System.Math.Log(value, @base.Value);

    [MinamoCommand("log10")]
    internal static double Log10(double value) => System.Math.Log10(value);

    [MinamoCommand("sin")]
    internal static double Sin(double value) => System.Math.Sin(value);

    [MinamoCommand("cos")]
    internal static double Cos(double value) => System.Math.Cos(value);

    [MinamoCommand("tan")]
    internal static double Tan(double value) => System.Math.Tan(value);

    [MinamoCommand("asin")]
    internal static double Asin(double value) => System.Math.Asin(value);

    [MinamoCommand("acos")]
    internal static double Acos(double value) => System.Math.Acos(value);

    [MinamoCommand("atan")]
    internal static double Atan(double value) => System.Math.Atan(value);

    [MinamoCommand("atan2")]
    internal static double Atan2(double y, double x) => System.Math.Atan2(y, x);

    [MinamoCommand("degreesToRadians")]
    internal static double DegreesToRadians(double value) => value * (System.Math.PI / 180d);

    [MinamoCommand("radiansToDegrees")]
    internal static double RadiansToDegrees(double value) => value * (180d / System.Math.PI);

    [MinamoCommand("isFinite")]
    internal static bool IsFinite(double value) => double.IsFinite(value);

    [MinamoCommand("sign")]
    internal static MinamoObject Sign(MinamoCommandContext host, MinamoObject value)
    {
        if (ReferenceEquals(value, MinamoInteger.Zero))
        {
            return MinamoInteger.Zero;
        }

        return value.Lesser(MinamoInteger.Zero, host.ExecutionContext)
            ? MinamoInteger.MinusOne
            : MinamoInteger.One;
    }
}
