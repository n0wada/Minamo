namespace Spellkit.Hosting;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SpellkitModuleAttribute : Attribute
{
    public SpellkitModuleAttribute(string name) => Name = name;

    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SpellkitResourceAttribute : Attribute
{
    public SpellkitResourceAttribute(string name) => Name = name;

    public string Name { get; }

    public SpellkitResourceLifetime Lifetime { get; set; } = SpellkitResourceLifetime.Shared;
}

public enum SpellkitResourceLifetime
{
    Shared,
    Transient
}

public abstract class SpellkitResource
{
    protected virtual void OnRelease() { }

    internal void Release() => OnRelease();
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SpellkitCommandAttribute : Attribute
{
    public SpellkitCommandAttribute() { }

    public SpellkitCommandAttribute(string name) => Name = name;

    public string? Name { get; }

    public string? Description { get; set; }

    public string? Capability { get; set; }

    public string? Type { get; set; }

}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SpellkitPropertyAttribute : Attribute
{
    public SpellkitPropertyAttribute() { }

    public SpellkitPropertyAttribute(string name) => Name = name;

    public string? Name { get; }

    public string? Description { get; set; }

    public string? Capability { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SpellkitForeignTypeAttribute : Attribute
{
    public SpellkitForeignTypeAttribute(Type type) => Type = type;

    public Type Type { get; }
}
