using Spellkit.Codegen;
using Spellkit.Library.Binary;
using Spellkit.Library.Json;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Text;

namespace Spellkit.Library.Http;

[SpellkitType]
public sealed partial class SpellkitHttpResponseTypeInfo : SpellkitForeignTypeInfo
{
    public override string ReflectedTypeName => "Response";

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        SpellkitString.Get(arg.ToString());

    [SpellkitProperty]
    internal static int StatusCode(SpellkitHttpResponse self) => self.StatusCode;

    [SpellkitProperty]
    internal static bool Ok(SpellkitHttpResponse self) => self.Ok;

    [SpellkitProperty]
    internal static string Reason(SpellkitHttpResponse self) => self.Reason;

    [SpellkitProperty]
    internal static string Url(SpellkitHttpResponse self) => self.Url?.ToString() ?? "";

    [SpellkitProperty]
    internal static SpellkitObject Headers(SpellkitHttpResponse self) => TypeConverter.ConvertFrom(self.Headers);

    [SpellkitProperty]
    internal static string Text(SpellkitHttpResponse self) => Encoding.UTF8.GetString(self.Content);

    [SpellkitProperty]
    internal static SpellkitObject Content(ExecutionContext ctx, SpellkitHttpResponse self) =>
        ctx.Type<SpellkitByteArrayTypeInfo>().Create(self.Content.ToArray());

    [SpellkitMethod]
    internal static SpellkitObject Json(ExecutionContext ctx, SpellkitHttpResponse self) =>
        SpellkitJson.Parse(ctx, self.Content);

    [SpellkitMethod]
    internal static SpellkitObject RaiseForStatus(ExecutionContext ctx, SpellkitHttpResponse self)
    {
        return self.Ok
            ? self
            : ctx.IOFailed($"HTTP {self.StatusCode} {self.Reason}".TrimEnd());
    }

    [SpellkitMethod]
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
