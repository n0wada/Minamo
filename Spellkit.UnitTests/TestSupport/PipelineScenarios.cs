using Spellkit.Compiler;
using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Reflection;

namespace Spellkit.UnitTesting;

internal static class PipelineScenarios
{
    internal static void ParserDiagnostics()
    {
        var result = SpellkitParser.Parse(SourceBuffer.FromString("let =", "<parser-contract>"));
        Assert(!result.Success, "invalid syntax is rejected");
        Assert(result.Messages.Any(message => message.Type == BuildMessageType.Error),
            "parser reports an error diagnostic");
    }

    internal static void CompilerAndRuntime()
    {
        const string source = """
            let choose = value => {
                if value > 0 { value + 1 } else { 0 }
            }
            choose(41)
            """;
        var parsed = SpellkitParser.Parse(SourceBuffer.FromString(source, "<pipeline-contract>"));
        Assert(parsed.Success && parsed.Value is not null, "parser accepts pipeline source");

        var options = BuilderOptions.Default();
        var linker = new SpellkitLinker(FileLookup.Restricted(options).Build());
        var compiled = linker.Make(parsed.Value!);
        Assert(compiled.Success && compiled.Value is not null, "lowering and compilation succeed");

        var context = SpellkitMachine.CreateExecutionContext(compiled.Value!);
        var executed = SpellkitMachine.Execute(context);
        Assert(Convert.ToInt64(executed.Value?.ToObject()) == 42, "VM returns expected value");
    }

    internal static void CollectionMutationContracts()
    {
        var set = new SpellkitSet(SpellkitInteger.One, SpellkitInteger.Two);
        using var iterator = set.GetEnumerator();
        Assert(iterator.MoveNext(), "set iterator starts");
        Assert(!set.Add(SpellkitInteger.One), "duplicate set item is not added");
        Assert(iterator.MoveNext(), "duplicate set add does not invalidate iterator");

        var ordered = new SpellkitSet(SpellkitInteger.One, SpellkitInteger.Two, SpellkitInteger.Three);
        ordered.Remove(SpellkitInteger.Two);
        ordered.Add(SpellkitInteger.Two);
        Assert(
            ordered.ToArray(null!).ToArray().SequenceEqual(
                new SpellkitObject[] { SpellkitInteger.One, SpellkitInteger.Three, SpellkitInteger.Two }),
            "set removal and re-addition appends to the iteration order");
        Assert(
            ordered.GetHashCode() == new SpellkitSet(
                SpellkitInteger.Two, SpellkitInteger.One, SpellkitInteger.Three).GetHashCode(),
            "equal sets have the same hash code regardless of insertion order");

        var tuple = SpellkitTuple.Create(
            new("first", SpellkitInteger.One),
            new("second", SpellkitInteger.Two));
        Assert(
            tuple.ToSpellkitDictionary().Select(item => ((SpellkitTuple)item)[0].ToString())
                .SequenceEqual(new[] { "first", "second" }),
            "tuple dictionary conversion preserves label order");

        var clrDictionary = new Dictionary<string, int>
        {
            ["first"] = 1,
            ["second"] = 2
        };
        var convertedDictionary = (SpellkitDictionary)TypeConverter.ConvertFrom(clrDictionary);
        Assert(
            convertedDictionary.Select(item => ((SpellkitTuple)item)[0].ToString())
                .SequenceEqual(new[] { "first", "second" }),
            "CLR dictionary conversion preserves source enumeration order");

        Assert(
            !TypeConverter.TryConvert(new SpellkitInteger(4_294_967_296), typeof(int), out _),
            "generic conversion rejects integer overflow");
        Assert(
            !TypeConverter.TryConvert(SpellkitInteger.MinusOne, typeof(uint), out _),
            "generic conversion rejects negative unsigned value");
    }

    internal static void InteropMethodMatching()
    {
        var parameters = typeof(PipelineScenarios)
            .GetMethod(nameof(TwoParameters), BindingFlags.NonPublic | BindingFlags.Static)!
            .GetParameters();

        Assert(
            SpellkitInteropTypeInfo.ParametersMatch(
                parameters,
                new SpellkitObject[] { new SpellkitInterop(typeof(string)), new SpellkitInterop(typeof(int)) }),
            "interop method matching accepts compatible parameter types");
        Assert(
            !SpellkitInteropTypeInfo.ParametersMatch(
                parameters,
                new SpellkitObject[] { new SpellkitInterop(typeof(string)), new SpellkitInterop(typeof(string)) }),
            "interop method matching rejects a partial type match");
    }

    private static void TwoParameters(string text, int number) { }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Pipeline contract failed: {name}.");
        }
    }
}
