using System.Collections.Generic;
using Spellkit.Compiler.Lowering;
using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Debug;
using Spellkit.Runtime.Types;

namespace Spellkit.Compiler;

public enum ScopeKind
{
    Lexical = 0,

    Function,

    Loop
}

public readonly struct ScopeVar
{
    public static readonly ScopeVar Empty = new(-1, 0);

    public readonly int Address;

    public readonly int Data;

    public readonly int Args;

    public ScopeVar(int address) : this(address, 0, 0) { }

    public ScopeVar(int address, int data) : this(address, data, 0) { }

    public ScopeVar(int address, int data, int args) => (Address, Data, Args) = (address, data, args);

    public bool IsEmpty() => Address == -1;
}

public sealed class Scope
{
    public Scope(ScopeKind kind, Scope? parent) => (Kind, Parent, Locals, Autos) = (kind, parent, new(), new());

    public ScopeVar GetVariable(string name)
    {
        if (!Locals.TryGetValue(name, out ScopeVar var))
        {
            var = ScopeVar.Empty;
        }

        return var;
    }

    public Scope Clone() =>
        new(Kind, Parent)
        {
            Locals = new(Locals),
            Autos = new(Autos)
        };

    public IEnumerable<string> EnumerateNames()
    {
        foreach (var kv in Locals)
        {
            yield return kv.Key;
        }
    }

    public IEnumerable<KeyValuePair<string, ScopeVar>> EnumerateVars()
    {
        foreach (var kv in Locals)
        {
            yield return kv;
        }
    }

    public bool LocalOrParent(string var)
    {
        if (Kind == ScopeKind.Function)
        {
            return Locals.ContainsKey(var);
        }

        var s = this;

        do
        {
            if (s.Locals.ContainsKey(var))
            {
                return true;
            }

            s = s.Parent;
        }
        while (s != null && s.Kind != ScopeKind.Function);

        return false;
    }

    public void AddData(string name, int data)
    {
        var sv = Locals[name];
        sv = new(sv.Address, data);
        Locals[name] = sv;
    }

    public int TryChangeVariable(string name)
    {
        if (Locals.TryGetValue(name, out var v))
        {
            v = new(v.Address, -1);
            Locals[name] = v;
            return v.Address;
        }

        return -1;
    }

    public bool IsGlobal => Parent == null;

    public Scope? Parent { get; set; }

    public Queue<(int, string)> Autos { get; private set; }

    public Dictionary<string, ScopeVar> Locals { get; private set; }

    public ScopeKind Kind { get; private set; }
}

internal sealed class CompilerContext
{
    private static readonly Label NoLabel = Label.Empty;

    public CompilerContext()
    {
        Errors = new();
        FunctionAddress = -1;
        BlockExit = NoLabel;
        BlockSkip = NoLabel;
        BlockBreakExit = NoLabel;
        FunctionStart = NoLabel;
        FunctionExit = NoLabel;
        MatchExit = NoLabel;
        IsIteratorBody = false;
        IsTailPosition = false;
    }

    public CompilerContext(CompilerContext old)
    {
        Errors = old.Errors;
        FunctionAddress = old.FunctionAddress;
        Function = old.Function;
        FunctionStart = old.FunctionStart;
        FunctionExit = old.FunctionExit;
        BlockBreakExit = old.BlockBreakExit;
        BlockExit = old.BlockExit;
        BlockSkip = old.BlockSkip;
        MatchExit = old.MatchExit;
        IsIteratorBody = old.IsIteratorBody;
        IsTailPosition = old.IsTailPosition;
        SelectName = old.SelectName;
        SelectStates = old.SelectStates;
        SelectIsStateLess = old.SelectIsStateLess;
    }

    public Stack<int> Errors { get; }

    public LoweredFunctionDeclaration? Function { get; set; }

    public int FunctionAddress { get; set; }

    public Label FunctionStart { get; set; }

    public Label FunctionExit { get; set; }

    public Label BlockBreakExit { get; set; }

    public Label BlockExit { get; set; }

    public Label BlockSkip { get; set; }

    public Label MatchExit { get; set; }

    public bool IsIteratorBody { get; set; }

    public bool IsTailPosition { get; set; }

    public string? SelectName { get; set; }

    public IReadOnlySet<string>? SelectStates { get; set; }

    public bool SelectIsStateLess { get; set; }

    public bool HasFunctionExit => !FunctionExit.IsEmpty();

    public bool HasLoop => !BlockExit.IsEmpty();

    public bool HasLoopContinue => !BlockSkip.IsEmpty();

    public CompilerContext WithLoop(Label blockSkip, Label blockExit, Label blockBreakExit)
    {
        var ctx = new CompilerContext(this)
        {
            BlockSkip = blockSkip,
            BlockExit = blockExit,
            BlockBreakExit = blockBreakExit
        };
        return ctx;
    }

    public CompilerContext WithFunction(LoweredFunctionDeclaration function, int functionAddress, Label functionStart, Label functionExit, bool isIteratorBody)
    {
        var ctx = new CompilerContext(this)
        {
            Function = function,
            FunctionAddress = functionAddress,
            FunctionStart = functionStart,
            FunctionExit = functionExit,
            IsIteratorBody = isIteratorBody
        };
        return ctx;
    }

    public CompilerContext WithMatchExit(Label matchExit)
    {
        var ctx = new CompilerContext(this)
        {
            MatchExit = matchExit
        };
        return ctx;
    }

    public CompilerContext WithTailPosition(bool isTailPosition)
    {
        var ctx = new CompilerContext(this)
        {
            IsTailPosition = isTailPosition
        };
        return ctx;
    }

    public CompilerContext WithSelectStates(
        string name,
        IReadOnlySet<string> states,
        bool isStateLess)
    {
        var ctx = new CompilerContext(this)
        {
            SelectName = name,
            SelectStates = states,
            SelectIsStateLess = isStateLess
        };
        return ctx;
    }
}

internal static class FunAttr
{
    public const int None = 0x00;

    public const int Auto = 0x01;

    public const int Variadic = 0x02;
}

internal sealed class VarFlags
{
    public const int None = 0;
    public const int Const = 0x01;
    public const int Argument = 0x02;
    public const int Function = 0x04;
    public const int External = 0x08;
    public const int Foreign = 0x10;
    public const int Module = 0x20;
    public const int This = 0x40;
    public const int Private = 0x80;
    public const int PreInit = 0x100;
    public const int Type = 0x200;
    public const int StdCall = 0x800;
}
