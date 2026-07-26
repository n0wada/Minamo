using System.Collections.Generic;
using Spellkit.Compiler.Lowering;
using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Debug;
using Spellkit.Runtime.Types;

namespace Spellkit.Compiler;

//Represents a memory structure for an actual (runtime) lexical scope (e.g. global
//or function). It is used for addressing.
public sealed class MemoryLayout
{
    internal MemoryLayout(int size, int stackSize, int address) =>
        (Size, StackSize, Address) = (size, stackSize, address);

    //Size of operational stack
    public int StackSize { get; }

    //Number of local variables
    public int Size { get; }

    //Address (ASM code offset)
    public int Address { get; internal set; }
}

public sealed class Reference : IEquatable<Reference>
{
    internal Guid Id { get; }

    internal int Checksum { get; set; }

    public string? LocalPath { get; }

    public string ModuleName { get; }

    public Location SourceLocation { get; }

    public string? SourceFileName { get; }

    public ForeignUnit? Instance { get; internal set; }

    internal Reference(Guid id, string moduleName, string? localPath, Location sourceLocation, string? sourceFleName)
    {
        Id = id;
        ModuleName = moduleName;
        LocalPath = localPath;
        SourceLocation = sourceLocation;
        SourceFileName = sourceFleName;
    }

    public string GetPath() =>
        LocalPath is null ? ModuleName : LocalPath + "/" + ModuleName;

    public bool Equals(Reference? other) =>
        other is not null
        && string.Equals(LocalPath, other.LocalPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ModuleName, other.ModuleName, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(LocalPath, ModuleName);

    public override bool Equals(object? obj) => obj is Reference r && Equals(r);
}

public class Unit
{
    internal int Checksum { get; set; }

    public int Id { get; internal set; }

    public FastList<Reference> References { get; }

    public FastList<int> UnitIds { get; }

    public FastList<HashString> Strings { get; }

    public FastList<SpkObject> Objects { get; }

    public FastList<Op> Ops { get; }

    public string? FileName { get; internal set; }

    public DebugInfo Symbols { get; internal set; }

    public Scope? GlobalScope { get; internal set; }

    public FastList<MemoryLayout> Layouts { get; }

    public Dictionary<HashString, ScopeVar> ExportList { get; }

    internal List<SelectDefinition> SelectDefinitions { get; private set; } = new();

    internal Unit()
    {
        Layouts = new();
        Ops = new();
        ExportList = new();
        UnitIds = new();
        References = new();
        Strings = new();
        Objects = new();
        Symbols = DebugInfo.Default;
    }

    private Unit(Unit unit, DebugInfo di)
    {
        Ops = unit.Ops;//new(unit.Ops.ToArray());
        Symbols = di;

        if (unit.GlobalScope is not null)
        {
            GlobalScope = unit.GlobalScope.Clone();
        }

        Layouts = unit.Layouts;
        ExportList = unit.ExportList;
        UnitIds = unit.UnitIds;
        References = unit.References;
        Strings = unit.Strings;
        Objects = unit.Objects;
        SelectDefinitions = unit.SelectDefinitions;
    }

    internal Unit Clone(DebugInfo di) => new(this, di);
}

public sealed class UnitComposition
{
    public Unit[] Units { get; }

    public UnitComposition(FastList<Unit> units) => Units = units.ToArray();
}
