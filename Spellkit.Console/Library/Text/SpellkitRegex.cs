using Spellkit.Runtime.Types;
using System.Text.RegularExpressions;

namespace Spellkit.Library.Text;

public sealed class SpellkitRegex : SpellkitForeignObject
{
    private static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(2);

    internal readonly Regex Regex;

    internal bool RemoveEmptyEntries { get; }

    public SpellkitRegex(SpellkitForeignTypeInfo typeInfo, string regex, bool ignoreCase, bool singleline, bool multiline, bool removeEmptyEntries) : base(typeInfo)
    {
        var opt = RegexOptions.Compiled;

        if (ignoreCase)
        {
            opt |= RegexOptions.IgnoreCase;
        }

        if (singleline)
        {
            opt |= RegexOptions.Singleline;
        }

        if (multiline)
        {
            opt |= RegexOptions.Multiline;
        }

        RemoveEmptyEntries = removeEmptyEntries;
        Regex = new Regex(regex, opt, DefaultMatchTimeout);
    }

    public override object ToObject() => Regex;

    public override SpellkitObject Clone() => this;

    public override bool Equals(SpellkitObject? other) => other is SpellkitRegex r && r.Regex == Regex;

    public override int GetHashCode() => Regex.GetHashCode();

    public override string ToString() => Regex.ToString();
}
