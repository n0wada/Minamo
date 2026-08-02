using Spellkit.Hosting;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Json;

[SpellkitModule("json")]
public static class JsonModule
{
    [SpellkitCommand("parse")]
    internal static SpellkitObject Parse(SpellkitCommandContext host, string value) =>
        SpellkitJson.Parse(host.ExecutionContext, value);

    [SpellkitCommand("stringify")]
    internal static SpellkitObject Stringify(
        SpellkitCommandContext host,
        SpellkitObject value,
        bool indented = false) =>
        SpellkitJson.Stringify(host.ExecutionContext, value, indented) is { } result
            ? SpellkitString.Get(result)
            : Nil;
}
