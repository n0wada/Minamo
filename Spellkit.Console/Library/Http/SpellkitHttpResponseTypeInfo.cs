using Spellkit.Codegen;
using Spellkit.Library.Binary;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Text;
using System.Text.Json;

namespace Spellkit.Library.Http;

[SpellkitType]
public sealed partial class SpellkitHttpResponseTypeInfo : SpellkitForeignTypeInfo
{
    public override string ReflectedTypeName => "Response";

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        SpellkitString.Get(arg.ToString());

    [SpellkitProperty("statusCode")]
    internal static int StatusCode(SpellkitHttpResponse self) => self.StatusCode;

    [SpellkitProperty("ok")]
    internal static bool Ok(SpellkitHttpResponse self) => self.Ok;

    [SpellkitProperty("reason")]
    internal static string Reason(SpellkitHttpResponse self) => self.Reason;

    [SpellkitProperty("url")]
    internal static string Url(SpellkitHttpResponse self) => self.Url?.ToString() ?? "";

    [SpellkitProperty("headers")]
    internal static SpellkitObject Headers(SpellkitHttpResponse self) => TypeConverter.ConvertFrom(self.Headers);

    [SpellkitProperty("text")]
    internal static string Text(SpellkitHttpResponse self) => Encoding.UTF8.GetString(self.Content);

    [SpellkitProperty("content")]
    internal static SpellkitObject Content(ExecutionContext ctx, SpellkitHttpResponse self) =>
        ctx.Type<SpellkitByteArrayTypeInfo>().Create(self.Content.ToArray());

    [SpellkitMethod("json")]
    internal static SpellkitObject Json(ExecutionContext ctx, SpellkitHttpResponse self)
    {
        try
        {
            using var document = JsonDocument.Parse(self.Content);
            return SpellkitHttp.JsonToSpellkit(document.RootElement);
        }
        catch (JsonException)
        {
            return ctx.ParsingFailed();
        }
    }

    [SpellkitMethod("raiseForStatus")]
    internal static SpellkitObject RaiseForStatus(ExecutionContext ctx, SpellkitHttpResponse self)
    {
        return self.Ok
            ? self
            : ctx.IOFailed($"HTTP {self.StatusCode} {self.Reason}".TrimEnd());
    }

    [SpellkitMethod("save")]
    internal static SpellkitObject Save(ExecutionContext ctx, SpellkitHttpResponse self, string path)
    {
        try
        {
            File.WriteAllBytes(path, self.Content);
            return self;
        }
        catch (Exception ex)
        {
            return ctx.IOFailed(ex.Message);
        }
    }
}
