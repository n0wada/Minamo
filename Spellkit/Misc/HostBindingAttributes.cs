namespace Spellkit.Codegen;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class GeneratedModuleAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class SpkTypeAttribute : Attribute;

public abstract class SpkMemberAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpkMethodAttribute : SpkMemberAttribute
{
    public SpkMethodAttribute() { }

    public SpkMethodAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpkPropertyAttribute : SpkMemberAttribute
{
    public SpkPropertyAttribute() { }

    public SpkPropertyAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpkStaticMethodAttribute : SpkMemberAttribute
{
    public SpkStaticMethodAttribute() { }

    public SpkStaticMethodAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpkStaticPropertyAttribute : SpkMemberAttribute
{
    public SpkStaticPropertyAttribute() { }

    public SpkStaticPropertyAttribute(string _) { }
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
