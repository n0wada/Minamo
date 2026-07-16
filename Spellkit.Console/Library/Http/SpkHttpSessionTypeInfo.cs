using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Http;

[SpkType]
public sealed partial class SpkHttpSessionTypeInfo : SpkForeignTypeInfo
{
    public override string ReflectedTypeName => "Session";

    [SpkMethod("Request")]
    internal static SpkObject Request(
        ExecutionContext ctx,
        SpkHttpSession self,
        string method,
        string url,
        [ParameterName("params")] [Default] SpkObject queryParams,
        [Default] SpkObject headers,
        [Default] SpkObject json,
        [Default] SpkObject data,
        double? timeout = null,
        [Default] SpkObject auth = null!,
        [Default] SpkObject allowRedirects = null!) =>
        SpkHttp.Request(ctx, method, url, queryParams, headers, json, data, timeout, auth, SpkHttp.OptionalBool(allowRedirects), self);

    [SpkMethod("Get")]
    internal static SpkObject Get(
        ExecutionContext ctx,
        SpkHttpSession self,
        string url,
        [ParameterName("params")] [Default] SpkObject queryParams,
        [Default] SpkObject headers,
        double? timeout = null,
        [Default] SpkObject auth = null!,
        [Default] SpkObject allowRedirects = null!) =>
        SpkHttp.Request(ctx, "GET", url, queryParams, headers, null, null, timeout, auth, SpkHttp.OptionalBool(allowRedirects), self);

    [SpkMethod("Post")]
    internal static SpkObject Post(
        ExecutionContext ctx,
        SpkHttpSession self,
        string url,
        [ParameterName("params")] [Default] SpkObject queryParams,
        [Default] SpkObject headers,
        [Default] SpkObject json,
        [Default] SpkObject data,
        double? timeout = null,
        [Default] SpkObject auth = null!,
        [Default] SpkObject allowRedirects = null!) =>
        SpkHttp.Request(ctx, "POST", url, queryParams, headers, json, data, timeout, auth, SpkHttp.OptionalBool(allowRedirects), self);

    [SpkMethod("Put")]
    internal static SpkObject Put(
        ExecutionContext ctx,
        SpkHttpSession self,
        string url,
        [ParameterName("params")] [Default] SpkObject queryParams,
        [Default] SpkObject headers,
        [Default] SpkObject json,
        [Default] SpkObject data,
        double? timeout = null,
        [Default] SpkObject auth = null!,
        [Default] SpkObject allowRedirects = null!) =>
        SpkHttp.Request(ctx, "PUT", url, queryParams, headers, json, data, timeout, auth, SpkHttp.OptionalBool(allowRedirects), self);

    [SpkMethod("Patch")]
    internal static SpkObject Patch(
        ExecutionContext ctx,
        SpkHttpSession self,
        string url,
        [ParameterName("params")] [Default] SpkObject queryParams,
        [Default] SpkObject headers,
        [Default] SpkObject json,
        [Default] SpkObject data,
        double? timeout = null,
        [Default] SpkObject auth = null!,
        [Default] SpkObject allowRedirects = null!) =>
        SpkHttp.Request(ctx, "PATCH", url, queryParams, headers, json, data, timeout, auth, SpkHttp.OptionalBool(allowRedirects), self);

    [SpkMethod("Delete")]
    internal static SpkObject Delete(
        ExecutionContext ctx,
        SpkHttpSession self,
        string url,
        [ParameterName("params")] [Default] SpkObject queryParams,
        [Default] SpkObject headers,
        double? timeout = null,
        [Default] SpkObject auth = null!,
        [Default] SpkObject allowRedirects = null!) =>
        SpkHttp.Request(ctx, "DELETE", url, queryParams, headers, null, null, timeout, auth, SpkHttp.OptionalBool(allowRedirects), self);

    [SpkStaticMethod("Session")]
    internal static SpkObject New(
        ExecutionContext ctx,
        string? baseUrl = null,
        [Default] SpkObject headers = null!,
        [Default] SpkObject auth = null!,
        double? timeout = null,
        [Default] SpkObject allowRedirects = null!) =>
        SpkHttp.CreateSession(ctx, baseUrl, headers, auth, timeout, SpkHttp.OptionalBool(allowRedirects));
}
