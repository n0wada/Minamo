using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.IO;
using System.Text;

namespace Spellkit;

public sealed class ConsoleTextReader : TextReader
{
    private readonly SpellkitObject read;
    private readonly SpellkitObject readLine;
    private readonly ExecutionContext ctx;

    public ConsoleTextReader(ExecutionContext ctx, SpellkitObject read, SpellkitObject readLine) =>
        (this.ctx, this.read, this.readLine) = (ctx, read, readLine);

    public override int Read()
    {
        var ret = read.Invoke(ctx);

        if (ret is SpellkitInteger i)
        {
            return (int)i.Value;
        }
        else if (ret is SpellkitChar c)
        {
            return c.Value;
        }
        else
        {
            ctx.InvalidType(SpellkitTypeCodes.Integer, SpellkitTypeCodes.Char, ret);
            return 0;
        }
    }

    public override string? ReadLine()
    {
        var ret = readLine.Invoke(ctx);

        if (ret is SpellkitString s)
        {
            return s.Value;
        }
        else
        {
            var str = ret.ToString(ctx);

            if (ctx.HasErrors)
            {
                return null;
            }

            return str.Value;
        }
    }
}

public sealed class ConsoleTextWriter : TextWriter
{
    private readonly SpellkitObject write;
    private readonly SpellkitObject? writeLine;
    private readonly ExecutionContext ctx;

    public override Encoding Encoding => Encoding.UTF8;

    public ConsoleTextWriter(ExecutionContext ctx, SpellkitObject write) : this(ctx, write, null) { }

    public ConsoleTextWriter(ExecutionContext ctx, SpellkitObject write, SpellkitObject? writeLine) =>
        (this.ctx, this.write, this.writeLine) = (ctx, write, writeLine);

    public override void Write(string? value) => write.Invoke(ctx, SpellkitString.Get(value));

    public override void WriteLine(string? value) => (writeLine ?? write).Invoke(ctx, SpellkitString.Get(value));
}
