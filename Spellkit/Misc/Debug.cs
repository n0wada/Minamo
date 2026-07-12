using System.Collections.Generic;
using static System.Math;
using Spellkit.Compiler;
using System.Collections;
using System.Text;
using Spellkit.Parser.Model;
using Spellkit.Runtime.Types;

namespace Spellkit.Debug;

public sealed class ScopeSym
{
    public int Index { get; init; }

    public int ParentIndex { get; init; }

    public int StartOffset { get; init; }

    public int EndOffset { get; internal set; }

    public int StartLine { get; init; }

    public int StartColumn { get; init; }

    public int EndLine { get; internal set; }

    public int EndColumn { get; internal set; }

    public ScopeSym() { }

    public ScopeSym(int index, int parentIndex, int startOffset, int endOffset, int startLine,
        int startColumn, int endLine, int endColumn)
    {
        Index = index;
        ParentIndex = parentIndex;
        StartOffset = startOffset;
        EndOffset = endOffset;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }
}

public sealed class LineSym
{
    internal static readonly LineSym Empty = new(0);

    public int Offset { get; }

    public int Line { get; }

    public int Column { get; }

    internal LineSym(int offset) => Offset = offset;

    internal LineSym(int offset, int line, int column) =>
        (Offset, Line, Column) = (offset, line, column);
}

public sealed class VarSym
{
    public required string Name { get; init; }

    public int Address { get; init; }

    public int Offset { get; init; }

    public int Scope { get; init; }

    public int Flags { get; init; }

    public int Data { get; init; }

    public override string ToString() => Name;
}

public sealed class FunSym
{
    public string Name { get; }

    public string? TypeName { get; init; }

    public Par[]? Parameters { get; init; }

    public int StartOffset { get; init; }

    public int EndOffset { get; internal set; }

    public int Handle { get; internal set; }

    internal FunSym(string name) => Name = name;

    internal FunSym(string name, string? typeName, int offset, Par[] pars) =>
        (Name, TypeName, StartOffset, Parameters) = (name, typeName, offset, pars);
}

public sealed class DebugInfo
{
    internal static readonly DebugInfo Default = new();

    public string? File { get; }

    public List<ScopeSym> Scopes { get; }

    public List<LineSym> Lines { get; }

    public List<VarSym> Vars { get; }

    public Dictionary<int, FunSym> Functions { get; }

    public DebugInfo() : this(default(string)) { }

    public DebugInfo(string? file)
    {
        Scopes = new();
        Lines = new();
        Vars = new();
        Functions = new();
        File = file;
    }

    private DebugInfo(DebugInfo di)
    {
        File = di.File;
        Scopes = new(di.Scopes.ToArray());
        Lines = new(di.Lines.ToArray());
        Vars = new(di.Vars.ToArray());
        Functions = new(di.Functions);
    }

    public DebugInfo Clone() => new(this);
}

public static class DebugReaderExtensions
{
    public static FunSym? FindFunSymByStart(this DebugInfo syms, int offset)
    {
        foreach (var f in syms.Functions.Values)
        {
            if (offset == f.StartOffset)
            {
                return f;
            }
        }

        return null;
    }

    public static FunSym? FindFunSym(this DebugInfo syms, int offset)
    {
        foreach (var f in syms.Functions.Values)
        {
            if (offset > f.StartOffset && offset < f.EndOffset)
            {
                return f;
            }
        }

        return null;
    }

    public static LineSym? FindLineSym(this DebugInfo syms, int offset)
    {
        if (offset < 0)
        {
            return null;
        }

        for (var i = 0; i < syms.Lines.Count; i++)
        {
            var l = syms.Lines[i];

            if (l.Offset == offset)
            {
                return l;
            }
        }

        return offset == 0 ? null : FindLineSym(syms, offset - 1);
    }

    public static LineSym? FindClosestLineSym(this DebugInfo syms, int line, int column)
    {
        var ln = default(LineSym);
        var minDiffCol = int.MaxValue;
        var minDiffLine = int.MaxValue;

        for (var i = 0; i < syms.Lines.Count; i++)
        {
            var l = syms.Lines[i];

            if (l.Line == line && l.Column == column)
            {
                ln = l;
                break;
            }
            else if (Abs(l.Line - line) < minDiffLine)
            {
                minDiffLine = Abs(l.Line - line);
                minDiffCol = Abs(l.Column - column);
                ln = l;
            }
            else if (Abs(l.Line - line) == minDiffLine && Abs(l.Column - column) < minDiffCol)
            {
                minDiffCol = Abs(l.Column - column);
                ln = l;
            }
        }

        if (ln != null)
        {
            for (var i = 0; i < syms.Lines.Count; i++)
            {
                var l = syms.Lines[i];

                if (l.Line == ln.Line && l.Column == ln.Column && l.Offset > ln.Offset)
                {
                    ln = l;
                }
            }
        }

        return ln;
    }

    public static ScopeSym? GetScopeSymByIndex(this DebugInfo syms, int scopeIndex)
    {
        for (var i = 0; i < syms.Scopes.Count; i++)
        {
            if (syms.Scopes[i].Index == scopeIndex)
            {
                return syms.Scopes[i];
            }
        }

        return null;
    }

    public static ScopeSym? FindScopeSym(this DebugInfo syms, int offset)
    {
        var scope = default(ScopeSym);

        for (var i = 0; i < syms.Scopes.Count; i++)
        {
            var s = syms.Scopes[i];

            if (offset >= s.StartOffset && offset <= s.EndOffset)
            {
                scope = s;
            }
        }

        return scope;
    }

    public static ScopeSym? FindScopeSym(this DebugInfo syms, int line, int column)
    {
        for (var i = 0; i < syms.Scopes.Count; i++)
        {
            var s = syms.Scopes[i];

            if ((line == s.StartLine && column >= s.StartColumn || line > s.StartLine)
                 && line <= s.EndLine
                )
            {
                return s;
            }
        }

        return null;
    }

    public static VarSym? FindVarSym(this DebugInfo syms, int address, int scopeIndex)
    {
        for (var i = 0; i < syms.Vars.Count; i++)
        {
            var v = syms.Vars[i];

            if (v.Address == address && v.Scope >= scopeIndex)
            {
                return v;
            }
        }

        return default;
    }

    public static IEnumerable<VarSym> FindVarSyms(this DebugInfo syms, int offset, ScopeSym scope)
    {
        for (var i = 0; i < syms.Vars.Count; i++)
        {
            var v = syms.Vars[i];

            if ((scope is null && v.Scope == 0 || scope is not null && v.Scope == scope.Index) &&
                v.Offset <= offset)
            {
                yield return v;
            }
        }
    }

    public static IEnumerable<VarSym> EnumerateVarSyms(this DebugInfo syms) => syms.Vars.ToArray();
}

public sealed class DebugWriter
{
    private readonly Stack<ScopeSym> scopes;
    private readonly Stack<FunSym> funs;
    private int scopeCount;

    public DebugInfo Symbols { get; }

    public VarSym? LastVarSym { get; private set; }

    public DebugWriter()
    {
        Symbols = new();
        scopes = new();
        funs = new();
        var glob = new ScopeSym { EndOffset = int.MaxValue };
        scopes.Push(glob);
        Symbols.Scopes.Add(glob);
    }

    private DebugWriter(DebugWriter dw)
    {
        Symbols = dw.Symbols.Clone();
        scopes = new(dw.scopes.ToArray());
        funs = new(dw.funs.ToArray());
    }

    public DebugWriter Clone() => new(this);

    public void StartFunction(string name, int offset, string? typeName, Par[]? pars = null) =>
        funs.Push(new(name, typeName, offset, pars ?? Array.Empty<Par>()));
    
    public void EndFunction(int handle, int offset)
    {
        var f = funs.Pop();
        f.Handle = handle;
        f.EndOffset = offset;
        Symbols.Functions.Add(handle, f);
    }

    public void StartScope(int offset, int line, int col) =>
        scopes.Push(new()
        {
            Index = ++scopeCount,
            ParentIndex = scopes.Peek().Index,
            StartOffset = offset,
            StartLine = line,
            StartColumn = col
        });

    public void EndScope(int offset, int line, int col)
    {
        var s = scopes.Pop();
        s.EndOffset = offset;
        s.EndLine = line;
        s.EndColumn = col;
        Symbols.Scopes.Add(s);
    }

    public void AddVarSym(string name, int address, int offset, int flags, int data) =>
        Symbols.Vars.Add(LastVarSym = new()
        {
            Name = name,
            Address = address,
            Offset = offset,
            Scope = scopes.Peek().Index,
            Flags = flags,
            Data = data
        });
    
    public void AddLineSym(int offset, int line, int col) =>
        Symbols.Lines.Add(new(offset, line, col));
}

public sealed class Breakpoint : IEquatable<Breakpoint>
{
    public Breakpoint(int line, int column) : this(line, column, false) { }

    public Breakpoint(int line, int column, bool temp) =>
        (Id, Line, Column, Temporary) = (Guid.NewGuid(), line, column, temp);

    public bool Temporary { get; }

    public int Line { get; }

    public int Column { get; }

    internal Guid Id { get; }

    internal int Offset { get; set; }

    public override int GetHashCode() => HashCode.Combine(Line, Column);

    public static bool Equals(Breakpoint? fst, Breakpoint? snd) =>
        fst is null && snd is null || ReferenceEquals(fst, snd) || (fst?.Line == snd?.Line && fst?.Column == snd?.Column);

    public bool Equals(Breakpoint? other) => Equals(this, other);

    public override bool Equals(object? obj) => obj is Breakpoint b && Equals(this, b);

    public override string ToString() => $"{(Temporary ? "#" : "")}{Line}:{Column}";
}

public readonly struct StackPoint
{
    public static readonly StackPoint External = new(external: true);

    internal static readonly StackPoint Empty = new(-1, -1);

    public readonly int Offset;

    public readonly int UnitId;

    public readonly bool IsExternal;

    public bool IsEmpty => Offset == -1;

    internal StackPoint(int offset, int unitId) =>
        (Offset, UnitId, IsExternal) = (offset, unitId, false);

    private StackPoint(bool external) =>
        (Offset, UnitId, IsExternal) = (0, 0, external);
}

public class CallFrame
{
    private const string Format = "\tat {0} in {1}, line {2}, column {3}";
    private const string ShortFormat = "\tat {0} in {1}, offset {2}";
    private const string ExternalPoint = "\tat <external code>";
    private const string Global = "<global>";

    internal static readonly CallFrame External = new ExternalCallFrame();

    private sealed class ExternalCallFrame : CallFrame
    {
        internal ExternalCallFrame() : base("", "", 0, LineSym.Empty) { }

        public override string ToString() => ExternalPoint;
    }

    private string GetName() => CodeBlockName ?? Global;

    public string? CodeBlockName { get; }

    public string? ModuleName { get; }

    public int Offset { get; }

    public LineSym? LinePragma { get; }

    internal CallFrame(string? moduleName, string codeBlockName, int offset, LineSym lineSym) =>
        (CodeBlockName, ModuleName, Offset, LinePragma) = (moduleName, codeBlockName, offset, lineSym);

    public override string ToString() =>
        LinePragma != null
            ? string.Format(Format, GetName(), ModuleName, LinePragma.Line, LinePragma.Column)
            : string.Format(ShortFormat, GetName(), ModuleName, Offset);
}

public sealed class CallStackTrace : IEnumerable<CallFrame>
{
    private readonly List<CallFrame> frames;

    internal CallStackTrace(List<CallFrame> frames) => this.frames = frames;

    public IEnumerator<CallFrame> GetEnumerator() => frames.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public CallFrame this[int index] => frames[index];

    public int FrameCount => frames.Count;

    public override string ToString()
    {
        var sb = new StringBuilder();

        foreach (var cf in this)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.Append(cf.ToString());
        }

        return sb.ToString();
    }
}

internal sealed class SpkDebugger
{
    private const string DefaultName = "<func>";
    private const string Unknown = "<unknown>";
    private const string Global = "<global>";

    internal UnitComposition Composition { get; }

    public SpkDebugger(UnitComposition asm)
    {
        Composition = asm;

        //if (CodeUnit.Symbols.Lines.Count > 0)
        //    Breakpoints = new List<Breakpoint>();
    }

    public CallStackTrace BuildCallStack(Stack<StackPoint> callChain)
    {
        var frames = new List<CallFrame>();
        var retval = new CallStackTrace(frames);

        if (callChain is null || callChain.Count == 0)
        {
            return retval;
        }

        do
        {
            var mem = callChain.Pop();

            if (mem.IsExternal)
            {
                frames.Add(CallFrame.External);
                continue;
            }

            var offset = mem.Offset - 1;
            var unit = Composition.Units[mem.UnitId];

            if (unit.Symbols is null)
            {
                frames.Add(new(
                    moduleName: unit.FileName,
                    codeBlockName: Unknown,
                    offset: offset,
                    lineSym: new(offset)));
                continue;
            }

            var funSym = unit.Symbols.FindFunSym(offset);
            var line = unit.Symbols.FindLineSym(offset);
            string? codeBlockName = null;

            if (funSym != null)
            {
                codeBlockName = funSym.Name ?? DefaultName;

                if (funSym.TypeName is not null)
                {
                    codeBlockName = funSym.TypeName + "." + codeBlockName;
                }

                if (funSym.Parameters is not null)
                {
                    codeBlockName += "(" + string.Join(",", funSym.Parameters) + ")";
                }
                else
                {
                    codeBlockName += "(...)";
                }
            }

            frames.Add(new(
                moduleName: unit.FileName,
                codeBlockName: codeBlockName ?? Global,
                offset: offset,
                lineSym: line ?? new LineSym(offset)));
        }
        while (callChain.Count > 0);

        return retval;
    }
}

public enum ParKind
{
    NotSpecified = 0,

    VarArg
}

public readonly struct Par
{
    public readonly string Name;
    public readonly bool IsVarArg;
    public readonly SpkObject? Value;
    public readonly TypeAnnotation? TypeAnnotation;

    internal Par(string name, SpkObject? val, bool isVarArg, TypeAnnotation? ta) =>
        (Name, Value, IsVarArg, TypeAnnotation) = (name, val, isVarArg, ta);

    internal Par(string name, SpkObject? val, bool isVarArg) =>
        (Name, Value, IsVarArg, TypeAnnotation) = (name, val, isVarArg, null);

    public Par(string name, ParKind kind) => (Name, Value, IsVarArg, TypeAnnotation) = (name, null, kind == ParKind.VarArg, null);

    public Par(string name, ParKind kind, SpkObject? value)
    {
        value ??= SpkNil.Instance;
        (Name, Value, IsVarArg, TypeAnnotation) = (name, value, kind == ParKind.VarArg, null);
    }

    public Par(string name, SpkObject? value)
    {
        value ??= SpkNil.Instance;
        (Name, Value, IsVarArg, TypeAnnotation) = (name, value, false, null);
    }

    public Par(string name) => (Name, Value, IsVarArg, TypeAnnotation) = (name, null, false, null);

    public Par(string name, int value) => (Name, Value, IsVarArg, TypeAnnotation) = (name, SpkInteger.Get(value), false, null);

    public Par(string name, double value) => (Name, Value, IsVarArg, TypeAnnotation) = (name, new SpkFloat(value), false, null);

    public Par(string name, string value) => (Name, Value, IsVarArg, TypeAnnotation) = (name, new SpkString(value), false, null);

    public Par(string name, char value) => (Name, Value, IsVarArg, TypeAnnotation) = (name, new SpkChar(value), false, null);

    public Par(string name, bool value) => (Name, Value, IsVarArg, TypeAnnotation) = (name, value ? True : False, false, null);

    public override string ToString() => Name;

    public static implicit operator Par(string name) => new(name);
}
