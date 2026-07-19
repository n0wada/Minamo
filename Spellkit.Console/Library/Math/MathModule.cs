using Spellkit.Hosting;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Mathematics;

[SpellkitModule("math")]
public static class MathModule
{
    [SpellkitCommand("sqrt")]
    internal static double Sqrt(double value) => System.Math.Sqrt(value);

    [SpellkitCommand("pow")]
    internal static double Pow(double value, double power) => System.Math.Pow(value, power);

    [SpellkitCommand("min")]
    internal static SpkObject Min(SpellkitCommandContext host, SpkObject left, SpkObject right) =>
        left.Lesser(right, host.ExecutionContext) ? left : right;

    [SpellkitCommand("max")]
    internal static SpkObject Max(SpellkitCommandContext host, SpkObject left, SpkObject right) =>
        left.Greater(right, host.ExecutionContext) ? left : right;

    [SpellkitCommand("abs")]
    internal static SpkObject Abs(SpellkitCommandContext host, SpkObject value) =>
        value.Lesser(SpkInteger.Zero, host.ExecutionContext) ? value.Negate(host.ExecutionContext) : value;

    [SpellkitCommand("round")]
    internal static double Round(double value, int digits = 2) => System.Math.Round(value, digits);

    [SpellkitCommand("sign")]
    internal static SpkObject Sign(SpellkitCommandContext host, SpkObject value)
    {
        if (ReferenceEquals(value, SpkInteger.Zero))
        {
            return SpkInteger.Zero;
        }

        return value.Lesser(SpkInteger.Zero, host.ExecutionContext)
            ? SpkInteger.MinusOne
            : SpkInteger.One;
    }
}
