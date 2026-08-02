namespace Spellkit.Hosting;

/// <summary>Registers a library supplied outside the core Spellkit assembly.</summary>
public interface ISpellkitLibrary
{
    void Register(SpellkitHost host);
}
