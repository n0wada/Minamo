using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Http;

[SpellkitType]
public sealed partial class SpellkitHttpSessionTypeInfo : SpellkitForeignTypeInfo
{
    public override string ReflectedTypeName => "Session";

    [SpellkitMethod("Request")]
    internal static SpellkitObject Request(
        ExecutionContext ctx,
        SpellkitHttpSession self,
        string method,
        string url,
        [ParameterName("params")] [Default] SpellkitObject queryParams,
        [Default] SpellkitObject headers,
        [Default] SpellkitObject json,
        [Default] SpellkitObject data,
        double? timeout = null,
        [Default] SpellkitObject auth = null!,
        [Default] SpellkitObject allowRedirects = null!) =>
        SpellkitHttp.Request(ctx, method, url, queryParams, headers, json, data, timeout, auth, SpellkitHttp.OptionalBool(allowRedirects), self);

    [SpellkitMethod("Get")]
    internal static SpellkitObject Get(
        ExecutionContext ctx,
        SpellkitHttpSession self,
        string url,
        [ParameterName("params")] [Default] SpellkitObject queryParams,
        [Default] SpellkitObject headers,
        double? timeout = null,
        [Default] SpellkitObject auth = null!,
        [Default] SpellkitObject allowRedirects = null!) =>
        SpellkitHttp.Request(ctx, "GET", url, queryParams, headers, null, null, timeout, auth, SpellkitHttp.OptionalBool(allowRedirects), self);

    [SpellkitMethod("Post")]
    internal static SpellkitObject Post(
        ExecutionContext ctx,
        SpellkitHttpSession self,
        string url,
        [ParameterName("params")] [Default] SpellkitObject queryParams,
        [Default] SpellkitObject headers,
        [Default] SpellkitObject json,
        [Default] SpellkitObject data,
        double? timeout = null,
        [Default] SpellkitObject auth = null!,
        [Default] SpellkitObject allowRedirects = null!) =>
        SpellkitHttp.Request(ctx, "POST", url, queryParams, headers, json, data, timeout, auth, SpellkitHttp.OptionalBool(allowRedirects), self);

    [SpellkitMethod("Put")]
    internal static SpellkitObject Put(
        ExecutionContext ctx,
        SpellkitHttpSession self,
        string url,
        [ParameterName("params")] [Default] SpellkitObject queryParams,
        [Default] SpellkitObject headers,
        [Default] SpellkitObject json,
        [Default] SpellkitObject data,
        double? timeout = null,
        [Default] SpellkitObject auth = null!,
        [Default] SpellkitObject allowRedirects = null!) =>
        SpellkitHttp.Request(ctx, "PUT", url, queryParams, headers, json, data, timeout, auth, SpellkitHttp.OptionalBool(allowRedirects), self);

    [SpellkitMethod("Patch")]
    internal static SpellkitObject Patch(
        ExecutionContext ctx,
        SpellkitHttpSession self,
        string url,
        [ParameterName("params")] [Default] SpellkitObject queryParams,
        [Default] SpellkitObject headers,
        [Default] SpellkitObject json,
        [Default] SpellkitObject data,
        double? timeout = null,
        [Default] SpellkitObject auth = null!,
        [Default] SpellkitObject allowRedirects = null!) =>
        SpellkitHttp.Request(ctx, "PATCH", url, queryParams, headers, json, data, timeout, auth, SpellkitHttp.OptionalBool(allowRedirects), self);

    [SpellkitMethod("Delete")]
    internal static SpellkitObject Delete(
        ExecutionContext ctx,
        SpellkitHttpSession self,
        string url,
        [ParameterName("params")] [Default] SpellkitObject queryParams,
        [Default] SpellkitObject headers,
        double? timeout = null,
        [Default] SpellkitObject auth = null!,
        [Default] SpellkitObject allowRedirects = null!) =>
        SpellkitHttp.Request(ctx, "DELETE", url, queryParams, headers, null, null, timeout, auth, SpellkitHttp.OptionalBool(allowRedirects), self);

    [SpellkitStaticMethod("Session")]
    internal static SpellkitObject New(
        ExecutionContext ctx,
        string? baseUrl = null,
        [Default] SpellkitObject headers = null!,
        [Default] SpellkitObject auth = null!,
        double? timeout = null,
        [Default] SpellkitObject allowRedirects = null!) =>
        SpellkitHttp.CreateSession(ctx, baseUrl, headers, auth, timeout, SpellkitHttp.OptionalBool(allowRedirects));
}
