using System.Text;
using System;
using Microsoft.CodeAnalysis.Text;

namespace Spellkit.Generators;

internal sealed class CodeWriter
{
    private readonly StringBuilder builder = new();
    private int padding;

    public void Indent() => padding += 4;
    public void Outdent() => padding -= 4;

    public void StartBlock()
    {
        AppendLine("{");
        Indent();
    }
    public void EndBlock(string suffix = "")
    {
        Outdent();
        AppendLine("}" + suffix);
    }

    public void AppendPadding() => builder.Append(new string(' ', padding));
    public void Append(string value) => builder.Append(value);
    public void AppendLine(string value = "")
    {
        if (value.Length == 0)
        {
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine(new string(' ', padding) + value);
        }
    }
    public void AppendInBlock(string value)
    {
        AppendLine("{");
        Indent();
        AppendLine(value);
        Outdent();
        AppendLine("}");
    }
    public void Block(string declaration, Action<CodeWriter> writeBody)
    {
        AppendLine(declaration);
        StartBlock();
        writeBody(this);
        EndBlock();
    }
    public SourceText ToSourceText() => SourceText.From(builder.ToString(), Encoding.UTF8);
    public override string ToString() => builder.ToString();
}
