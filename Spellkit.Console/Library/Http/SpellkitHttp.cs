using Spellkit.Library.Json;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Net.Http;
using System.Text;

namespace Spellkit.Library.Http;

internal static class SpellkitHttp
{
    private static readonly HttpClient SharedClient = new();

    internal static SpellkitObject CreateSession(
        ExecutionContext ctx,
        string? baseUrl,
        SpellkitObject? headers,
        SpellkitObject? auth,
        double? timeout,
        bool? allowRedirects)
    {
        var defaults = SpellkitHttpOptions.FromValues(ctx, baseUrl, null, headers, null, null, timeout, auth, allowRedirects);
        return ctx.HasErrors
            ? Nil
            : new SpellkitHttpSession(ctx.Type<SpellkitHttpSessionTypeInfo>(), defaults);
    }

    internal static SpellkitObject Request(
        ExecutionContext ctx,
        string method,
        string url,
        SpellkitObject? @params,
        SpellkitObject? headers,
        SpellkitObject? json,
        SpellkitObject? data,
        double? timeout,
        SpellkitObject? auth,
        bool? allowRedirects,
        SpellkitHttpSession? session = null)
    {
        var overrides = SpellkitHttpOptions.FromValues(ctx, null, @params, headers, json, data, timeout, auth, allowRedirects);
        var requestOptions = session is null
            ? overrides
            : session.Defaults.Merge(overrides);
        if (ctx.HasErrors)
        {
            return Nil;
        }

        try
        {
            using var request = CreateRequest(ctx, method, url, requestOptions);
            if (ctx.HasErrors)
            {
                return Nil;
            }

            var client = session?.Client ?? SharedClient;
            using var cts = requestOptions.Timeout is null
                ? null
                : new CancellationTokenSource(TimeSpan.FromSeconds(requestOptions.Timeout.Value));
            using var response = cts is null
                ? client.Send(request)
                : client.Send(request, cts.Token);
            var content = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return new SpellkitHttpResponse(ctx.Type<SpellkitHttpResponseTypeInfo>(), response, content);
        }
        catch (OperationCanceledException)
        {
            return ctx.Timeout();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            return ctx.IOFailed(ex.Message);
        }
    }

    private static HttpRequestMessage CreateRequest(
        ExecutionContext ctx,
        string method,
        string url,
        SpellkitHttpOptions options)
    {
        var requestUri = BuildUri(ctx, options.BaseUrl, url, options.Params);
        var request = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), requestUri);

        foreach (var (name, value) in options.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content ??= new ByteArrayContent(Array.Empty<byte>());
                request.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (options.Auth is { } auth)
        {
            request.Headers.TryAddWithoutValidation("Authorization", auth);
        }

        if (options.Json is not null)
        {
            var content = SpellkitJson.Stringify(ctx, options.Json);
            request.Content = new StringContent(
                content ?? string.Empty,
                Encoding.UTF8,
                "application/json");
        }
        else if (options.Data is not null)
        {
            request.Content = CreateContent(ctx, options.Data);
        }

        return request;
    }

    private static HttpContent CreateContent(ExecutionContext ctx, SpellkitObject data)
    {
        if (data is SpellkitString text)
        {
            return new StringContent(text.Value, Encoding.UTF8);
        }

        if (data is SpellkitInterop interop && interop.Object is byte[] bytes)
        {
            return new ByteArrayContent(bytes);
        }

        if (data.ToObject() is byte[] directBytes)
        {
            return new ByteArrayContent(directBytes);
        }

        return new StringContent(data.ToString(ctx).Value, Encoding.UTF8);
    }

    private static Uri BuildUri(
        ExecutionContext ctx,
        string? baseUrl,
        string url,
        Dictionary<string, SpellkitObject> query)
    {
        Uri uri;
        if (baseUrl is not null && !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            uri = new Uri(new Uri(AppendSlash(baseUrl), UriKind.Absolute), url);
        }
        else
        {
            uri = new Uri(url, UriKind.Absolute);
        }

        if (query.Count == 0)
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var existing = builder.Query;
        if (existing.StartsWith('?'))
        {
            existing = existing[1..];
        }

        var appended = string.Join("&", query.SelectMany(kv => QueryParts(ctx, kv.Key, kv.Value)));
        builder.Query = string.IsNullOrEmpty(existing) ? appended : existing + "&" + appended;
        return builder.Uri;
    }

    private static IEnumerable<string> QueryParts(ExecutionContext ctx, string key, SpellkitObject value)
    {
        if (value is SpellkitArray array)
        {
            foreach (var item in array)
            {
                yield return QueryPart(ctx, key, item);
            }
        }
        else
        {
            yield return QueryPart(ctx, key, value);
        }
    }

    private static string QueryPart(ExecutionContext ctx, string key, SpellkitObject value) =>
        Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value.ToString(ctx).Value);

    private static string AppendSlash(string value) =>
        value.EndsWith('/') ? value : value + "/";

    internal static bool TryConvertDictionary(
        ExecutionContext ctx,
        SpellkitObject value,
        out Dictionary<string, SpellkitObject> map)
    {
        map = new(StringComparer.Ordinal);
        if (value is not SpellkitDictionary dictionary)
        {
            ctx.InvalidCast(value.TypeName, typeof(Dictionary<string, SpellkitObject>).FullName!);
            return false;
        }

        foreach (var item in dictionary)
        {
            if (item is not SpellkitTuple pair || pair.Count < 2)
            {
                ctx.InvalidValue(item);
                return false;
            }

            map[pair[0].ToString(ctx).Value] = pair[1];
            if (ctx.HasErrors)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool? OptionalBool(SpellkitObject? value) =>
        value is null || value.TypeId == SpellkitTypeCodes.Nil ? null : value.IsTrue();
}

internal sealed record SpellkitHttpOptions
{
    internal string? BaseUrl { get; init; }

    internal Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    internal Dictionary<string, SpellkitObject> Params { get; init; } = new(StringComparer.Ordinal);

    internal SpellkitObject? Json { get; init; }

    internal SpellkitObject? Data { get; init; }

    internal double? Timeout { get; init; }

    internal bool? AllowRedirects { get; init; }

    internal string? Auth { get; init; }

    internal SpellkitHttpOptions Merge(SpellkitHttpOptions other)
    {
        return new()
        {
            BaseUrl = other.BaseUrl ?? BaseUrl,
            Headers = Headers
                .Concat(other.Headers)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            Params = Params
                .Concat(other.Params)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            Json = other.Json ?? Json,
            Data = other.Data ?? Data,
            Timeout = other.Timeout ?? Timeout,
            AllowRedirects = other.AllowRedirects ?? AllowRedirects,
            Auth = other.Auth ?? Auth
        };
    }

    internal static SpellkitHttpOptions FromValues(
        ExecutionContext ctx,
        string? baseUrl,
        SpellkitObject? @params,
        SpellkitObject? headers,
        SpellkitObject? json,
        SpellkitObject? data,
        double? timeout,
        SpellkitObject? auth,
        bool? allowRedirects)
    {
        var result = new SpellkitHttpOptions
        {
            BaseUrl = baseUrl,
            Params = IsNil(@params) ? new(StringComparer.Ordinal) : ReadObjectMap(ctx, @params!),
            Headers = IsNil(headers) ? new(StringComparer.OrdinalIgnoreCase) : ReadStringMap(ctx, headers!),
            Json = IsNil(json) ? null : json,
            Data = IsNil(data) ? null : data,
            Timeout = timeout,
            AllowRedirects = allowRedirects,
            Auth = IsNil(auth) ? null : ReadAuth(ctx, auth!)
        };

        return result;
    }

    private static Dictionary<string, SpellkitObject> ReadObjectMap(ExecutionContext ctx, SpellkitObject value) =>
        SpellkitHttp.TryConvertDictionary(ctx, value, out var map) ? map : new();

    private static Dictionary<string, string> ReadStringMap(ExecutionContext ctx, SpellkitObject value)
    {
        if (!SpellkitHttp.TryConvertDictionary(ctx, value, out var map))
        {
            return new();
        }

        return map.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(ctx).Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string ReadAuth(ExecutionContext ctx, SpellkitObject value)
    {
        if (value is SpellkitString token)
        {
            return token.Value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? token.Value
                : "Bearer " + token.Value;
        }

        if (!SpellkitHttp.TryConvertDictionary(ctx, value, out var map))
        {
            return "";
        }

        if (map.TryGetValue("bearer", out var bearer))
        {
            return "Bearer " + bearer.ToString(ctx).Value;
        }

        if (map.TryGetValue("basic", out var basic) && SpellkitHttp.TryConvertDictionary(ctx, basic, out var pair))
        {
            pair.TryGetValue("user", out var user);
            pair.TryGetValue("password", out var password);
            var text = (user?.ToString(ctx).Value ?? "") + ":" + (password?.ToString(ctx).Value ?? "");
            return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        return "";
    }

    private static bool IsNil(SpellkitObject? value) => value is null || value.TypeId == SpellkitTypeCodes.Nil;
}
