using Minamo.Compiler;
using Minamo.Linker;
using Minamo.Parser;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Reflection;

namespace Minamo.UnitTesting;

internal static class PipelineScenarios
{
    internal static void ParserDiagnostics()
    {
        var result = MinamoParser.Parse(SourceBuffer.FromString("let =", "<parser-contract>"));
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
        var parsed = MinamoParser.Parse(SourceBuffer.FromString(source, "<pipeline-contract>"));
        Assert(parsed.Success && parsed.Value is not null, "parser accepts pipeline source");

        var options = BuilderOptions.Default();
        var linker = new MinamoLinker(FileLookup.Restricted(options).Build());
        var compiled = linker.Make(parsed.Value!);
        Assert(compiled.Success && compiled.Value is not null, "lowering and compilation succeed");

        var context = MinamoMachine.CreateExecutionContext(compiled.Value!);
        var executed = MinamoMachine.Execute(context);
        Assert(Convert.ToInt64(executed.Value?.ToObject()) == 42, "VM returns expected value");
    }

    internal static void CollectionMutationContracts()
    {
        var set = new MinamoSet(MinamoInteger.One, MinamoInteger.Two);
        using var iterator = set.GetEnumerator();
        Assert(iterator.MoveNext(), "set iterator starts");
        Assert(!set.Add(MinamoInteger.One), "duplicate set item is not added");
        Assert(iterator.MoveNext(), "duplicate set add does not invalidate iterator");

        var ordered = new MinamoSet(MinamoInteger.One, MinamoInteger.Two, MinamoInteger.Three);
        ordered.Remove(MinamoInteger.Two);
        ordered.Add(MinamoInteger.Two);
        Assert(
            ordered.ToArray(null!).ToArray().SequenceEqual(
                new MinamoObject[] { MinamoInteger.One, MinamoInteger.Three, MinamoInteger.Two }),
            "set removal and re-addition appends to the iteration order");
        Assert(
            ordered.GetHashCode() == new MinamoSet(
                MinamoInteger.Two, MinamoInteger.One, MinamoInteger.Three).GetHashCode(),
            "equal sets have the same hash code regardless of insertion order");

        var tuple = MinamoTuple.Create(
            new("first", MinamoInteger.One),
            new("second", MinamoInteger.Two));
        Assert(
            tuple.ToMinamoDictionary().Select(item => ((MinamoTuple)item)[0].ToString())
                .SequenceEqual(new[] { "first", "second" }),
            "tuple dictionary conversion preserves label order");

        var clrDictionary = new Dictionary<string, int>
        {
            ["first"] = 1,
            ["second"] = 2
        };
        var convertedDictionary = (MinamoDictionary)TypeConverter.ConvertFrom(clrDictionary);
        Assert(
            convertedDictionary.Select(item => ((MinamoTuple)item)[0].ToString())
                .SequenceEqual(new[] { "first", "second" }),
            "CLR dictionary conversion preserves source enumeration order");

        Assert(
            !TypeConverter.TryConvert(new MinamoInteger(4_294_967_296), typeof(int), out _),
            "generic conversion rejects integer overflow");
        Assert(
            !TypeConverter.TryConvert(MinamoInteger.MinusOne, typeof(uint), out _),
            "generic conversion rejects negative unsigned value");
    }

    internal static void InteropMethodMatching()
    {
        var parameters = typeof(PipelineScenarios)
            .GetMethod(nameof(TwoParameters), BindingFlags.NonPublic | BindingFlags.Static)!
            .GetParameters();

        Assert(
            MinamoInteropTypeInfo.ParametersMatch(
                parameters,
                new MinamoObject[] { new MinamoInterop(typeof(string)), new MinamoInterop(typeof(int)) }),
            "interop method matching accepts compatible parameter types");
        Assert(
            !MinamoInteropTypeInfo.ParametersMatch(
                parameters,
                new MinamoObject[] { new MinamoInterop(typeof(string)), new MinamoInterop(typeof(string)) }),
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
