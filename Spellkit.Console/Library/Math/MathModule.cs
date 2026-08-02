using Spellkit.Hosting;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Mathematics;

[SpellkitModule("math")]
public static class MathModule
{
    [SpellkitProperty("e")]
    internal static double E => System.Math.E;

    [SpellkitProperty("pi")]
    internal static double Pi => System.Math.PI;

    [SpellkitProperty("tau")]
    internal static double Tau => System.Math.Tau;

    [SpellkitCommand("sqrt")]
    internal static double Sqrt(double value) => System.Math.Sqrt(value);

    [SpellkitCommand("pow")]
    internal static double Pow(double value, double power) => System.Math.Pow(value, power);

    [SpellkitCommand("min")]
    internal static SpellkitObject Min(SpellkitCommandContext host, SpellkitObject left, SpellkitObject right) =>
        left.Lesser(right, host.ExecutionContext) ? left : right;

    [SpellkitCommand("max")]
    internal static SpellkitObject Max(SpellkitCommandContext host, SpellkitObject left, SpellkitObject right) =>
        left.Greater(right, host.ExecutionContext) ? left : right;

    [SpellkitCommand("abs")]
    internal static SpellkitObject Abs(SpellkitCommandContext host, SpellkitObject value) =>
        value.Lesser(SpellkitInteger.Zero, host.ExecutionContext) ? value.Negate(host.ExecutionContext) : value;

    [SpellkitCommand("round")]
    internal static double Round(double value, int digits = 2) => System.Math.Round(value, digits);

    [SpellkitCommand("floor")]
    internal static double Floor(double value) => System.Math.Floor(value);

    [SpellkitCommand("ceiling")]
    internal static double Ceiling(double value) => System.Math.Ceiling(value);

    [SpellkitCommand("truncate")]
    internal static double Truncate(double value) => System.Math.Truncate(value);

    [SpellkitCommand("clamp")]
    internal static SpellkitObject Clamp(SpellkitCommandContext host, double value, double min, double max) =>
        min > max
            ? host.ExecutionContext.InvalidValue(min, max)
            : new SpellkitFloat(System.Math.Clamp(value, min, max));

    [SpellkitCommand("exp")]
    internal static double Exp(double value) => System.Math.Exp(value);

    [SpellkitCommand("log")]
    internal static double Log(double value, double? @base = null) =>
        @base is null ? System.Math.Log(value) : System.Math.Log(value, @base.Value);

    [SpellkitCommand("log10")]
    internal static double Log10(double value) => System.Math.Log10(value);

    [SpellkitCommand("sin")]
    internal static double Sin(double value) => System.Math.Sin(value);

    [SpellkitCommand("cos")]
    internal static double Cos(double value) => System.Math.Cos(value);

    [SpellkitCommand("tan")]
    internal static double Tan(double value) => System.Math.Tan(value);

    [SpellkitCommand("asin")]
    internal static double Asin(double value) => System.Math.Asin(value);

    [SpellkitCommand("acos")]
    internal static double Acos(double value) => System.Math.Acos(value);

    [SpellkitCommand("atan")]
    internal static double Atan(double value) => System.Math.Atan(value);

    [SpellkitCommand("atan2")]
    internal static double Atan2(double y, double x) => System.Math.Atan2(y, x);

    [SpellkitCommand("degreesToRadians")]
    internal static double DegreesToRadians(double value) => value * (System.Math.PI / 180d);

    [SpellkitCommand("radiansToDegrees")]
    internal static double RadiansToDegrees(double value) => value * (180d / System.Math.PI);

    [SpellkitCommand("isFinite")]
    internal static bool IsFinite(double value) => double.IsFinite(value);

    [SpellkitCommand("sign")]
    internal static SpellkitObject Sign(SpellkitCommandContext host, SpellkitObject value)
    {
        if (ReferenceEquals(value, SpellkitInteger.Zero))
        {
            return SpellkitInteger.Zero;
        }

        return value.Lesser(SpellkitInteger.Zero, host.ExecutionContext)
            ? SpellkitInteger.MinusOne
            : SpellkitInteger.One;
    }
}
