namespace Minamo.Codegen;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class GeneratedModuleAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MinamoTypeAttribute : Attribute;

public abstract class MinamoMemberAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MinamoMethodAttribute : MinamoMemberAttribute
{
    public MinamoMethodAttribute() { }

    public MinamoMethodAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MinamoPropertyAttribute : MinamoMemberAttribute
{
    public MinamoPropertyAttribute() { }

    public MinamoPropertyAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MinamoStaticMethodAttribute : MinamoMemberAttribute
{
    public MinamoStaticMethodAttribute() { }

    public MinamoStaticMethodAttribute(string _) { }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MinamoStaticPropertyAttribute : MinamoMemberAttribute
{
    public MinamoStaticPropertyAttribute() { }

    public MinamoStaticPropertyAttribute(string _) { }
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
