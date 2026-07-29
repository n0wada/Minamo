using Spellkit.Runtime.Types;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace Spellkit.Library.Http;

public sealed class SpellkitHttpSession : SpellkitForeignObject, IDisposable
{
    internal readonly SpellkitHttpOptions Defaults;
    internal readonly HttpClient Client;

    internal SpellkitHttpSession(SpellkitHttpSessionTypeInfo typeInfo, SpellkitHttpOptions defaults) : base(typeInfo)
    {
        Defaults = defaults;
        Client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = defaults.AllowRedirects != false
        });
    }

    public override SpellkitObject Clone() => this;

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public override object ToObject() => Client;

    public override string ToString() => Defaults.BaseUrl ?? "[http Session]";

    public void Dispose() => Client.Dispose();
}
