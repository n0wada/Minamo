using Spellkit.Parser;
using Spellkit.Parser.Model;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Spellkit.Compiler.Lowering;

namespace Spellkit.Compiler;

//This part is responsible for adding/resolving variables
partial class Builder
{
    //Variables indexers
    private readonly Stack<int> counters; //Stack of indices for the lexical scope
    private int currentCounter; //Global indexer
    //If we have a simple expression than all name references are qualified as implicit function parameters
    private bool CrawlVariables(SyntaxNode? node, HashSet<string> vars)
    {
        if (node is null)
        {
            return true;
        }

        switch (node.NodeType)
        {
            case NodeType.ExpressionStatement:
                return CrawlVariables(((ExpressionStatementSyntax)node).Expression, vars);
            case NodeType.Name:
                vars.Add(((NameSyntax)node).Value);
                return true;
            case NodeType.Label:
                return CrawlVariables(((LabelLiteralSyntax)node).Expression, vars);
            case NodeType.Unary:
                return CrawlVariables(((UnaryOperationSyntax)node).Node, vars);
            case NodeType.Binary:
                return CrawlVariables(((BinaryOperationSyntax)node).Left, vars)
                    && CrawlVariables(((BinaryOperationSyntax)node).Right, vars);
            case NodeType.Application:
                if (CrawlVariables(((ApplicationSyntax)node).Target, vars))
                {
                    foreach (var a in ((ApplicationSyntax)node).Arguments)
                    {
                        if (!CrawlVariables(a, vars))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                else
                {
                    return false;
                }

            case NodeType.Index:
                return CrawlVariables(((IndexerSyntax)node).Target, vars)
                    && CrawlVariables(((IndexerSyntax)node).Index, vars);
            case NodeType.Tuple:
                {
                    foreach (var n in ((TupleLiteralSyntax)node).Elements)
                    {
                        if (!CrawlVariables(n, vars))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            case NodeType.Array:
                {
                    foreach (var n in ((ArrayLiteralSyntax)node).Elements)
                    {
                        if (!CrawlVariables(n, vars))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            case NodeType.Comprehension:
                {
                    var comp = (ComprehensionSyntax)node;
                    return CrawlVariables(comp.Key, vars)
                        && CrawlVariables(comp.Value, vars)
                        && CrawlVariables(comp.Target, vars)
                        && CrawlVariables(comp.Guard, vars);
                }
            case NodeType.Range:
                return CrawlVariables(((RangeSyntax)node).From, vars)
                    && CrawlVariables(((RangeSyntax)node).To, vars);
            case NodeType.Access:
                return CrawlVariables(((AccessSyntax)node).Target, vars);
            case NodeType.Throw:
                return CrawlVariables(((ThrowSyntax)node).Expression, vars);
            case NodeType.As:
                return CrawlVariables(((AsSyntax)node).Expression, vars);
            case NodeType.String:
            case NodeType.Nil:
            case NodeType.Float:
            case NodeType.Integer:
            case NodeType.Char:
            case NodeType.Boolean:
                return true;
            default:
                return false;
        }
    }

    private CompilerError VariableExists(string name)
    {
        var err = GetVariable(name, out _);

        if (err is CompilerError.None)
        {
            return err;
        }

        return err;
    }

    private int PushVariable(CompilerContext ctx, string name, Location loc)
    {
        var err = GetVariable(name, out var sv);

        if (err is not CompilerError.None)
        {
            if (string.IsNullOrEmpty(name))
            {
                return default;
            }

            if (char.IsUpper(name[0]))
            {
                var ti = Spk.GetTypeCodeByName(name);

                if (ti != 0)
                {
                    AddLinePragma(loc);
                    cw.LoadType(ti);
                    return -ti;
                }
                else
                {
                    AddError(CompilerError.UndefinedType, loc, name);
                }

                return default;
            }

            if (options.AllowEnvironmentNames
                && (options.ExposeHostObject || !string.Equals(name, "host", StringComparison.Ordinal)))
            {
                AddLinePragma(loc);
                cw.LoadEnvironment(name);
            }
            else
            {
                AddError(err, loc, name);
            }
            return default;
        }

        AddLinePragma(loc);
        DirectPushScopeVar(name, sv);
        return sv.Address;
    }

    private void DirectPushScopeVar(string name, ScopeVar sv)
    {
        cw.LoadVariable(sv);
    }

    private void PopVariable(CompilerContext ctx, string name, Location loc)
    {
        var err = GetVariable(name, out var sv);

        if (err is not CompilerError.None)
        {
            AddError(err, loc, name);
            return;
        }

        AddLinePragma(loc);
        cw.StoreVariable(sv.Address);
        
        if ((sv.Data & VarFlags.Const) == VarFlags.Const)
        {
            AddError(CompilerError.UnableAssignConstant, loc, name);
        }
    }

    //Standard routine to add variables, can be used when an internal unnamed variable is need
    //which won't be visible to the user (for system purposes).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int AddVariable()
    {
        var ret = 0 | currentCounter << 8;
        currentCounter++;
        return ret;
    }

    //Call close for all variables in this scope, registered by use declarations
    private void CallAutos(bool cls = false)
    {
        PeekAutos(currentScope);
        if (cls)
        {
            currentScope.Autos.Clear();
        }
    }

    private void CallAutosForKind(ScopeKind kind)
    {
        var scope = currentScope;
        var last = false;
        var shift = 0;

        while (true)
        {
            PeekAutos(scope, shift);

            if (last)
            {
                break;
            }

            scope = scope.Parent;

            if (scope!.Kind == kind)
            {
                last = true;
            }

            if (scope.Kind == ScopeKind.Function)
            {
                shift++;
            }
        }
    }

    private void PeekAutos(Scope scope, int shift = 0)
    {
        foreach (var a in scope.Autos)
        {
            var sv = new ScopeVar(shift | a.Item1 << 8);
            var escape = cw.DefineLabel();
            cw.LoadVariable(sv);
            cw.CheckNull();
            cw.JumpIfTrue(escape);
            cw.LoadVariable(sv);
            cw.LoadMember(Builtins.Dispose);
            cw.PrepareCall(0);
            cw.InvokePreparedCall(0);
            cw.Drop();
            cw.MarkLabel(escape);
            cw.NoOperation();
        }
    }


    //Add a regular named variable
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int AddVariable(string name, Location loc, int data) => AddVariable(name, loc, data, 0);

    //Add a regular named variable
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int AddVariable(string name, Location loc, int data, int args)
    {
        //Check if we already have such a variable in the local scope
        if (currentScope.Locals.TryGetValue(name, out var exist))
        {
            if ((exist.Data & VarFlags.PreInit) == VarFlags.PreInit && (data & VarFlags.Function) == VarFlags.Function)
            {
                currentScope.Locals[name] = new ScopeVar(exist.Address, exist.Data ^ VarFlags.PreInit, args);
                return (0 | exist.Address << 8);
            }
            else
            {
                AddError(CompilerError.VariableAlreadyDeclared, loc, name);
                return -1;
            }
        }

        currentScope.Locals.Add(name, new(currentCounter, data, args));

        //An extended debug info is generated only in debug mode
        if (isDebug && !loc.IsEmpty)
        {
            AddVarPragma(name, currentCounter, cw.Offset, data);
            AddLinePragma(loc);
            cw.NoOperation();
        }

        var retval = AddVariable();

        if (currentScope == globalScope)
        {
            unit.ExportList.Remove(name);
            unit.ExportList.Add(name, new(retval, data, args));
        }

        return retval;
    }

    private bool TryGetLocalVariable(string name, out ScopeVar var)
    {
        var = default;

        if (currentScope.Locals.TryGetValue(name, out var sv))
        {
            var = new(0 | sv.Address << 8, sv.Data);
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CompilerError GetVariable(string name, out ScopeVar var)
    {
        var cur = currentScope;
        var shift = 0;

        //Search all upper scopes recursively
        do
        {
            if (cur.Locals.TryGetValue(name, out var sv))
            {
                var = new(shift | sv.Address << 8, sv.Data);
                return CompilerError.None;
            }

            if (cur.Kind == ScopeKind.Function)
            {
                shift++;
            }

            cur = cur.Parent;
        }
        while (cur != null);

        //No luck. Need to check if this variable is imported from some module
        if (TryGetImport(name, out var sv1, out var moduleHandle))
        {
            if ((sv1.Data & VarFlags.Private) == VarFlags.Private)
            {
                var = ScopeVar.Empty;
                return CompilerError.PrivateNameAccess;
            }

            var = new(moduleHandle | (sv1.Address >> 8) << 8, sv1.Data | VarFlags.External);
            return CompilerError.None;
        }

        var = ScopeVar.Empty;
        return CompilerError.UndefinedVariable;
    }

    private bool TryGetImport(string name, out ScopeVar sv, out int moduleHandle)
    {
        if (importedSymbols.TryGetValue(name, out var imported))
        {
            sv = imported.Variable;
            moduleHandle = imported.Handle;
            return true;
        }

        moduleHandle = default;
        sv = default;
        return false;
    }
}

partial class Builder
{
    private Reference CreateImportReference(LoweredImport node)
    {
        var localPath = node.LocalPath;

        if ((localPath is not null && localPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            || node.ModuleName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            AddError(CompilerError.DllImportNotAllowed, node.Location);
            throw new TerminationException();
        }

        return new Reference(Guid.NewGuid(), node.ModuleName, localPath, null, node.Location, unit.FileName);
    }

    private int AddUnitReference(Reference reference, Unit linkedUnit)
    {
        reference.Checksum = linkedUnit.Checksum;
        var moduleIndex = unit.UnitIds.Count;
        unit.References.Add(reference);
        unit.UnitIds.Add(-1);
        return moduleIndex;
    }

}
