using Spellkit.Compiler;
using Spellkit.Debug;
using Spellkit.Parser;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Spellkit;

internal static class BytecodeFormatter
{
    private static readonly char[] cz = new char[] { '\\', '/' };

    public static string Format(IEnumerable<Unit> units)
    {
        var builder = new StringBuilder();

        foreach (var u in units)
        {
            var sb = new StringBuilder();
            Format(sb, u);

            if (sb.Length > 0)
            {
                builder.AppendLine();

                if (u.FileName is not null)
                {
                    if (u.FileName.IndexOfAny(cz) == -1)
                    {
                        builder.AppendLine($"{u.FileName} (size {u.Ops.Count}):");
                    }
                    else
                    {
                        var fi = new FileInfo(u.FileName);
                        builder.AppendLine($"{fi.Directory?.Name}/{fi.Name} (size {u.Ops.Count}):");
                    }
                }
                else
                {
                    builder.AppendLine($"Size {u.Ops.Count}:");
                }

                builder.Append(sb);
            }
        }

        return builder.ToString();
    }

    public static string Format(Unit unit)
    {
        var sb = new StringBuilder();
        Format(sb, unit);
        return sb.ToString();
    }

    private static bool TryGetVarSym(int offset, Unit unit, Op op, bool loc, out VarSym? vs)
    {
        vs = null;

        if (unit.Symbols is not null)
        {
            var scopeIndex = 0;

            if (!loc)
            {
                var sym = unit.Symbols.FindScopeSym(offset);

                if (sym is null)
                {
                    return false;
                }

                scopeIndex = loc ? 0 : sym.Index - (op.Data >> 8);
            }

            vs = unit.Symbols.FindVarSym(op.Data & byte.MaxValue, scopeIndex);
            return vs is not null;
        }

        return false;
    }

    private static string GetFunName(FunSym funSym)
    {
        if (funSym.Name is not null && funSym.TypeName is not null)
        {
            return $"{funSym.TypeName}.{funSym.Name}";
        }
        else if (funSym.Name is not null)
        {
            return funSym.Name;
        }
        else
        {
            return "lambda@" + funSym.Handle;
        }
    }

    private static string GetFunSym(Unit unit, Op op)
    {
        var fs = unit.Symbols.Functions[op.Data];
        return GetFunName(fs);
    }

    private static string FormatOffset(int offset) => offset.ToString().PadLeft(5, '0');

    private static void Format(StringBuilder sb, Unit unit)
    {
        var funs = new Stack<FunSym>();

        for (var i = 0; i < unit.Ops.Count; i++)
        {
            var op = unit.Ops[i];
            sb.Append(FormatOffset(i));
            sb.Append(": ");
            sb.Append(op.Code.ToString());

            switch (op.Code)
            {
                case OpCode.LoadConst:
                    {
                        var obj = unit.Objects[op.Data];

                        if (obj.TypeId is SpellkitTypeCodes.String)
                        {
                            sb.Append($" {StringUtil.Escape(obj.ToString())}");
                        }
                        else if (obj.TypeId is SpellkitTypeCodes.Char)
                        {
                            sb.Append($" {StringUtil.Escape(obj.ToString(), "'")}");
                        }
                        else
                        {
                            sb.Append($" {obj.ToObject()}");
                        }
                    }
                    break;
                case OpCode.Jump:
                case OpCode.JumpIfTrue:
                case OpCode.JumpIfTerminator:
                case OpCode.JumpIfFalse:
                case OpCode.EnterTry:
                    sb.Append(" " + FormatOffset(op.Data));
                    break;
                case OpCode.LoadExternal:
                case OpCode.LoadType:
                case OpCode.LoadModule:
                case OpCode.CreateIterator:
                    sb.Append(" " + op.Data);
                    break;
                case OpCode.PrepareCall:
                case OpCode.SetCallArgument:
                case OpCode.InvokePreparedCall:
                case OpCode.Call:
                case OpCode.CreateTuple:
                case OpCode.TailCall:
                case OpCode.CreateArguments:
                case OpCode.CreateDictionary:
                    sb.Append(" " + op.Data);
                    break;
                case OpCode.SetFunctionAttribute:
                    if ((op.Data & 0x01) == 0x01)
                    {
                        sb.Append(" Auto");
                    }

                    if ((op.Data & 0x02) == 0x02)
                    {
                        sb.Append(" Variadic");
                    }

                    if ((op.Data & 0x04) == 0x04)
                    {
                        sb.Append(" Final");
                    }

                    break;
                case OpCode.StoreLocal:
                case OpCode.LoadLocal:
                    {
                        if (TryGetVarSym(i, unit, op, true, out var vs))
                        {
                            sb.Append(" " + vs!.Name);
                        }
                        else
                        {
                            sb.Append(" #" + op.Data);
                        }
                    }
                    break;
                case OpCode.StoreCaptured:
                case OpCode.LoadCaptured:
                    {
                        if (TryGetVarSym(i, unit, op, false, out var vs))
                        {
                            sb.Append(" " + vs!.Name);
                        }
                        else
                        {
                            sb.Append(" #" + op.Data);
                        }
                    }
                    break;
                case OpCode.CreateFunction:
                    sb.Append(" " + GetFunSym(unit, op));
                    break;
                case OpCode.CreateVariadicFunction:
                    sb.Append(" " + GetFunSym(unit, op));
                    sb.Append(" varargs:" + op.Data2);
                    break;
                case OpCode.StoreStaticMember:
                case OpCode.StoreMember:
                case OpCode.LoadMember:
                case OpCode.HasMember:
                case OpCode.CreateLabel:
                case OpCode.SetNamedCallArgument:
                case OpCode.CreateObject:
                case OpCode.CreateType:
                case OpCode.CheckConstructor:
                case OpCode.LoadPrivateMember:
                case OpCode.StorePrivateMember:
                    sb.Append(" " + StringUtil.Escape((string)unit.Strings[op.Data]));
                    break;
                case OpCode.CallMember:
                case OpCode.CallStatic:
                    sb.Append(" " + StringUtil.Escape((string)unit.Strings[op.Data]));
                    sb.Append(" args:" + op.Data2);
                    break;
                case OpCode.CheckContains:
                    sb.Append(" " + StringUtil.Escape(unit.Objects[op.Data].ToObject().ToString() ?? ""));
                    break;
            }

            var funSym = unit.Symbols?.FindFunSymByStart(i - 1);

            if (funSym is not null)
            {
                sb.Append($" //Start of {GetFunName(funSym)}");
                funs.Push(funSym);
            }
            else if (funs.Count > 0 && funs.Peek().EndOffset == i + 1)
            {
                    funSym = funs.Pop();
                sb.Append($" //End of {GetFunName(funSym)}");
            }

            sb.AppendLine();
        }
    }
}
