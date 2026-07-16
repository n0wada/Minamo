using Spellkit.Hosting;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Http;

[SpellkitModule("http")]
[SpellkitForeignType(typeof(SpkHttpResponseTypeInfo))]
[SpellkitForeignType(typeof(SpkHttpSessionTypeInfo))]
public static class HttpModule
{
    [SpellkitCommand("Request")]
    internal static SpkObject Request(
        SpellkitCommandContext host,
        string method,
        string url,
        SpkObject @params = null!,
        SpkObject headers = null!,
        SpkObject json = null!,
        SpkObject data = null!,
        double? timeout = null,
        SpkObject auth = null!,
        bool? allowRedirects = null) =>
        SpkHttp.Request(host.ExecutionContext, method, url, @params, headers, json, data, timeout, auth, allowRedirects);

    [SpellkitCommand("Get")]
    internal static SpkObject Get(
        SpellkitCommandContext host,
        string url,
        SpkObject @params = null!,
        SpkObject headers = null!,
        double? timeout = null,
        SpkObject auth = null!,
        bool? allowRedirects = null) =>
        SpkHttp.Request(host.ExecutionContext, "GET", url, @params, headers, null, null, timeout, auth, allowRedirects);

    [SpellkitCommand("Post")]
    internal static SpkObject Post(
        SpellkitCommandContext host,
        string url,
        SpkObject @params = null!,
        SpkObject headers = null!,
        SpkObject json = null!,
        SpkObject data = null!,
        double? timeout = null,
        SpkObject auth = null!,
        bool? allowRedirects = null) =>
        SpkHttp.Request(host.ExecutionContext, "POST", url, @params, headers, json, data, timeout, auth, allowRedirects);

    [SpellkitCommand("Put")]
    internal static SpkObject Put(
        SpellkitCommandContext host,
        string url,
        SpkObject @params = null!,
        SpkObject headers = null!,
        SpkObject json = null!,
        SpkObject data = null!,
        double? timeout = null,
        SpkObject auth = null!,
        bool? allowRedirects = null) =>
        SpkHttp.Request(host.ExecutionContext, "PUT", url, @params, headers, json, data, timeout, auth, allowRedirects);

    [SpellkitCommand("Patch")]
    internal static SpkObject Patch(
        SpellkitCommandContext host,
        string url,
        SpkObject @params = null!,
        SpkObject headers = null!,
        SpkObject json = null!,
        SpkObject data = null!,
        double? timeout = null,
        SpkObject auth = null!,
        bool? allowRedirects = null) =>
        SpkHttp.Request(host.ExecutionContext, "PATCH", url, @params, headers, json, data, timeout, auth, allowRedirects);

    [SpellkitCommand("Delete")]
    internal static SpkObject Delete(
        SpellkitCommandContext host,
        string url,
        SpkObject @params = null!,
        SpkObject headers = null!,
        double? timeout = null,
        SpkObject auth = null!,
        bool? allowRedirects = null) =>
        SpkHttp.Request(host.ExecutionContext, "DELETE", url, @params, headers, null, null, timeout, auth, allowRedirects);

    [SpellkitCommand("Session")]
    internal static SpkObject Session(
        SpellkitCommandContext host,
        string? baseUrl = null,
        SpkObject headers = null!,
        SpkObject auth = null!,
        double? timeout = null,
        bool? allowRedirects = null) =>
        SpkHttp.CreateSession(host.ExecutionContext, baseUrl, headers, auth, timeout, allowRedirects);
}
