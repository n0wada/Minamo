using Minamo.Compiler;
using Minamo.Parser;
using Minamo.Parser.Model;
using Minamo.Runtime.Types;
using Minamo.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Minamo.Linker;

public partial class MinamoLinker
{
    private const string EXT = ".nami";
    private readonly Lang lang;

    protected Dictionary<Guid, Unit> UnitMap { get; set; } = new();

    protected Dictionary<string, Unit> SearchMap { get; set; } = new();

    private readonly HashSet<string> linkingPaths = new(StringComparer.OrdinalIgnoreCase);

    protected FastList<Unit> Units { get; set; } = new();

    protected FastList<BuildMessage> Messages { get; } = new();

    public BuilderOptions BuilderOptions { get; }

    public FileLookup Lookup { get; }

    public MinamoLinker(FileLookup lookup, MinamoTuple? args = null)
    {
        Lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        BuilderOptions = lookup.BuilderOptions;
        lang = BuilderOptions.LangFactory?.Invoke(args, BuilderOptions.ExposeHostObject)
            ?? new Lang(args, BuilderOptions.ExposeHostObject);
        lang.FileName = nameof(lang);
        lang.Id = 1;
        Units.Add(null!);
        Units.Add(lang);
    }

    public Result<UnitComposition> Make(string filePath)
    {
        Messages.Clear();
        string fullPath;

        if (!Lookup.Find(Path.GetDirectoryName(filePath), Path.GetFileName(filePath), out fullPath))
        {
            AddError(LinkerError.ModuleNotFound, filePath, default, filePath);
            return Result.Create(default(UnitComposition), Messages);
        }

        SourceBuffer buffer;

        try
        {
            buffer = SourceBuffer.FromFile(fullPath);
        }
        catch (Exception ex)
        {
            AddError(LinkerError.UnableReadModule, fullPath, default, fullPath, ex.Message);
            return Result.Create(default(UnitComposition), Messages);
        }

        fullPath = NormalizePath(fullPath);
        linkingPaths.Add(fullPath);
        try
        {
            var codeModel = ProcessBuffer(buffer);
            return codeModel is null
                ? Result.Create(default(UnitComposition), Messages)
                : Make(codeModel);
        }
        finally
        {
            linkingPaths.Remove(fullPath);
        }
    }

    public Result<UnitComposition> Make(SourceBuffer buffer)
    {
        Messages.Clear();
        var codeModel = ProcessBuffer(buffer);

        if (codeModel == null)
        {
            return Result.Create(default(UnitComposition), Messages);
        }

        return Make(codeModel);
    }

    public Result<Unit> Compile(SourceBuffer buffer)
    {
        Messages.Clear();
        var codeModel = ProcessBuffer(buffer);

        if (codeModel is null)
        {
            return Result.Create(default(Unit), Messages);
        }

        return Compile(codeModel);
    }

    public Result<Unit> Compile(MinamoCodeModel codeModel)
    {
        Prepare();

        try
        {
            var unit = CompileNodes(codeModel, root: true);
            return Result.Create(unit, Messages);
        }
        finally
        {
            var failed = Messages.Any(m => m.Type == BuildMessageType.Error);
            Complete(failed);
        }
    }

    public Result<UnitComposition> Make(MinamoCodeModel codeModel)
    {
        Prepare();

        try
        {
            var unit = CompileNodes(codeModel, root: true);

            if (unit is null)
            {
                return Result.Create(default(UnitComposition), Messages);
            }

            return Make(unit);
        }
        finally
        {
            var failed = Messages.Any(m => m.Type == BuildMessageType.Error);
            Complete(failed);
        }
    }

    protected virtual void Prepare() { }

    protected virtual void Complete(bool failed) { }

    protected virtual Result<UnitComposition> Make(Unit unit)
    {
        Units[0] = unit;
        var asm = new UnitComposition(Units);
        ProcessUnits();
        return Result.Create(asm, Messages);
    }

    protected void ProcessUnits()
    {
        for (var uid = 0; uid < Units.Count; uid++)
        {
            var u = Units[uid];

            for (var i = 0; i < u.References.Count; i++)
            {
                var r = u.References[i];
                u.UnitIds[i] = UnitMap[r.Id].Id;
            }
        }
    }

    private void AddError(LinkerError error, string fileName, Location loc, params object[] args)
    {
        var codeName = error.ToString();
        var str = MessageCatalog.Find(MessageGroup.Linker, codeName) ?? codeName;

        if (args is not null)
        {
            str = string.Format(str, args);
        }

        Messages.Add(new(str, BuildMessageType.Error, (int)error, loc.Line, loc.Column, fileName));
    }
}
