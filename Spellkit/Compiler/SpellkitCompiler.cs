using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Parser.Model;

namespace Spellkit.Compiler;

public static class SpellkitCompiler
{
    public static Result<UnitComposition> Compile(
        string source,
        BuilderOptions? options = null,
        string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= BuilderOptions.Default();
        var lookup = FileLookup.Restricted(options).Build();
        return Compile(source, lookup, sourceName);
    }

    public static Result<UnitComposition> Compile(
        string source,
        FileLookup lookup,
        string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lookup);
        return new SpkLinker(lookup).Make(SourceBuffer.FromString(source, sourceName));
    }

    public static Result<UnitComposition> Compile(
        SpkCodeModel codeModel,
        BuilderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(codeModel);
        options ??= BuilderOptions.Default();
        return Compile(codeModel, FileLookup.Restricted(options).Build());
    }

    public static Result<UnitComposition> Compile(
        SpkCodeModel codeModel,
        FileLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(codeModel);
        ArgumentNullException.ThrowIfNull(lookup);
        return new SpkLinker(lookup).Make(codeModel);
    }

    public static Result<UnitComposition> CompileFile(
        string path,
        BuilderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= BuilderOptions.Default();
        return CompileFile(path, FileLookup.Restricted(options).Build());
    }

    public static Result<UnitComposition> CompileFile(string path, FileLookup lookup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(lookup);
        return new SpkLinker(lookup).Make(SourceBuffer.FromFile(path));
    }
}
