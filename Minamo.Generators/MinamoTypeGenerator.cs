using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Minamo.Generators;

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
public sealed class MinamoTypeGenerator : IIncrementalGenerator
{
    // Keep the adapters internal to each consuming assembly, including external type bindings.
    private const string SharedSource = @"#nullable enable
using System;
using Minamo.Debug;
using Minamo.Runtime.Types;
using ExecutionContext = Minamo.Runtime.ExecutionContext;
namespace Minamo.Generated
{
    internal abstract class GeneratedBuiltinFunctionBase : MinamoForeignFunction
    {
        private readonly Func<int, MinamoFunction> factory;
        private readonly int factoryId;
        private readonly Func<ExecutionContext, MinamoObject, MinamoObject>? property;
        protected GeneratedBuiltinFunctionBase(string name, Par[] pars, int varArgIndex,
            Func<int, MinamoFunction> factory, int factoryId, Func<ExecutionContext, MinamoObject, MinamoObject>? property)
            : base(name, pars, varArgIndex)
        {
            this.factory = factory;
            this.factoryId = factoryId;
            this.property = property;
            if (property != null) Attr |= 0x01;
        }
        protected override MinamoFunction Clone(ExecutionContext ctx) => factory(factoryId);
        protected override MinamoObject BindOrRun(ExecutionContext ctx, MinamoObject arg) => property != null ? property(ctx, arg) : base.BindOrRun(ctx, arg);
    }

    internal sealed class GeneratedBuiltinFunction : GeneratedBuiltinFunctionBase
    {
        private readonly Func<ExecutionContext, MinamoObject, MinamoObject[], MinamoObject> invoke;
        internal GeneratedBuiltinFunction(string name, Par[] pars, int varArgIndex,
            Func<ExecutionContext, MinamoObject, MinamoObject[], MinamoObject> invoke, Func<int, MinamoFunction> factory, int factoryId,
            Func<ExecutionContext, MinamoObject, MinamoObject>? property)
            : base(name, pars, varArgIndex, factory, factoryId, property) => this.invoke = invoke;
        protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) => invoke(ctx, Self!, args);
        protected override bool Equals(MinamoFunction other) => other is GeneratedBuiltinFunction fn && invoke.Equals(fn.invoke) && IsSameInstance(this, other);
    }

    internal sealed class GeneratedBuiltinFunction0 : GeneratedBuiltinFunctionBase
    {
        private readonly Func<ExecutionContext, MinamoObject, MinamoObject> invoke;
        internal GeneratedBuiltinFunction0(string name, Par[] pars, int varArgIndex,
            Func<ExecutionContext, MinamoObject, MinamoObject> invoke, Func<int, MinamoFunction> factory, int factoryId,
            Func<ExecutionContext, MinamoObject, MinamoObject>? property)
            : base(name, pars, varArgIndex, factory, factoryId, property) => this.invoke = invoke;
        protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) => invoke(ctx, Self!);
        protected override bool Equals(MinamoFunction other) => other is GeneratedBuiltinFunction0 fn && invoke.Equals(fn.invoke) && IsSameInstance(this, other);
    }

    internal sealed class GeneratedBuiltinFunction1 : GeneratedBuiltinFunctionBase
    {
        private readonly Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject> invoke;
        internal GeneratedBuiltinFunction1(string name, Par[] pars, int varArgIndex,
            Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject> invoke, Func<int, MinamoFunction> factory, int factoryId,
            Func<ExecutionContext, MinamoObject, MinamoObject>? property)
            : base(name, pars, varArgIndex, factory, factoryId, property) => this.invoke = invoke;
        protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) => invoke(ctx, Self!, args[0]);
        protected override bool Equals(MinamoFunction other) => other is GeneratedBuiltinFunction1 fn && invoke.Equals(fn.invoke) && IsSameInstance(this, other);
        protected override bool CanCallWithSingleArgumentDirectly => true;
        protected override MinamoObject CallWithSingleArgument(ExecutionContext ctx, MinamoObject arg) => ctx.HasErrors ? MinamoNil.Instance : invoke(ctx, Self!, arg);
    }

    internal sealed class GeneratedBuiltinFunction2 : GeneratedBuiltinFunctionBase
    {
        private readonly Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject, MinamoObject> invoke;
        internal GeneratedBuiltinFunction2(string name, Par[] pars, int varArgIndex,
            Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject, MinamoObject> invoke, Func<int, MinamoFunction> factory, int factoryId,
            Func<ExecutionContext, MinamoObject, MinamoObject>? property)
            : base(name, pars, varArgIndex, factory, factoryId, property) => this.invoke = invoke;
        protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) => invoke(ctx, Self!, args[0], args[1]);
        protected override bool Equals(MinamoFunction other) => other is GeneratedBuiltinFunction2 fn && invoke.Equals(fn.invoke) && IsSameInstance(this, other);
        protected override bool CanCallWithTwoArgumentsDirectly => true;
        protected override MinamoObject CallWithTwoArguments(ExecutionContext ctx, MinamoObject first, MinamoObject second) => ctx.HasErrors ? MinamoNil.Instance : invoke(ctx, Self!, first, second);
    }
}
";
    internal static readonly object Nil = new();
    private static readonly string[] neverNulls = new[] { "int", "long", "float", "double", "char", "bool" };

    private static string Error(string value) => $"__ctx.InvalidType({value})";

    private static bool Error(SourceProductionContext context, string value) =>
        GeneratorSupport.Error(context, value);

    private static bool IsMinamoObject(ITypeSymbol type) => GeneratorSupport.IsMinamoObject(type);

    private static void ParamCheck(CodeWriter sb, string input, string par, string conversion, ParFlags flags, params string[] MinamoTypes)
    {
        if (MinamoTypes.Length > 0)
        {
            sb.AppendPadding();
            sb.Append($"if ({input}.TypeId is ");

            for (var i = 0; i < MinamoTypes.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(" and ");
                }

                sb.Append($"not {Types.Minamo}.{MinamoTypes[i]}");
            }

            if ((flags & ParFlags.Nullable) == ParFlags.Nullable)
            {
                sb.Append($" and not {Types.Minamo}.Nil");
            }

            sb.Append($") return {Error(input)};");
            sb.AppendLine();
        }

        var app = (flags & ParFlags.NoVar) != ParFlags.NoVar ? "var " : "";

        if ((flags & ParFlags.Nullable) == ParFlags.Nullable)
        {
            sb.AppendLine($"{app}{par} = {input}.TypeId == {Types.Minamo}.Nil ? null : {string.Format(conversion, input)};");
        }
        else
        {
            sb.AppendLine($"{app}{par} = {string.Format(conversion, input)};");
        }

        if ((flags & ParFlags.CheckErr) == ParFlags.CheckErr)
        {
            sb.AppendLine($"if (__ctx.HasErrors) return {Types.MinamoNil}.Instance;");
        }
    }

    private bool ParamCheckArray(string input, string par, ParFlags flags, ITypeSymbol ti, CodeWriter sb, bool nullable)
    {
        var ati = (IArrayTypeSymbol)ti;
        var (arr, elem, index, len) = ($"__arr_{par}", $"__elem_{par}", $"__index_{par}", $"__len_{par}");
        var collection = $"__coll_{par}";
        var elementTypeName = ati.ElementType.GetSafeName();

        sb.AppendLine($"{Types.MinamoObject}[] {arr};");



        if ((flags & ParFlags.VarArg) == ParFlags.VarArg)
        {
            sb.AppendLine($"{arr} = (({Types.MinamoTuple}){input}).ToArray();");
        }
        else
        {
            sb.AppendLine($"if ({input} is {Types.MinamoCollection} {collection})");
            sb.AppendInBlock($"{arr} = {collection}.ToArray();");
            
            if (nullable)
            {
                sb.AppendLine($"else if ({input}.TypeId == {Types.Minamo}.Nil)");
                sb.AppendInBlock($"{arr} = null;");
            }

            sb.AppendLine("else");
            sb.StartBlock();
            sb.AppendLine($"{arr} = global::System.Linq.Enumerable.ToArray({Types.MinamoIterator}.ToEnumerable(__ctx, {input}));");
            sb.AppendLine($"if (__ctx.HasErrors) return {Types.MinamoNil}.Instance;");
            sb.EndBlock();
        }

        if (ati.ElementType.ToString() == Types.MinamoObject)
        {
            sb.AppendLine($"var {par} = {arr};");
            return true;
        }
        else if (IsMinamoObject(ati.ElementType))
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
        { "long", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"(({Types.MinamoInteger}){{0}}).Value", flag, "Integer") },
        { "long?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"(long?)(({Types.MinamoInteger}){{0}}).Value", flag, "Integer") },
        { "int", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "global::Minamo.Hosting.MinamoCommandConvert.ToInt32(__ctx, {0})", flag | ParFlags.CheckErr, "Integer") },
        { "int?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "(int?)global::Minamo.Hosting.MinamoCommandConvert.ToInt32(__ctx, {0})", flag | ParFlags.CheckErr, "Integer") },
        { "double", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "{0}.GetFloat()", flag, "Float", "Integer") },
        { "double?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "(double?){0}.GetFloat()", flag, "Float", "Integer") },
        { "float", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "(float){0}.GetFloat()", flag, "Float", "Integer") },
        { "float?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "(float?){0}.GetFloat()", flag, "Float", "Integer") },
        { "string", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "{0}.ToString()", flag, "String", "Char") },
        { "char", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "{0}.GetChar()", flag, "Char", "String") },
        { "char?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "(char?){0}.GetChar()", flag, "Char", "String") },
        { "bool", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"!ReferenceEquals({{0}}, {Types.MinamoBool}.False) && !ReferenceEquals({{0}}, {Types.MinamoNil}.Instance)", flag) },
        { "bool?", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"(bool?)(!ReferenceEquals({{0}}, {Types.MinamoBool}.False) && !ReferenceEquals({{0}}, {Types.MinamoNil}.Instance))", flag) },
        { $"{Types.MinamoObject}", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, "{0}", flag) },
        { $"{Types.MinamoFunction}", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"{Types.Extensions}.ToFunction({{0}}, __ctx)", flag | ParFlags.CheckErr)},
        { $"{Types.Enumerable}", (sb, oname, name, flag, ti) => ParamCheck(sb, oname, name, $"{Types.MinamoIterator}.ToEnumerable(__ctx, {{0}})", flag | ParFlags.CheckErr)}
    }; 
    
    private readonly Dictionary<string, Func<string, string>> returnTypeConversions = new()
    {
        { "long", name => $"new {Types.MinamoInteger}({name})" },
        { "long?", name => $"({name} is null ? ({Types.MinamoObject}){Types.MinamoNil}.Instance : new {Types.MinamoInteger}({name}.Value))" },
        { "int", name => $"new {Types.MinamoInteger}({name})" },
        { "int?", name => $"({name} is null ? ({Types.MinamoObject}){Types.MinamoNil}.Instance : new {Types.MinamoInteger}({name}.Value))" },
        { "string", name => $"({name} is null ? ({Types.MinamoObject}){Types.MinamoNil}.Instance : new {Types.MinamoString}({name}))" },
        { "char", name => $"new {Types.MinamoChar}({name})" },
        { "char?", name => $"({name} is null ? ({Types.MinamoObject}){Types.MinamoNil}.Instance : new {Types.MinamoChar}({name}.Value))" },
        { "double", name => $"new {Types.MinamoFloat}({name})" },
        { "double?", name => $"({name} is null ? ({Types.MinamoObject}){Types.MinamoNil}.Instance : new {Types.MinamoFloat}({name}.Value))" },
        { "float", name => $"new {Types.MinamoFloat}({name})" },
        { "float?", name => $"({name} is null ? ({Types.MinamoObject}){Types.MinamoNil}.Instance : new {Types.MinamoFloat}({name}.Value))" },
        { "bool", name => $"({name} ? {Types.MinamoBool}.True : {Types.MinamoBool}.False)" },
        { "bool?", name => $"({name} is null ? ({Types.MinamoObject}){Types.MinamoNil}.Instance : ({name}.Value ? {Types.MinamoBool}.True : {Types.MinamoBool}.False))" },
        { $"{Types.MinamoObject}", name => $"({name} ?? {Types.MinamoNil}.Instance)" },
        { $"{Types.Enumerable}", name => $"{Types.MinamoIterator}.Create({name})" },
        { $"{Types.Enumerable}?", name => $"({name} is null ? ({Types.MinamoObject}){Types.MinamoNil}.Instance : {Types.MinamoIterator}.Create({name})" },
        { $"{Types.HashSet}", name => $"new {Types.MinamoSet}({name})"},
        { $"{Types.HashSet}?", name => $"({name} is null ? ({Types.MinamoObject}){Types.MinamoNil}.Instance : new {Types.MinamoSet}({name})" }
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
        context.RegisterSourceOutput(targets.Collect(), (productionContext, values) =>
        {
            if (values.Length > 0)
            {
                productionContext.AddSource("GeneratedBuiltinFunction.cs", Microsoft.CodeAnalysis.Text.SourceText.From(SharedSource, Encoding.UTF8));
            }
        });
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
            if (!GeneratorSupport.IsDeclaredOn(attribute, declaration)) continue;
            var attributeName = attribute.AttributeClass?.ToDisplayString();

            if (attributeName == "Minamo.Codegen.MinamoTypeAttribute")
            {
                return new(type, GeneratorTargetKind.Type);
            }

            if (attributeName == "Minamo.Codegen.GeneratedModuleAttribute")
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

        foreach (var importedNamespace in new[] { "Minamo.Runtime", "Minamo.Runtime.Types" })
        {
            if (unit?.Usings.Any(us => us.Alias is null && us.Name?.ToString() == importedNamespace) != true)
            {
                builder.AppendLine($"using {importedNamespace};");
            }
        }

        builder.AppendLine($"namespace {t.ContainingNamespace}.Internal");
        builder.StartBlock();
        builder.AppendLine($"internal static class {t.Name}GeneratedCalls");
        builder.StartBlock();
        var methods = new List<(string name, bool instance)>();
        var factories = new List<string>();

        foreach (IMethodSymbol m in t.GetMembers().OfType<IMethodSymbol>())
        {
            if (!ProcessMethod(ctx, t, m, builder, methods, factories))
            {
                return;
            }
        }

        if (factories.Count > 0)
        {
            builder.AppendLine($"internal static {Types.MinamoFunction} Create(int id)");
            builder.StartBlock();
            builder.AppendLine("switch (id)");
            builder.StartBlock();
            foreach (var factory in factories)
            {
                builder.AppendLine(factory);
            }
            builder.AppendLine("default: throw new global::System.ArgumentOutOfRangeException(nameof(id));");
            builder.EndBlock();
            builder.EndBlock();
        }

        builder.EndBlock();
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
                builder.AppendLine($"partial class {Identifier(t.Name)}");
                builder.StartBlock();

                if (instanceMethods.Count > 0)
                {
                    builder.AppendLine($"protected override {Types.MinamoFunction} InitializeInstanceMember({Types.MinamoObject} self, string name, {Types.ExecutionContext} ctx) =>");
                    builder.Indent();
                    builder.AppendLine("name switch");
                    builder.StartBlock();
                    foreach (var (im, _) in instanceMethods)
                    {
                        builder.AppendLine($"{Literal(im)} => {t.Name}GeneratedCalls.Create({methods.IndexOf((im, true))}),");
                    }

                    builder.AppendLine("_ => base.InitializeInstanceMember(self, name, ctx)");
                    builder.Outdent();
                    builder.AppendLine("};");
                    builder.Outdent();
                }

                if (staticMethods.Count > 0)
                {
                    builder.AppendLine($"protected override {Types.MinamoFunction} InitializeStaticMember(string name, {Types.ExecutionContext} ctx) =>");
                    builder.Indent();
                    builder.AppendLine("name switch");
                    builder.StartBlock();
                    foreach (var (im, _) in staticMethods)
                    {
                        builder.AppendLine($"{Literal(im)} => {t.Name}GeneratedCalls.Create({methods.IndexOf((im, false))}),");
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
                builder.AppendLine($"partial class {Identifier(t.Name)}");
                builder.StartBlock();
                builder.AppendLine("protected override void InitializeMembers()");
                builder.StartBlock();

                foreach (var m in staticMethods)
                {
                    builder.AppendLine($"Add({Literal(m)}, {t.Name}GeneratedCalls.Create({methods.IndexOf((m, false))}));");
                }

                builder.EndBlock();
                builder.EndBlock();
            }
        }

        builder.EndBlock();
        ctx.AddSource($"{GeneratorSupport.HintName(t)}.generated.cs", builder.ToSourceText());
    }

    private bool ProcessMethod(SourceProductionContext ctx, INamedTypeSymbol t, IMethodSymbol m, CodeWriter builder, List<(string name, bool instance)> methods, List<string> factories)
    {
        var attr = m.GetAttributes().FirstOrDefault(a => a.AttributeClass.BaseType.ToString() == Types.MinamoMemberAttribute);

        if (attr is not null)
        {
            if (!m.IsStatic)
            {
                return Error(ctx, $"Method \"{m.Name}\" is not static. An underlying method for a Minamo type should be declarated as static.");
            }

            if (m.DeclaredAccessibility != Accessibility.Public && m.DeclaredAccessibility != Accessibility.Internal)
            {
                return Error(ctx, $"Method \"{m.Name}\" is private. An underlying method for a Minamo type should be declarated as public or internal.");
            }

            var methodNameData = attr.ConstructorArguments.Length == 1 ? attr.ConstructorArguments[0].Value : null;

            if (methodNameData is null || methodNameData is not string methodName)
            {
                methodName = m.Name;
            }

            var isInstance = attr.AttributeClass.ToString() is Types.MinamoMethodAttribute or Types.MinamoPropertyAttribute;
            var isProperty = attr.AttributeClass.ToString() is Types.MinamoPropertyAttribute or Types.MinamoStaticPropertyAttribute;
            var flags = (!isInstance ? MethodFlags.Static : MethodFlags.None) | (isProperty ? MethodFlags.Property : MethodFlags.None);

            if (ProcessMethod(ctx, t, m, builder, methodName, flags, factories))
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

    private bool ProcessMethod(SourceProductionContext ctx, INamedTypeSymbol t, IMethodSymbol m, CodeWriter builder, string methodName, MethodFlags implFlags, List<string> factories)
    {
        var isStatic = (implFlags & MethodFlags.Static) == MethodFlags.Static;
        var prefix = isStatic ? "st" : "in";
        var callName = $"{prefix}_{GeneratorSupport.EncodeName(methodName)}";
        var isProperty = (implFlags & MethodFlags.Property) == MethodFlags.Property;
        var hasContext = m.Parameters.Length > 0 && m.Parameters[0].Type.ToString() == Types.ExecutionContext;
        var parameters = m.Parameters.Skip((hasContext ? 1 : 0) + (isStatic ? 0 : 1)).ToArray();
        var arity = parameters.Length;
        // Optional and variable arguments still use the normal argument layout and default filling.
        var direct = arity <= 2 && parameters.All(parameter =>
            !parameter.IsParams
            && !parameter.HasExplicitDefaultValue
            && !parameter.GetAttributes().Any(attribute =>
                attribute.AttributeClass.Name is "VarArgAttribute" or "DefaultAttribute"));
        var extra = direct
            ? string.Concat(Enumerable.Range(0, arity).Select(index => $", {Types.MinamoObject} __arg{index}"))
            : $", {Types.MinamoObject}[] __args";
        var functionClass = "global::Minamo.Generated.GeneratedBuiltinFunction" + (direct ? arity.ToString() : "");
        builder.AppendLine($"internal static {Types.MinamoObject} Call_{callName}({Types.ExecutionContext} __ctx, {Types.MinamoObject} __self{extra})");
        builder.StartBlock();
        var pars = EmitParameters(ctx, builder, t, m, isStatic, false, out var varArgIndex, direct);
        if (pars is null || !EmitReturnType(ctx, builder, t, m))
        {
            return false;
        }

        builder.EndBlock();
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
                sb.Append($"new {Types.Par}({Literal(name)}");

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

        var reusePropertyCall = isProperty && direct && arity == 0;
        var propertyCall = isProperty
            ? $"{(reusePropertyCall ? "Call" : "Property")}_{callName}"
            : "null";
        factories.Add($"case {factories.Count}: return new {functionClass}({Literal(methodName)}, {sb}, {varArgIndex}, Call_{callName}, Create, {factories.Count}, {propertyCall});");
        if (isProperty && !reusePropertyCall)
        {
            builder.AppendLine($"internal static {Types.MinamoObject} Property_{callName}({Types.ExecutionContext} __ctx, {Types.MinamoObject} __arg)");
            builder.StartBlock();
            EmitParameters(ctx, builder, t, m, isStatic, true, out _);
            EmitReturnType(ctx, builder, t, m);
            builder.EndBlock();
        }

        return true;
    }

    private List<(string name, string def, bool vararg)> EmitParameters(SourceProductionContext ctx, CodeWriter builder, ITypeSymbol t, IMethodSymbol m, bool isStatic, bool specialCall, out int varArgIndex, bool directCall = false)
    {
        var hasContext = m.Parameters.Length > 0 && m.Parameters[0].Type.ToString() == Types.ExecutionContext;
        var pars = new List<(string name, string def, bool vararg)>();
        varArgIndex = -1;

        for (var i = 0; i < m.Parameters.Length; i++)
        {
            var mp = m.Parameters[i];
            var localName = $"__p{i}";
            var parName = mp.Name;
            var parNameAttr = m.Parameters[i].GetAttributes().FirstOrDefault(a => a.AttributeClass.Name == "ParameterNameAttribute");

            if (parNameAttr is not null)
            {
                parName = parNameAttr.ConstructorArguments[0].Value.ToString();
            }

            if (hasContext && i == 0) //First parameter is a context
            {
                builder.AppendLine($"var {localName} = __ctx;");
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
            var sourceParName = firstParam && !isStatic
                ? "__self"
                : directCall ? $"__arg{i + indexShift}" : $"__args[{i + indexShift}]";

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
                conv1(builder, sourceParName, localName, flags, mp.Type);
            }
            else if (mp.Type is IArrayTypeSymbol arr)
            {
                var elementType = arr.ElementType;
                var res = ParamCheckArray(sourceParName, localName, flags, mp.Type, builder, nullable);

                if (!res)
                {
                    Error(ctx, $"Parameter type \"{typeName}\" is not supported.");
                    return null;
                }
            }
            else if (IsMinamoObject(mp.Type))
            {
                if ((firstParam && !isStatic) || (vararg && typeName == Types.MinamoTuple))
                {
                    builder.AppendLine($"var {localName} = ({typeName}){sourceParName};");
                }
                else
                {
                    ConvertToMinamoObject(builder, typeName, sourceParName, localName, nullable);
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
                $"return global::Minamo.Hosting.MinamoCommandConvert.FromAwaitable({t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{Identifier(m.Name)}({string.Join(", ", Enumerable.Range(0, m.Parameters.Length).Select(index => $"__p{index}"))}));");
            return true;
        }

        if (m.ReturnType.ToString() != "void")
        {
            var returnTypeName = m.ReturnType.GetSafeName();

            builder.AppendLine($"var __ret = {t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{Identifier(m.Name)}({string.Join(", ", Enumerable.Range(0, m.Parameters.Length).Select(index => $"__p{index}"))});");

            if (returnTypeConversions.TryGetValue(returnTypeName, out var conv2))
            {
                builder.AppendLine($"return {conv2("__ret")};");
            }
            else if (IsMinamoObject(m.ReturnType))
            {
                builder.AppendLine($"return __ret ?? ({Types.MinamoObject}){Types.MinamoNil}.Instance;");
            }
            else if (m.ReturnType is IArrayTypeSymbol arr)
            {
                builder.AppendLine($"if (__ret is null) return {Types.MinamoNil}.Instance;");
                if (IsMinamoObject(arr.ElementType))
                {
                    builder.AppendLine($"return new {Types.MinamoArray}(__ret);");
                }
                else
                {
                    builder.AppendLine($"var __arr = new {Types.MinamoObject}[__ret.Length];");
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
                    builder.AppendLine($"return new {Types.MinamoArray}(__arr);");
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
            builder.AppendLine($"{t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{Identifier(m.Name)}({string.Join(", ", Enumerable.Range(0, m.Parameters.Length).Select(index => $"__p{index}"))});");
            builder.AppendLine($"return {Types.MinamoNil}.Instance;");
            return true;
        }
    }

    private static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);

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

    private void ConvertToMinamoObject(CodeWriter builder, string targetType, string oldVar, string newVar, bool nullable = false)
    {
        if (!nullable)
        {
            builder.AppendLine($"if ({oldVar} is not {targetType} {newVar}) return {Error(oldVar)};");
            return;
        }

        builder.AppendLine($"{targetType} {newVar} = default;");
        builder.AppendLine($"if ({oldVar}.TypeId != {Types.Minamo}.Nil)");
        builder.StartBlock();
        builder.AppendLine($"if ({oldVar} is not {targetType} __tmp_{newVar}) return {Error(oldVar)};");
        builder.AppendLine($"{newVar} = __tmp_{newVar};");
        builder.EndBlock();
    }
}
