namespace Spellkit.Hosting;

/// <summary>Registers a library supplied outside the core Spellkit assembly.</summary>
public interface ISpellkitLibrary
{
    string Id { get; }

    void Register(SpellkitHost host);
}
