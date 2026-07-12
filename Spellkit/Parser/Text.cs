using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Spellkit.Parser.Model;
using Spellkit.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Spellkit.Parser;

public abstract class SourceBuffer
{
    public abstract string? FileName { get; }

    protected internal abstract int Pos { get; set; }

    protected internal abstract int Read();

    public static SourceBuffer FromFile(string file)
    {
        using var sr = new StreamReader(File.OpenRead(file));
        return new StringBuffer(sr.ReadToEnd(), file);
    }

    public static async Task<SourceBuffer> FromFileAsync(
        string file,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        var source = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        return new StringBuffer(source, file);
    }

    public static SourceBuffer FromString(string str, string? file = null) =>
        new StringBuffer(str, file ?? "<memory>");
}

internal sealed class StringBuffer : SourceBuffer
{
    private readonly char[] buffer;
    private readonly int bufferLen;
    private int bufferPosition;

    public StringBuffer(string value)
    {
        buffer = value.ToCharArray();
        bufferLen = buffer.Length;
    }

    public StringBuffer(string value, string fileName) : this(value) =>
        FileName = fileName.Replace('\\', '/');

    protected internal override int Read() =>
        bufferPosition < bufferLen ? buffer[bufferPosition++] : Constants.EOF;

    public override string? FileName { get; }

    protected internal override int Pos
    {
        get => bufferPosition;
        set
        {
            if (value < 0 || value > bufferLen)
            {
                throw new SpkException($"End of file, position: {value}.", null);
            }

            bufferPosition = value;
        }
    }
}

public static class StringUtil
{
    private static readonly Dictionary<string, string> replaceDict =
        new()
        {
            { "\a", @"\a" },
            { "\b", @"\b" },
            { "\f", @"\f" },
            { "\n", @"\n" },
            { "\r", @"\r" },
            { "\t", @"\t" },
            { "\v", @"\v" },
            { "\\", @"\\" },
            { "\0", @"\0" },
            { "\"", @"\""" }
        };

    private const string regexEscapes = @"[\a\b\f\n\r\t\v\\""]";

    public static string Escape(string value, string quote = "\"") =>
        quote + Regex.Replace(value, regexEscapes, Match) + quote;

    private static string Match(Match m)
    {
        var match = m.ToString();

        if (replaceDict.ContainsKey(match))
        {
            return replaceDict[match];
        }

        return string.Empty;
    }
}

internal static class EscapeCodeParser
{
    public static bool Parse(string? fileName, Location loc, string str, List<BuildMessage> messages, out string? value)
    {
        if (str is null || str.Length < 4)
        {
            value = str?[1..^1];
            return true;
        }

        var buffer = str.ToCharArray(1, str.Length - 2);
        var len = buffer.Length;
        var sb = new StringBuilder(str.Length);
        value = null;

        for (var i = 0; i < len; i++)
        {
            var c = buffer[i];

            if (c == '\\')
            {
                i++;

                if (i < len)
                {
                    var cn = buffer[i];

                    switch (cn)
                    {
                        case 's':
                            sb.Append('\u0020');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case '"':
                            sb.Append('"');
                            break;
                        case '\'':
                            sb.Append('\'');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case '0':
                            sb.Append('\0');
                            break;
                        case 'u':
                            {
                                if (i + 3 < len)
                                {
                                    var ns = new string(buffer, i + 1, 4);

                                    if (ns[0] == ' ' || ns[0] == '\t' || ns[3] == ' ' || ns[3] == '\t')
                                    {
                                        return InvalidLiteral(messages, loc, fileName, i);
                                    }

                                    if (!int.TryParse(ns, NumberStyles.HexNumber, InvariantCulture.NumberFormat, out var ci))
                                    {
                                        return InvalidLiteral(messages, loc, fileName, i);
                                    }

                                    sb.Append((char)ci);
                                    i += 4;
                                }
                                else
                                {
                                    return InvalidLiteral(messages, loc, fileName, i);
                                }
                            }
                            break;
                        default:
                            return InvalidLiteral(messages, loc, fileName, i);
                    }
                }
                else
                {
                    return InvalidLiteral(messages, loc, fileName, i);
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        value = sb.ToString();
        return true;
    }

    private static bool InvalidLiteral(List<BuildMessage> messages, Location baseLocation, string? fileName, int offset)
    {
        messages.Add(new BuildMessage(
            MessageCatalog.Get(MessageGroup.Parser, nameof(ParserError.InvalidEscapeCode)),
            BuildMessageType.Error,
            (int)ParserError.InvalidEscapeCode,
            baseLocation.Line,
            baseLocation.Column + offset,
            fileName));
        return false;
    }
}
