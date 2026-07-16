using Spellkit.Codegen;
using Spellkit.Library.Binary;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Text;
using System.Text.Json;

namespace Spellkit.Library.Http;

[SpkType]
public sealed partial class SpkHttpResponseTypeInfo : SpkForeignTypeInfo
{
    public override string ReflectedTypeName => "Response";

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format) =>
        SpkString.Get(arg.ToString());

    [SpkProperty("statusCode")]
    internal static int StatusCode(SpkHttpResponse self) => self.StatusCode;

    [SpkProperty("ok")]
    internal static bool Ok(SpkHttpResponse self) => self.Ok;

    [SpkProperty("reason")]
    internal static string Reason(SpkHttpResponse self) => self.Reason;

    [SpkProperty("url")]
    internal static string Url(SpkHttpResponse self) => self.Url?.ToString() ?? "";

    [SpkProperty("headers")]
    internal static SpkObject Headers(SpkHttpResponse self) => TypeConverter.ConvertFrom(self.Headers);

    [SpkProperty("text")]
    internal static string Text(SpkHttpResponse self) => Encoding.UTF8.GetString(self.Content);

    [SpkProperty("content")]
    internal static SpkObject Content(ExecutionContext ctx, SpkHttpResponse self) =>
        ctx.Type<SpkByteArrayTypeInfo>().Create(self.Content.ToArray());

    [SpkMethod("json")]
    internal static SpkObject Json(ExecutionContext ctx, SpkHttpResponse self)
    {
        try
        {
            using var document = JsonDocument.Parse(self.Content);
            return SpkHttp.JsonToSpk(document.RootElement);
        }
        catch (JsonException)
        {
            return ctx.ParsingFailed();
        }
    }

    [SpkMethod("raiseForStatus")]
    internal static SpkObject RaiseForStatus(ExecutionContext ctx, SpkHttpResponse self)
    {
        return self.Ok
            ? self
            : ctx.IOFailed($"HTTP {self.StatusCode} {self.Reason}".TrimEnd());
    }

    [SpkMethod("save")]
    internal static SpkObject Save(ExecutionContext ctx, SpkHttpResponse self, string path)
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
