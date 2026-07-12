using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.IO;
using System.Text;

namespace Spellkit;

public sealed class ConsoleTextReader : TextReader
{
    private readonly SpkObject read;
    private readonly SpkObject readLine;
    private readonly ExecutionContext ctx;

    public ConsoleTextReader(ExecutionContext ctx, SpkObject read, SpkObject readLine) =>
        (this.ctx, this.read, this.readLine) = (ctx, read, readLine);

    public override int Read()
    {
        var ret = read.Invoke(ctx);

        if (ret is SpkInteger i)
        {
            return (int)i.Value;
        }
        else if (ret is SpkChar c)
        {
            return c.Value;
        }
        else
        {
            ctx.InvalidType(Spk.Integer, Spk.Char, ret);
            return 0;
        }
    }

    public override string? ReadLine()
    {
        var ret = readLine.Invoke(ctx);

        if (ret is SpkString s)
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
    private readonly SpkObject write;
    private readonly SpkObject? writeLine;
    private readonly ExecutionContext ctx;

    public override Encoding Encoding => Encoding.UTF8;

    public ConsoleTextWriter(ExecutionContext ctx, SpkObject write) : this(ctx, write, null) { }

    public ConsoleTextWriter(ExecutionContext ctx, SpkObject write, SpkObject? writeLine) =>
        (this.ctx, this.write, this.writeLine) = (ctx, write, writeLine);

    public override void Write(string? value) => write.Invoke(ctx, SpkString.Get(value));

    public override void WriteLine(string? value) => (writeLine ?? write).Invoke(ctx, SpkString.Get(value));
}
