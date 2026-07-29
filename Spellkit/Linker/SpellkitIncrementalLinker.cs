using Spellkit.Compiler;
using Spellkit.Parser.Model;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;

namespace Spellkit.Linker;

internal sealed class SpellkitIncrementalLinker : SpellkitLinker
{
    private SpellkitCompilerEngine? compiler;
    private SpellkitCompilerEngine? oldCompiler;
    private UnitComposition? composition;
    private int? startOffset;
    private Dictionary<Guid, Unit>? backupUnitMap;
    private List<Unit>? backupUnits;

    public SpellkitIncrementalLinker(FileLookup lookup, SpellkitTuple? args = null) : base(lookup, args) { }

    protected override void Prepare()
    {
        backupUnitMap = new(UnitMap);
        backupUnits = new(Units!);
    }

    protected override void Complete(bool failed)
    {
        if (failed)
        {
            Rollback();
        }
    }

    public void Rollback()
    {
        if (backupUnitMap is not null)
        {
            UnitMap = backupUnitMap;
        }

        Units.Clear();

        if (backupUnits is null)
        {
            return;
        }

        for (var i = 0; i < backupUnits.Count; i++)
        {
            Units.Add(backupUnits[i]);
        }

        compiler = oldCompiler;
    }

    public void Commit() => oldCompiler = compiler;

    protected override Result<UnitComposition> Make(Unit unit)
    {
        Units[0] = unit;
        composition = new(Units);
        ProcessUnits();
        return Result.Create(composition, Messages);
    }

    protected override Unit? CompileNodes(SpellkitCodeModel codeModel, bool root)
    {
        if (!root)
        {
            return base.CompileNodes(codeModel, root);
        }

        Messages.Clear();

        if (compiler is null)
        {
            compiler = new(BuilderOptions, this);
        }
        else
        {
            compiler = new(compiler);
            var ops = composition!.Units[0]?.Ops;
            startOffset = ops is null ? 0 : ops.Count;
        }

        var res = compiler.Compile(codeModel);

        if (res.Messages.Any())
        {
            Messages.AddRange(res.Messages);
        }

        if (!res.Success)
        {
            compiler = oldCompiler;
            startOffset = null;
            return null;
        }

        if (startOffset is not null)
        {
            res.Value!.Layouts[0].Address = startOffset.Value;
        }

        return res.Value;
    }
}
