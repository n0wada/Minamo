using Spellkit.Hosting;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Http;

[SpellkitModule("http")]
[SpellkitForeignType(typeof(SpellkitHttpResponseTypeInfo))]
[SpellkitForeignType(typeof(SpellkitHttpSessionTypeInfo))]
public static class HttpModule
{
    [SpellkitCommand("Request")]
    internal static SpellkitObject Request(
        SpellkitCommandContext host,
        string method,
        string url,
        SpellkitObject @params = null!,
        SpellkitObject headers = null!,
        SpellkitObject json = null!,
        SpellkitObject data = null!,
        double? timeout = null,
        SpellkitObject auth = null!,
        bool? allowRedirects = null) =>
        SpellkitHttp.Request(host.ExecutionContext, method, url, @params, headers, json, data, timeout, auth, allowRedirects);

    [SpellkitCommand("Get")]
    internal static SpellkitObject Get(
        SpellkitCommandContext host,
        string url,
        SpellkitObject @params = null!,
        SpellkitObject headers = null!,
        double? timeout = null,
        SpellkitObject auth = null!,
        bool? allowRedirects = null) =>
        SpellkitHttp.Request(host.ExecutionContext, "GET", url, @params, headers, null, null, timeout, auth, allowRedirects);

    [SpellkitCommand("Post")]
    internal static SpellkitObject Post(
        SpellkitCommandContext host,
        string url,
        SpellkitObject @params = null!,
        SpellkitObject headers = null!,
        SpellkitObject json = null!,
        SpellkitObject data = null!,
        double? timeout = null,
        SpellkitObject auth = null!,
        bool? allowRedirects = null) =>
        SpellkitHttp.Request(host.ExecutionContext, "POST", url, @params, headers, json, data, timeout, auth, allowRedirects);

    [SpellkitCommand("Put")]
    internal static SpellkitObject Put(
        SpellkitCommandContext host,
        string url,
        SpellkitObject @params = null!,
        SpellkitObject headers = null!,
        SpellkitObject json = null!,
        SpellkitObject data = null!,
        double? timeout = null,
        SpellkitObject auth = null!,
        bool? allowRedirects = null) =>
        SpellkitHttp.Request(host.ExecutionContext, "PUT", url, @params, headers, json, data, timeout, auth, allowRedirects);

    [SpellkitCommand("Patch")]
    internal static SpellkitObject Patch(
        SpellkitCommandContext host,
        string url,
        SpellkitObject @params = null!,
        SpellkitObject headers = null!,
        SpellkitObject json = null!,
        SpellkitObject data = null!,
        double? timeout = null,
        SpellkitObject auth = null!,
        bool? allowRedirects = null) =>
        SpellkitHttp.Request(host.ExecutionContext, "PATCH", url, @params, headers, json, data, timeout, auth, allowRedirects);

    [SpellkitCommand("Delete")]
    internal static SpellkitObject Delete(
        SpellkitCommandContext host,
        string url,
        SpellkitObject @params = null!,
        SpellkitObject headers = null!,
        double? timeout = null,
        SpellkitObject auth = null!,
        bool? allowRedirects = null) =>
        SpellkitHttp.Request(host.ExecutionContext, "DELETE", url, @params, headers, null, null, timeout, auth, allowRedirects);

    [SpellkitCommand("Session")]
    internal static SpellkitObject Session(
        SpellkitCommandContext host,
        string? baseUrl = null,
        SpellkitObject headers = null!,
        SpellkitObject auth = null!,
        double? timeout = null,
        bool? allowRedirects = null) =>
        SpellkitHttp.CreateSession(host.ExecutionContext, baseUrl, headers, auth, timeout, allowRedirects);
}
