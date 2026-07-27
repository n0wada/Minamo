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
        var result = SpkParser.Parse(SourceBuffer.FromString("let =", "<parser-contract>"));
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
        var parsed = SpkParser.Parse(SourceBuffer.FromString(source, "<pipeline-contract>"));
        Assert(parsed.Success && parsed.Value is not null, "parser accepts pipeline source");

        var options = BuilderOptions.Default();
        var linker = new SpkLinker(FileLookup.Restricted(options).Build());
        var compiled = linker.Make(parsed.Value!);
        Assert(compiled.Success && compiled.Value is not null, "lowering and compilation succeed");

        var context = SpkMachine.CreateExecutionContext(compiled.Value!);
        var executed = SpkMachine.Execute(context);
        Assert(Convert.ToInt64(executed.Value?.ToObject()) == 42, "VM returns expected value");
    }

    internal static void CollectionMutationContracts()
    {
        var set = new SpkSet(SpkInteger.One, SpkInteger.Two);
        using var iterator = set.GetEnumerator();
        Assert(iterator.MoveNext(), "set iterator starts");
        Assert(!set.Add(SpkInteger.One), "duplicate set item is not added");
        Assert(iterator.MoveNext(), "duplicate set add does not invalidate iterator");

        var ordered = new SpkSet(SpkInteger.One, SpkInteger.Two, SpkInteger.Three);
        ordered.Remove(SpkInteger.Two);
        ordered.Add(SpkInteger.Two);
        Assert(
            ordered.ToArray(null!).ToArray().SequenceEqual(
                new SpkObject[] { SpkInteger.One, SpkInteger.Three, SpkInteger.Two }),
            "set removal and re-addition appends to the iteration order");
        Assert(
            ordered.GetHashCode() == new SpkSet(
                SpkInteger.Two, SpkInteger.One, SpkInteger.Three).GetHashCode(),
            "equal sets have the same hash code regardless of insertion order");

        var tuple = SpkTuple.Create(
            new("first", SpkInteger.One),
            new("second", SpkInteger.Two));
        Assert(
            tuple.ToSpkDictionary().Select(item => ((SpkTuple)item)[0].ToString())
                .SequenceEqual(new[] { "first", "second" }),
            "tuple dictionary conversion preserves label order");

        var clrDictionary = new Dictionary<string, int>
        {
            ["first"] = 1,
            ["second"] = 2
        };
        var convertedDictionary = (SpkDictionary)TypeConverter.ConvertFrom(clrDictionary);
        Assert(
            convertedDictionary.Select(item => ((SpkTuple)item)[0].ToString())
                .SequenceEqual(new[] { "first", "second" }),
            "CLR dictionary conversion preserves source enumeration order");

        Assert(
            !TypeConverter.TryConvert(new SpkInteger(4_294_967_296), typeof(int), out _),
            "generic conversion rejects integer overflow");
        Assert(
            !TypeConverter.TryConvert(SpkInteger.MinusOne, typeof(uint), out _),
            "generic conversion rejects negative unsigned value");
    }

    internal static void InteropMethodMatching()
    {
        var parameters = typeof(PipelineScenarios)
            .GetMethod(nameof(TwoParameters), BindingFlags.NonPublic | BindingFlags.Static)!
            .GetParameters();

        Assert(
            SpkInteropTypeInfo.ParametersMatch(
                parameters,
                new SpkObject[] { new SpkInterop(typeof(string)), new SpkInterop(typeof(int)) }),
            "interop method matching accepts compatible parameter types");
        Assert(
            !SpkInteropTypeInfo.ParametersMatch(
                parameters,
                new SpkObject[] { new SpkInterop(typeof(string)), new SpkInterop(typeof(string)) }),
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
