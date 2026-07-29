using Spellkit.Compiler;
using Spellkit.Parser;
using Spellkit.Parser.Model;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spellkit.Linker;

public partial class SpellkitLinker
{
    protected internal virtual Result<Unit> Link(Unit self, Reference mod)
    {
        if (!UnitMap.TryGetValue(mod.Id, out var unit))
        {
            if (mod.LocalPath is null)
            {
                if (BuilderOptions.ModuleProvider?.TryGetUnit(mod.ModuleName, out var hostUnit) == true)
                {
                    unit = hostUnit;
                }
            }

            if (unit is null && mod.ModuleName == nameof(lang))
            {
                unit = lang;
            }
            else if (unit is null)
            {
                var path = FindModule(self, mod.GetPath(), mod);

                if (path is not null && !SearchMap.TryGetValue(path, out unit))
                {
                    if (!linkingPaths.Add(path))
                    {
                        AddError(LinkerError.CircularModuleReference, mod.SourceFileName!, mod.SourceLocation, path);
                    }
                    else
                    {
                        try
                        {
                            unit = ProcessSourceFile(path, mod);

                            if (unit is not null)
                            {
                                SearchMap.Add(path, unit);
                            }
                        }
                        finally
                        {
                            linkingPaths.Remove(path);
                        }
                    }
                }
            }

            if (unit is not null)
            {
                if (unit.Id == 0)
                {
                    unit.Id = Units.Count;
                    Units.Add(unit);
                }

                UnitMap.Add(mod.Id, unit);
            }
        }

        if (unit is not null && mod.Checksum != 0 && mod.Checksum != unit.Checksum && !BuilderOptions.LinkerSkipChecksum)
        {
            AddError(LinkerError.ChecksumValidationFailed, mod.SourceFileName!, mod.SourceLocation, mod.ModuleName, unit.FileName ?? "<unknown>");
        }

        return Result.Create(unit, Messages);
    }

    private Unit? ProcessSourceFile(string fileName, Reference reference)
    {
        string src;

        try
        {
            src = File.ReadAllText(fileName);
        }
        catch (Exception ex)
        {
            AddError(LinkerError.UnableReadModule, reference.SourceFileName!, reference.SourceLocation, fileName, ex.Message);
            return null;
        }

        var codeModel = ProcessBuffer(new StringBuffer(src, fileName));
        return codeModel is not null ? CompileNodes(codeModel, root: false) : null;
    }

    private SpellkitCodeModel? ProcessBuffer(SourceBuffer buffer)
    {
        var res = SpellkitParser.Parse(buffer);

        if (!res.Success)
        {
            Messages.AddRange(res.Messages);
            return null;
        }

        return res.Value;
    }

    protected virtual Unit? CompileNodes(SpellkitCodeModel codeModel, bool root)
    {
        var compiler = new SpellkitCompilerEngine(BuilderOptions, this);
        var res = compiler.Compile(codeModel);

        if (res.Messages.Any())
        {
            Messages.AddRange(res.Messages);
        }

        if (!res.Success)
        {
            return null;
        }

        return res.Value;
    }

    private string? FindModule(Unit self, string module, Reference mod)
    {
        if (!module.EndsWith(EXT, StringComparison.OrdinalIgnoreCase))
        {
            module += EXT;
        }

        if (!FindModuleExact(self.FileName!, module, mod, out var path))
        {
            AddError(LinkerError.ModuleNotFound, mod.SourceFileName!, mod.SourceLocation, module);
            return null;
        }
        else
        {
            return path;
        }
    }

    private bool FindModuleExact(string workingDir, string module, Reference mod, out string? path)
    {
        path = null;

        if (!Lookup.Find(Path.GetDirectoryName(workingDir), module, out var fullPath))
        {
            return false;
        }

        if (!ShouldSuppressWarning((int)LinkerWarning.NewerSourceFile)
            && !string.Equals(Path.GetExtension(module), ".DLL", StringComparison.OrdinalIgnoreCase))
        {
            var sf = Path.Combine(Path.GetDirectoryName(fullPath)!, Path.GetFileNameWithoutExtension(fullPath) + ".kit");

            if (File.Exists(sf) && File.GetLastWriteTime(sf) > File.GetLastWriteTime(fullPath))
            {
                AddWarning(LinkerWarning.NewerSourceFile, mod.SourceFileName!, mod.SourceLocation, Path.GetFileNameWithoutExtension(fullPath));
            }
        }

        path = NormalizePath(fullPath);
        return true;

    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');


}
