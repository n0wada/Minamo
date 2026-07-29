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
