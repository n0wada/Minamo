using Spellkit.Runtime.Types;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace Spellkit.Library.Http;

public sealed class SpkHttpSession : SpkForeignObject, IDisposable
{
    internal readonly SpkHttpOptions Defaults;
    internal readonly HttpClient Client;

    internal SpkHttpSession(SpkHttpSessionTypeInfo typeInfo, SpkHttpOptions defaults) : base(typeInfo)
    {
        Defaults = defaults;
        Client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = defaults.AllowRedirects != false
        });
    }

    public override SpkObject Clone() => this;

    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public override object ToObject() => Client;

    public override string ToString() => Defaults.BaseUrl ?? "[http Session]";

    public void Dispose() => Client.Dispose();
}
