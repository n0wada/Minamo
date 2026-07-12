using Spellkit.Compiler;
using Spellkit.Parser;
using Spellkit.Parser.Model;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Spellkit.Linker;

public partial class SpkLinker
{
    protected internal virtual Result<Unit> Link(Unit self, Reference mod)
    {
        if (!UnitMap.TryGetValue(mod.Id, out var unit))
        {
            if (mod.LocalPath is null && mod.DllName is null)
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
            else if (unit is null && mod.DllName is not null)
            {
                unit = LinkForeignModule(self, mod);

                if (unit is null)
                {
                    AddError(LinkerError.AssemblyNotFound, mod.SourceFileName!, mod.SourceLocation, mod.DllName, mod.ModuleName);
                }
                else
                {
                    foreach (var rf in unit.References)
                    {
                        var res = Link(self, rf);
                        if (!res.Success || res.Value is null)
                        {
                            return res;
                        }

                        rf.Instance = (ForeignUnit)res.Value;
                    }
                }
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

    private SpkCodeModel? ProcessBuffer(SourceBuffer buffer)
    {
        var res = SpkParser.Parse(buffer);

        if (!res.Success)
        {
            Messages.AddRange(res.Messages);
            return null;
        }

        return res.Value;
    }

    protected virtual Unit? CompileNodes(SpkCodeModel codeModel, bool root)
    {
        var compiler = new SpkCompiler(BuilderOptions, this);
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


    private ForeignUnit? LinkForeignModule(Unit self, Reference mod)
    {
        var dict = LookupAssembly(self, mod.DllName!, mod);

        if (dict is not null)
        {
            if (!dict.TryGetValue(mod.ModuleName, out var unit))
            {
                AddError(LinkerError.AssemblyModuleNotFound, mod.SourceFileName!, mod.SourceLocation,
                    mod.ModuleName, mod.DllName!);
                return null;
            }

            return unit;
        }

        return null;
    }

    private Dictionary<string, ForeignUnit>? LookupAssembly(Unit self, string dll, Reference? @ref = null)
    {
        if (!Lookup.Find(Path.GetDirectoryName(self.FileName), dll, out var path))
        {
            return null;
        }

        var key = path.Replace('\\', '/');

        if (!AssemblyMap.TryGetValue(key, out var dict))
        {
            dict = LoadAssembly(path, @ref ?? Reference.Empty);

            if (dict is not null)
            {
                AssemblyMap.Add(key, dict);
            }
        }

        return dict;
    }

    private Dictionary<string, ForeignUnit>? LoadAssembly(string path, Reference mod)
    {
        Assembly asm;

        try
        {
            asm = Assembly.LoadFrom(path);
        }
        catch (Exception ex)
        {
            AddError(LinkerError.UnableLoadAssembly, mod.SourceFileName!, mod.SourceLocation,
                mod.DllName!, ex.Message);
            return null;
        }

        var dict = new Dictionary<string, ForeignUnit>();

        foreach (var t in asm.GetTypes())
        {
            if (Attribute.GetCustomAttribute(t, typeof(SpkUnitAttribute)) is not SpkUnitAttribute attr)
            {
                continue;
            }

            if (dict.ContainsKey(attr.Name))
            {
                AddError(LinkerError.DuplicateModuleName, mod.SourceFileName!, mod.SourceLocation,
                    mod.DllName!, attr.Name);
            }

            object module;

            try
            {
                module = Activator.CreateInstance(t)!;
            }
            catch (Exception ex)
            {
                AddError(LinkerError.AssemblyModuleLoadError, mod.SourceFileName!, mod.SourceLocation,
                    mod.ModuleName, mod.DllName!, ex.Message);
                return null;
            }

            if (module is not ForeignUnit unit)
            {
                AddError(LinkerError.InvalidAssemblyModule, mod.SourceFileName!, mod.SourceLocation,
                    mod.ModuleName, mod.DllName!);
                return null;
            }

            unit.FileName = path;
            dict.Add(attr.Name, unit);
        }

        return dict;
    }
}
