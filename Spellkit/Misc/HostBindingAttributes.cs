namespace Spellkit.Codegen;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class GeneratedModuleAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class SpellkitTypeAttribute : Attribute;

public abstract class SpellkitMemberAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpellkitMethodAttribute : SpellkitMemberAttribute
{
    public SpellkitMethodAttribute() { }

    public SpellkitMethodAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpellkitPropertyAttribute : SpellkitMemberAttribute
{
    public SpellkitPropertyAttribute() { }

    public SpellkitPropertyAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpellkitStaticMethodAttribute : SpellkitMemberAttribute
{
    public SpellkitStaticMethodAttribute() { }

    public SpellkitStaticMethodAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpellkitStaticPropertyAttribute : SpellkitMemberAttribute
{
    public SpellkitStaticPropertyAttribute() { }

    public SpellkitStaticPropertyAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class MixinAttribute : Attribute;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class ParameterNameAttribute : Attribute
{
    public ParameterNameAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class VarArgAttribute : Attribute;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class DefaultAttribute : Attribute
{
    public DefaultAttribute() { }

    public DefaultAttribute(int _) { }

    public DefaultAttribute(long _) { }

    public DefaultAttribute(char _) { }

    public DefaultAttribute(string _) { }

    public DefaultAttribute(bool _) { }

    public DefaultAttribute(double _) { }
}
