using Spellkit.Parser.Model;
using System;

namespace Spellkit.Parser;

public static class SpellkitParser
{
    private const string DefaultBuffer = "<stdin>";

    public static Result<SpellkitCodeModel> Parse(string source, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Parse(SourceBuffer.FromString(source, sourceName));
    }

    public static Result<SpellkitCodeModel> ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(SourceBuffer.FromFile(path));
    }

    public static Result<SpellkitCodeModel> Parse(SourceBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
#if DEBUG
        ParserSelfTest.Verify();
#endif
        var fileName = buffer.FileName ?? DefaultBuffer;
        return new HandwrittenParser(fileName, new Lexer(buffer)).Parse();
    }
}

internal static class ParserSelfTest
{
    private static readonly System.Threading.Lock SyncRoot = new();
    private static bool verified;

    public static void Verify()
    {
        if (verified)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (verified)
            {
                return;
            }

            VerifyCursor();
            VerifyDiagnostics();
            VerifyGrammarAndMetadata();
            verified = true;
        }
    }

    private static void VerifyCursor()
    {
        var cursor = new TokenCursor(new Lexer(SourceBuffer.FromString("first\nsecond")));
        var mark = cursor.Mark();
        Require(cursor.Current.Text == "first", "Token cursor returned an unexpected token.");
        Require(cursor.Peek(1).AfterEol, "Token cursor lost end-of-line context.");
        cursor.Consume();
        cursor.Reset(mark);
        Require(cursor.Current.Text == "first", "Token cursor reset failed.");
    }

    private static void VerifyDiagnostics()
    {
        var separators = CreateParser("first\nsecond third fourth");
        separators.Consume();
        Require(separators.ExpectSeparator(), "A newline should separate statements.");
        separators.Consume();
        Require(!separators.ExpectSeparator() && separators.Errors.Count == 1,
            "A missing separator should produce one diagnostic.");
        separators.ReportExpected(TokenKind.RightBrace);
        Require(separators.Errors.Count == 1, "Adjacent diagnostics should be suppressed.");

        Require(!Parse("let = 1").Success, "An invalid binding should fail parsing.");
        Require(!Parse("func broken( { 1 }").Success, "An unclosed signature should fail parsing.");
        Require(!Parse("let text = \"unterminated").Success,
            "An unterminated string should fail parsing.");
        Require(!Parse("/* unterminated").Success,
            "An unterminated block comment should fail parsing.");
        Require(!Parse("private import text").Success,
            "The removed private import syntax should fail parsing.");
    }

    private static void VerifyGrammarAndMetadata()
    {
        const string source = """
            import text/format as format
            const Answer = 42
            struct Box { mut value }
            enum Maybe { None, Some(value) }
            trait Renderable { func Text(): String }
            impl Box with Renderable { mut cache = nil
                func init(value) { this.cache = value }
                func Text() => this.cache.ToString() }
            func values() { for value in Iterator.Range(1, 10, 2) { yield value } }
            let lambda = value => match value { Some(x) when x > 0 => x, _ => 0 }
            """;
        var result = Parse(source);
        Require(result.Success, "The parser rejected its grammar smoke test.");
        var model = result.Value ?? throw new InvalidOperationException("The parser returned no model.");
        var structure = (TypeDeclarationSyntax)model.Root.Nodes[1];
        var iterator = (FunctionDeclarationSyntax)model.Root.Nodes[5];
        var binding = (BindingSyntax)model.Root.Nodes[6];

        Require(structure.Constructors[0].IsConstructor
            && ((TypeParameterSyntax)structure.Constructors[0].Parameters[0]).Mutable,
            "Struct constructor metadata is incomplete.");
        Require(iterator.IsIterator, "Yield did not mark its function as an iterator.");
        Require(binding.Init is FunctionDeclarationSyntax { Name: null },
            "A lambda lost its expression identity.");
    }

    private static HandwrittenParser CreateParser(string source) =>
        new("<parser-self-test>", new Lexer(SourceBuffer.FromString(source)));

    private static Result<SpellkitCodeModel> Parse(string source) => CreateParser(source).Parse();

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
