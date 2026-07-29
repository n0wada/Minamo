using Spellkit.Runtime.Types;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace Spellkit.Library.Http;

public sealed class SpellkitHttpResponse : SpellkitForeignObject
{
    internal readonly byte[] Content;
    internal readonly Dictionary<string, string> Headers;
    internal readonly string Reason;
    internal readonly int StatusCode;
    internal readonly Uri? Url;

    internal SpellkitHttpResponse(
        SpellkitHttpResponseTypeInfo typeInfo,
        HttpResponseMessage response,
        byte[] content) : base(typeInfo)
    {
        Content = content;
        Headers = response.Headers
            .Concat(response.Content.Headers)
            .ToDictionary(
                header => header.Key,
                header => string.Join(", ", header.Value),
                StringComparer.OrdinalIgnoreCase);
        Reason = response.ReasonPhrase ?? "";
        StatusCode = (int)response.StatusCode;
        Url = response.RequestMessage?.RequestUri;
    }

    internal bool Ok => StatusCode >= 200 && StatusCode <= 399;

    public override SpellkitObject Clone() => this;

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public override object ToObject() => this;

    public override string ToString() => $"HTTP {StatusCode} {Reason}".TrimEnd();
}
