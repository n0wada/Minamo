using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Spellkit.Generators;

public static class Extensions
{
    public static string ToString(this IEnumerable<string> elements, string mask, string separator = ", ") =>
        string.Join(separator, elements.Select(s => string.Format(mask, s)));
    
    public static string ToString<T>(this IEnumerable<T> elements, string mask, Func<T, string> selector, string separator = ", ") =>
        string.Join(separator, elements.Select(obj => string.Format(mask, selector(obj))));

    public static string GetSafeName(this ITypeSymbol self)
    {
        if (self.NullableAnnotation == NullableAnnotation.Annotated && self.IsReferenceType)
        {
            return self.OriginalDefinition.ToString();
        }
        else
        {
            return self.ToString();
        }
    }
}

internal enum ParFlags
{
    None,
    Nullable = 0x01,
    VarArg = 0x02,
    NoVar = 0x04,
    CheckErr = 0x08
}

internal enum MethodFlags
{
    None,
    Static = 0x01,
    Property = 0x02
}

internal enum GeneratorTargetKind
{
    Type,
    Module
}

internal readonly struct GeneratorTarget
{
    public GeneratorTarget(INamedTypeSymbol type, GeneratorTargetKind kind) =>
        (Type, Kind) = (type, kind);

    public INamedTypeSymbol Type { get; }

    public GeneratorTargetKind Kind { get; }
}

[Generator]
public sealed class SpellkitTypeGenerator : IIncrementalGenerator
{
    internal static readonly object Nil = new();
    private static readonly string[] neverNulls = new[] { "int", "long", "float", "double", "char", "bool" };

    private static string Error(string value) => $"__ctx.InvalidType({value})";

    private static bool Error(SourceProductionContext context, string value) =>
        GeneratorSupport.Error(context, value);

    private static bool IsSpellkitObject(ITypeSymbol type) => GeneratorSupport.IsSpellkitObject(type);

    private static void ParamCheck(CodeWriter sb, string input, string par, string conversion, ParFlags flags, params string[] SpellkitTypes)
    {
        if (SpellkitTypes.Length > 0)
        {
            sb.AppendPadding();
            sb.Append($"if ({input}.TypeId is ");

            for (var i = 0; i < SpellkitTypes.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(" and ");
                }

                sb.Append($"not {Types.Spellkit}.{SpellkitTypes[i]}");
            }

            if ((flags & ParFlags.Nullable) == ParFlags.Nullable)
            {
                sb.Append($" and not {Types.Spellkit}.Nil");
            }

            sb.Append($") return {Error(input)};");
            sb.AppendLine();
        }

        var app = (flags & ParFlags.NoVar) != ParFlags.NoVar ? "var " : "";

        if ((flags & ParFlags.Nullable) == ParFlags.Nullable)
        {
            sb.AppendLine($"{app}{par} = {input}.TypeId == {Types.Spellkit}.Nil ? null : {string.Format(conversion, input)};");
        }
        else
        {
            sb.AppendLine($"{app}{par} = {string.Format(conversion, input)};");
        }

        if ((flags & ParFlags.CheckErr) == ParFlags.CheckErr)
        {
            sb.AppendLine($"if (__ctx.HasErrors) return {Types.SpellkitNil}.Instance;");
        }
    }

    private bool ParamCheckArray(string input, string par, ParFlags flags, ITypeSymbol ti, CodeWriter sb, bool nullable)
    {
        var ati = (IArrayTypeSymbol)ti;
        var (arr, elem, index, len) = ($"__arr_{par}", $"__elem_{par}", $"__index_{par}", $"__len_{par}");
        var elementTypeName = ati.ElementType.GetSafeName();

        sb.AppendLine($"{Types.SpellkitObject}[] {arr};");



        if ((flags & ParFlags.VarArg) == ParFlags.VarArg)
        {
            sb.AppendLine($"{arr} = (({Types.SpellkitTuple}){input}).ToArray();");
        }
        else
        {
            sb.AppendLine($"if ({input} is {Types.SpellkitCollection} __coll)");
            sb.AppendInBlock($"{arr} = __coll.ToArray();");
            
            if (nullable)
            {
                sb.AppendLine($"else if ({input}.TypeId == {Types.Spellkit}.Nil)");
                sb.AppendInBlock($"{arr} = null;");
            }

            sb.AppendLine("else");
            sb.StartBlock();
            sb.AppendLine($"{arr} = {Types.SpellkitIterator}.ToEnumerable(ctx, {input}).ToArray();");
            sb.AppendLine($"if (ctx.HasErrors) return {Types.SpellkitNil}.Instance;");
            sb.EndBlock();
        }

        if (ati.ElementType.ToString() == Types.SpellkitObject)
        {
            sb.AppendLine($"var {par} = {arr};");
            return true;
        }
        else if (IsSpellkitObject(ati.ElementType))
        {
            sb.AppendLine($"{ati.ElementType}[] {par} = {arr} is null ? null : new {ati.ElementType}[{arr}.Length];");
            sb.AppendLine($"var {len} = {arr} is null ? 0 : {arr}.Length;");
            sb.AppendLine($"for (var {index} = 0; {index} < {len}; {index}++)");
            sb.StartBlock();
            sb.AppendLine($"if ({arr}[{index}] is not {ati.ElementType} {elem}) return {Error(input)};");
            sb.AppendLine($"{par}[{index}] = {elem};");
            sb.EndBlock();
            return true;
        }
        else if (typeConversions.TryGetValue(elementTypeName, out var conv))
        {
            sb.AppendLine($"{ati.ElementType}[] {par} = {arr} is null ? null : new {ati.ElementType}[{arr}.Length];");
            sb.AppendLine($"var {len} = {arr} is null ? 0 : {arr}.Length;");
            sb.AppendLine($"for (var {index} = 0; {index} < {len}; {index}++)");
            sb.StartBlock();
            conv(sb, $"{arr}[{index}]", $"{par}[{index}]", ParFlags.NoVar, ati.ElementType);
            sb.EndBlock();
            return true;
        }

        return false;
    }

    private readonly Dictionary<string, Action<CodeWriter, string, string, ParFlags, ITypeSymbol>> typeConversions = new()
    {
        { "long", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"(({Types.SpellkitInteger}){{0}}).Value", flag, "Integer") },
        { "long?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"(long?)(({Types.SpellkitInteger}){{0}}).Value", flag, "Integer") },
        { "int", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"(int)(({Types.SpellkitInteger}){{0}}).Value", flag, "Integer") },
        { "int?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"(int?)(({Types.SpellkitInteger}){{0}}).Value", flag, "Integer") },
        { "double", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "{0}.GetFloat()", flag, "Float", "Integer") },
        { "double?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "(double?){0}.GetFloat()", flag, "Float", "Integer") },
        { "float", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "(float){0}.GetFloat()", flag, "Float", "Integer") },
        { "float?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "(float?){0}.GetFloat()", flag, "Float", "Integer") },
        { "string", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "{0}.ToString()", flag, "String", "Char") },
        { "char", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "{0}.GetChar()", flag, "Char", "String") },
        { "char?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "(char?){0}.GetChar()", flag, "Char", "String") },
        { "bool", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"!ReferenceEquals({{0}}, {Types.SpellkitBool}.False) && !ReferenceEquals({{0}}, {Types.SpellkitNil}.Instance)", flag) },
        { "bool?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"(bool?)!ReferenceEquals({{0}}, {Types.SpellkitBool}.False) && !ReferenceEquals({{0}}, {Types.SpellkitNil}.Instance)", flag) },
        { $"{Types.SpellkitObject}", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "{0}", flag) },
        { $"{Types.SpellkitFunction}", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"{Types.Extensions}.ToFunction({{0}}, __ctx)", flag | ParFlags.CheckErr)},
        { $"{Types.Enumerable}", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"{Types.SpellkitIterator}.ToEnumerable(__ctx, {{0}})", flag | ParFlags.CheckErr)}
    }; 
    
    private readonly Dictionary<string, Func<string, string>> returnTypeConversions = new()
    {
        { "long", name => $"new {Types.SpellkitInteger}({name})" },
        { "long?", name => $"({name} is null ? ({Types.SpellkitObject}){Types.SpellkitNil}.Instance : new {Types.SpellkitInteger}({name}.Value))" },
        { "int", name => $"new {Types.SpellkitInteger}({name})" },
        { "int?", name => $"({name} is null ? ({Types.SpellkitObject}){Types.SpellkitNil}.Instance : new {Types.SpellkitInteger}({name}.Value))" },
        { "string", name => $"({name} is null ? ({Types.SpellkitObject}){Types.SpellkitNil}.Instance : new {Types.SpellkitString}({name}))" },
        { "char", name => $"new {Types.SpellkitChar}({name})" },
        { "char?", name => $"({name} is null ? ({Types.SpellkitObject}){Types.SpellkitNil}.Instance : new {Types.SpellkitChar}({name}.Value))" },
        { "double", name => $"new {Types.SpellkitFloat}({name})" },
        { "double?", name => $"({name} is null ? ({Types.SpellkitObject}){Types.SpellkitNil}.Instance : new {Types.SpellkitFloat}({name}.Value))" },
        { "single", name => $"new {Types.SpellkitFloat}({name})" },
        { "single?", name => $"({name} is null ? ({Types.SpellkitObject}){Types.SpellkitNil}.Instance : new {Types.SpellkitFloat}({name}.Value))" },
        { "bool", name => $"({name} ? {Types.SpellkitBool}.True : {Types.SpellkitBool}.False)" },
        { "bool?", name => $"({name} is null ? ({Types.SpellkitObject}){Types.SpellkitNil}.Instance : ({name} ? {Types.SpellkitBool}.True : {Types.SpellkitBool}.False))" },
        { $"{Types.SpellkitObject}", name => $"({name} ?? {Types.SpellkitNil}.Instance)" },
        { $"{Types.Enumerable}", name => $"{Types.SpellkitIterator}.Create({name})" },
        { $"{Types.Enumerable}?", name => $"({name} is null ? ({Types.SpellkitObject}){Types.SpellkitNil}.Instance : {Types.SpellkitIterator}.Create({name})" },
        { $"{Types.HashSet}", name => $"new {Types.SpellkitSet}({name})"},
        { $"{Types.HashSet}?", name => $"({name} is null ? ({Types.SpellkitObject}){Types.SpellkitNil}.Instance : new {Types.SpellkitSet}({name})" }
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targets = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
            static (syntaxContext, cancellationToken) => GetTarget(syntaxContext, cancellationToken))
            .Where(static target => target.HasValue);

        context.RegisterSourceOutput(
            targets,
            (productionContext, target) => Emit(productionContext, target!.Value));
    }

    private static GeneratorTarget? GetTarget(
        GeneratorSyntaxContext context,
        System.Threading.CancellationToken cancellationToken)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol type)
        {
            return null;
        }

        foreach (var attribute in type.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();

            if (attributeName == "Spellkit.Codegen.SpellkitTypeAttribute")
            {
                return new(type, GeneratorTargetKind.Type);
            }

            if (attributeName == "Spellkit.Codegen.GeneratedModuleAttribute")
            {
                return new(type, GeneratorTargetKind.Module);
            }
        }

        return null;
    }

    private void Emit(SourceProductionContext ctx, GeneratorTarget target)
    {
        var t = target.Type;
        var builder = new CodeWriter();
        var syntax = t.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ctx.CancellationToken);
        var unit = syntax?.SyntaxTree.GetRoot(ctx.CancellationToken) as CompilationUnitSyntax;

        if (unit is not null)
        {
            foreach (var us in unit.Usings)
            {
                builder.Append(us.ToFullString());
            }
        }

        builder.AppendLine($"namespace {t.ContainingNamespace}.Internal");
        builder.StartBlock();
        var methods = new List<(string name, bool instance)>();

        foreach (IMethodSymbol m in t.GetMembers().OfType<IMethodSymbol>())
        {
            if (!ProcessMethod(ctx, t, m, builder, methods))
            {
                return;
            }
        }

        builder.EndBlock();
        builder.AppendLine($"namespace {t.ContainingNamespace}");
        builder.StartBlock();
        builder.AppendLine($"using {t.ContainingNamespace}.Internal;");

        if (methods.Count > 0)
        {
            if (target.Kind == GeneratorTargetKind.Type)
            {
                var instanceMethods = methods.Where(kv => kv.instance).ToList();
                var staticMethods = methods.Where(kv => !kv.instance).ToList();

                builder.AppendLine();
                builder.AppendLine($"partial class {t.Name}");
                builder.StartBlock();

                if (instanceMethods.Count > 0)
                {
                    builder.AppendLine($"protected override {Types.SpellkitFunction} InitializeInstanceMember({Types.SpellkitObject} self, string name, {Types.ExecutionContext} ctx) =>");
                    builder.Indent();
                    builder.AppendLine("name switch");
                    builder.StartBlock();
                    foreach (var (im, _) in instanceMethods)
                    {
                        builder.AppendLine($"\"{im}\" => new in_{t.Name}_{im}_WrapperFunction(),");
                    }

                    builder.AppendLine("_ => base.InitializeInstanceMember(self, name, ctx)");
                    builder.Outdent();
                    builder.AppendLine("};");
                    builder.Outdent();
                }

                if (staticMethods.Count > 0)
                {
                    builder.AppendLine($"protected override {Types.SpellkitFunction} InitializeStaticMember(string name, {Types.ExecutionContext} ctx) =>");
                    builder.Indent();
                    builder.AppendLine("name switch");
                    builder.StartBlock();
                    foreach (var (im, _) in staticMethods)
                    {
                        builder.AppendLine($"\"{im}\" => new st_{t.Name}_{im}_WrapperFunction(),");
                    }

                    builder.AppendLine("_ => base.InitializeStaticMember(name, ctx)");
                    builder.Outdent();
                    builder.AppendLine("};");
                    builder.Outdent();
                }

                builder.EndBlock();
            }
            else
            {
                var staticMethods = methods.Where(kv => !kv.instance).Select(kv => kv.name).ToList();

                if (staticMethods.Count == 0)
                {
                    Error(ctx, "Only static methods are supported in modules.");
                    return;
                }

                builder.AppendLine();
                builder.AppendLine($"partial class {t.Name}");
                builder.StartBlock();
                builder.AppendLine("protected override void InitializeMembers()");
                builder.StartBlock();

                foreach (var m in staticMethods)
                {
                    builder.AppendLine($"Add(\"{m}\", new st_{t.Name}_{m}_WrapperFunction());");
                }

                builder.EndBlock();
                builder.EndBlock();
            }
        }

        builder.EndBlock();
        ctx.AddSource($"{t.Name}.generated.cs", builder.ToSourceText());
    }

    private bool ProcessMethod(SourceProductionContext ctx, INamedTypeSymbol t, IMethodSymbol m, CodeWriter builder, List<(string name, bool instance)> methods)
    {
        var attr = m.GetAttributes().FirstOrDefault(a => a.AttributeClass.BaseType.ToString() == Types.SpellkitMemberAttribute);

        if (attr is not null)
        {
            if (!m.IsStatic)
            {
                return Error(ctx, $"Method \"{m.Name}\" is not static. An underlying method for a Spellkit type should be declarated as static.");
            }

            if (m.DeclaredAccessibility != Accessibility.Public && m.DeclaredAccessibility != Accessibility.Internal)
            {
                return Error(ctx, $"Method \"{m.Name}\" is private. An underlying method for a Spellkit type should be declarated as public or internal.");
            }

            var methodNameData = attr.ConstructorArguments.Length == 1 ? attr.ConstructorArguments[0].Value : null;

            if (methodNameData is null || methodNameData is not string methodName)
            {
                methodName = m.Name;
            }

            var isInstance = attr.AttributeClass.ToString() is Types.SpellkitMethodAttribute or Types.SpellkitPropertyAttribute;
            var isProperty = attr.AttributeClass.ToString() is Types.SpellkitPropertyAttribute or Types.SpellkitStaticPropertyAttribute;
            var flags = (!isInstance ? MethodFlags.Static : MethodFlags.None) | (isProperty ? MethodFlags.Property : MethodFlags.None);

            if (ProcessMethod(ctx, t, m, builder, methodName, flags))
            {
                methods.Add((methodName, isInstance));
                return true;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private bool ProcessMethod(SourceProductionContext ctx, INamedTypeSymbol t, IMethodSymbol m, CodeWriter builder, string methodName, MethodFlags implFlags)
    {
        var isStatic = (implFlags & MethodFlags.Static) == MethodFlags.Static;
        var prefix = isStatic ? "st" : "in";
        var className = $"{prefix}_{t.Name}_{methodName}_WrapperFunction";

        builder.AppendLine($"internal sealed class {className} : {Types.SpellkitForeignFunction}");
        builder.StartBlock();
        builder.AppendLine($"protected override {Types.SpellkitObject} CallWithMemoryLayout({Types.ExecutionContext} __ctx, {Types.SpellkitObject}[] __args)");
        builder.StartBlock();

        var pars = EmitParameters(ctx, builder, t, m, isStatic, false, out var varArgIndex);

        if (pars is null)
        {
            return false;
        }

        if (!EmitReturnType(ctx, builder, t, m))
        {
            return false;
        }

        builder.EndBlock();
        builder.AppendLine();

        var sb = new StringBuilder();

        if (pars.Count > 0)
        {
            sb.Append($"new {Types.Par}[] {{");

            for (var i = 0; i < pars.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                var (name, def, vararg) = pars[i];
                sb.Append($"new {Types.Par}(\"{name}\"");

                if (vararg)
                {
                    sb.Append($", {Types.ParKind}.VarArg)");
                }
                else if (def is not null)
                {
                    sb.Append($", {def})");
                }
                else
                {
                    sb.Append($")");
                }
            }

            sb.Append(" }");
        }
        else
        {
            sb.Append($"{Types.Array}.Empty<{Types.Par}>()");
        }

        builder.AppendLine($"public {className}() : base(\"{methodName}\", {sb}, {varArgIndex})");
        builder.StartBlock();

        if ((implFlags & MethodFlags.Property) == MethodFlags.Property)
        {
            builder.AppendLine($"Attr |= 0x01;");
        }

        builder.EndBlock();
        builder.AppendLine();
        builder.AppendLine($"protected override {Types.SpellkitFunction} Clone({Types.ExecutionContext} ctx) => new {prefix}_{t.Name}_{methodName}_WrapperFunction();");

        if ((implFlags & MethodFlags.Property) == MethodFlags.Property)
        {
            builder.AppendLine();
            builder.AppendLine($"protected override SpellkitObject BindOrRun({Types.ExecutionContext} __ctx, {Types.SpellkitObject} __arg)");
            builder.StartBlock();
            EmitParameters(ctx, builder, t, m, isStatic, true, out _);
            EmitReturnType(ctx, builder, t, m);
            builder.EndBlock();
        }

        builder.AppendLine();
        builder.AppendLine($"protected override bool Equals({Types.SpellkitFunction} func) => func is {className} cn && (ReferenceEquals(cn.Self, Self) || (cn.Self is not null && cn.Self.Equals(Self)));");
        builder.EndBlock();

        return true;
    }

    private List<(string name, string def, bool vararg)> EmitParameters(SourceProductionContext ctx, CodeWriter builder, ITypeSymbol t, IMethodSymbol m, bool isStatic, bool specialCall, out int varArgIndex)
    {
        var hasContext = m.Parameters.Length > 0 && m.Parameters[0].Type.ToString() == Types.ExecutionContext;
        var pars = new List<(string name, string def, bool vararg)>();
        varArgIndex = -1;

        for (var i = 0; i < m.Parameters.Length; i++)
        {
            var mp = m.Parameters[i];
            var parName = mp.Name;
            var parNameAttr = m.Parameters[i].GetAttributes().FirstOrDefault(a => a.AttributeClass.Name == "ParameterNameAttribute");

            if (parNameAttr is not null)
            {
                parName = parNameAttr.ConstructorArguments[0].Value.ToString();
            }

            if (hasContext && i == 0) //First parameter is a context
            {
                builder.AppendLine($"var {mp.Name} = __ctx;");
                continue;
            }

            var attrs = mp.GetAttributes();
            var vararg = attrs.Any(a => a.AttributeClass.Name == "VarArgAttribute") || mp.IsParams;
            var def = attrs.Where(a => a.AttributeClass.Name == "DefaultAttribute")
                .Select(a => a.ConstructorArguments.Length > 0 ? (a.ConstructorArguments[0].Value ?? Nil) : Nil)
                .FirstOrDefault();

            if (mp.HasExplicitDefaultValue && def is null)
            {
                def = mp.ExplicitDefaultValue ?? Nil;
            }

            var nullable = ReferenceEquals(def, Nil);
            var typeName = mp.Type.GetSafeName();

            var indexShift = 0 - (hasContext ? 1 : 0) - (isStatic ? 0 : 1);

            if (vararg)
            {
                varArgIndex = i + indexShift;
            }

            if (nullable && neverNulls.Contains(typeName))
            {
                Error(ctx, $"Type of parameter \"{typeName}\" is not nullable (type {t.Name}, method {m.Name}).");
                return null;
            }

            var firstParam = i == 1 && hasContext || i == 0 && !hasContext;
            var sourceParName = firstParam && !isStatic ? "Self" : $"__args[{i + indexShift}]";

            if (specialCall)
            {
                sourceParName = "__arg";
            }

            if (isStatic || !firstParam)
            {
                pars.Add((parName, def.ToLiteral(), vararg));
            }

            var flags = vararg ? ParFlags.VarArg : ParFlags.None;
            if (nullable)
            {
                flags |= ParFlags.Nullable;
            }

            if (typeConversions.TryGetValue(typeName, out var conv1))
            {
                conv1(builder, sourceParName, mp.Name, flags, mp.Type);
            }
            else if (mp.Type is IArrayTypeSymbol arr)
            {
                var elementType = arr.ElementType;
                var res = ParamCheckArray(sourceParName, mp.Name, flags, mp.Type, builder, nullable);

                if (!res)
                {
                    Error(ctx, $"Parameter type \"{typeName}\" is not supported.");
                    return null;
                }
            }
            else if (IsSpellkitObject(mp.Type))
            {
                if ((firstParam && !isStatic) || (vararg && typeName == Types.SpellkitTuple))
                {
                    builder.AppendLine($"var {mp.Name} = ({typeName}){sourceParName};");
                }
                else
                {
                    ConvertToSpellkitObject(builder, typeName, sourceParName, mp.Name, nullable);
                }
            }
            else
            {
                Error(ctx, $"Parameter type \"{typeName}\" is not supported.");
                return null;
            }
        }

        return pars;
    }

    private bool EmitReturnType(SourceProductionContext ctx, CodeWriter builder, ITypeSymbol t, IMethodSymbol m)
    {
        if (IsAsyncReturnType(m.ReturnType))
        {
            builder.AppendLine(
                $"return global::Spellkit.Hosting.SpellkitCommandConvert.FromAwaitable({t.Name}.{m.Name}({m.Parameters.ToString("{0}", p => p.Name)}));");
            return true;
        }

        if (m.ReturnType.ToString() != "void")
        {
            var returnTypeName = m.ReturnType.GetSafeName();

            builder.AppendLine($"var __ret = {t.Name}.{m.Name}({m.Parameters.ToString("{0}", p => p.Name)});");

            if (returnTypeConversions.TryGetValue(returnTypeName, out var conv2))
            {
                builder.AppendLine($"return {conv2("__ret")};");
            }
            else if (IsSpellkitObject(m.ReturnType))
            {
                builder.AppendLine("return __ret;");
            }
            else if (m.ReturnType is IArrayTypeSymbol arr)
            {
                if (IsSpellkitObject(arr.ElementType))
                {
                    builder.AppendLine($"return new {Types.SpellkitArray}(__ret);");
                }
                else
                {
                    builder.AppendLine($"var __arr = new {Types.SpellkitObject}[__ret.Length];");
                    builder.AppendLine("for (var i = 0; i < __ret.Length; i++)");
                    builder.StartBlock();
                    var et = arr.ElementType.GetSafeName();
                    if (returnTypeConversions.TryGetValue(et, out var conv3))
                    {
                        builder.AppendLine($"__arr[i] = {conv3("__ret[i]")};");
                    }
                    else
                    {
                        return Error(ctx, $"Return type \"{returnTypeName}\" is not supported.");
                    }

                    builder.EndBlock();
                    builder.AppendLine($"return new {Types.SpellkitArray}(__arr);");
                }
            }
            else
            {
                return Error(ctx, $"Return type \"{returnTypeName}\" is not supported.");
            }

            return true;
        }
        else
        {
            builder.AppendLine($"{t.Name}.{m.Name}({m.Parameters.ToString("{0}", p => p.Name)});");
            builder.AppendLine($"return {Types.SpellkitNil}.Instance;");
            return true;
        }
    }

    private static bool IsAsyncReturnType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var definition = namedType.ConstructedFrom.ToDisplayString();
        return definition is "System.Threading.Tasks.Task"
            or "System.Threading.Tasks.Task<TResult>"
            or "System.Threading.Tasks.ValueTask"
            or "System.Threading.Tasks.ValueTask<TResult>";
    }

    private void ConvertToSpellkitObject(CodeWriter builder, string targetType, string oldVar, string newVar, bool nullable = false)
    {
        if (!nullable)
        {
            builder.AppendLine($"if ({oldVar} is not {targetType} {newVar}) return {Error(oldVar)};");
            return;
        }

        builder.AppendLine($"{targetType} {newVar} = default;");
        builder.AppendLine($"if ({oldVar}.TypeId != {Types.Spellkit}.Nil)");
        builder.StartBlock();
        builder.AppendLine($"if ({oldVar} is not {targetType} __tmp_{newVar}) return {Error(oldVar)};");
        builder.AppendLine($"{newVar} = __tmp_{newVar};");
        builder.EndBlock();
    }
}
