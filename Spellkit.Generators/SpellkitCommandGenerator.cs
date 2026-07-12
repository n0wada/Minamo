using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Spellkit.Generators;

[Generator]
public sealed class SpellkitCommandGenerator : IIncrementalGenerator
{
    private const string ModuleAttribute = "Spellkit.Hosting.SpellkitModuleAttribute";
    private const string CommandAttribute = "Spellkit.Hosting.SpellkitCommandAttribute";
    private const string PropertyAttribute = "Spellkit.Hosting.SpellkitPropertyAttribute";
    private const string ForeignTypeAttribute = "Spellkit.Hosting.SpellkitForeignTypeAttribute";
    private const string CommandContext = "Spellkit.Hosting.SpellkitCommandContext";
    private const string ForeignType = "Spellkit.Runtime.Types.SpkForeignTypeInfo";
    private const string ForeignUnit = "Spellkit.Linker.ForeignUnit";

    private static readonly DiagnosticDescriptor InvalidModule = Error(
        "SPKH001", "Invalid host module", "SpellkitModule on '{0}' requires a non-empty module name.");
    private static readonly DiagnosticDescriptor InvalidMethod = Error(
        "SPKH002", "Invalid host command", "Method '{0}' cannot be exposed as a SpellkitCommand: {1}");
    private static readonly DiagnosticDescriptor DuplicateCommand = Error(
        "SPKH003", "Duplicate host command", "Module '{0}' contains more than one command named '{1}'.");
    private static readonly DiagnosticDescriptor InvalidParameter = Error(
        "SPKH004", "Invalid host command parameter", "Parameter '{0}' on command '{1}' is not supported: {2}");
    private static readonly DiagnosticDescriptor InvalidForeignType = Error(
        "SPKH005", "Invalid foreign host type", "Type '{0}' cannot be registered as a foreign host type: {1}");
    private static readonly DiagnosticDescriptor InvalidForeignUnit = Error(
        "SPKH006", "Invalid foreign host unit", "Module unit '{0}' is invalid: {1}");
    private static readonly DiagnosticDescriptor InvalidProperty = Error(
        "SPKH007", "Invalid host property", "Property '{0}' cannot be exposed as a SpellkitProperty: {1}");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modules = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
            static (syntaxContext, cancellationToken) => GetModule(syntaxContext, cancellationToken))
            .Where(static module => module is not null);

        context.RegisterSourceOutput(
            modules,
            static (productionContext, module) => Emit(productionContext, module!));
    }

    private static INamedTypeSymbol GetModule(
        GeneratorSyntaxContext context,
        System.Threading.CancellationToken cancellationToken)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        var type = context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;

        return type?.GetAttributes().Any(IsModuleAttribute) == true ? type : null!;
    }

    private static void Emit(SourceProductionContext context, INamedTypeSymbol moduleType)
    {
        var moduleAttribute = moduleType.GetAttributes().First(IsModuleAttribute);
        var moduleName = moduleAttribute.ConstructorArguments.FirstOrDefault().Value as string;

        if (string.IsNullOrWhiteSpace(moduleName))
        {
            Report(context, InvalidModule, moduleType, moduleType.Name);
            return;
        }

        if (moduleType.IsGenericType)
        {
            Report(context, InvalidMethod, moduleType, moduleType.Name, "generic module classes are not supported");
            return;
        }

        var commands = new List<CommandSpec>();
        var properties = new List<PropertySpec>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var method in moduleType.GetMembers().OfType<IMethodSymbol>())
        {
            var attribute = method.GetAttributes().FirstOrDefault(IsCommandAttribute);
            if (attribute is null)
            {
                continue;
            }

            var commandName = attribute.ConstructorArguments.Length == 1
                ? attribute.ConstructorArguments[0].Value as string
                : null;
            commandName = string.IsNullOrWhiteSpace(commandName) ? method.Name : commandName;

            if (!ValidateMethod(context, method))
            {
                return;
            }

            var description = attribute.NamedArguments
                .FirstOrDefault(pair => pair.Key == "Description").Value.Value as string;
            var capability = attribute.NamedArguments
                .FirstOrDefault(pair => pair.Key == "Capability").Value.Value as string;
            var typeName = attribute.NamedArguments
                .FirstOrDefault(pair => pair.Key == "Type").Value.Value as string;
            typeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName;

            if (!names.Add($"{typeName}\0{commandName}"))
            {
                Report(context, DuplicateCommand, method, moduleName!, commandName!);
                return;
            }

            commands.Add(new(
                method,
                commandName!,
                description,
                capability,
                typeName));
        }

        foreach (var property in moduleType.GetMembers().OfType<IPropertySymbol>())
        {
            var attribute = property.GetAttributes().FirstOrDefault(IsPropertyAttribute);
            if (attribute is null)
            {
                continue;
            }

            var propertyName = attribute.ConstructorArguments.Length == 1
                ? attribute.ConstructorArguments[0].Value as string
                : null;
            propertyName = string.IsNullOrWhiteSpace(propertyName) ? property.Name : propertyName;

            if (!ValidateProperty(context, property))
            {
                return;
            }

            if (!names.Add($"\0{propertyName}")
                || !names.Add($"\0set_{propertyName}"))
            {
                Report(context, DuplicateCommand, property, moduleName!, propertyName!);
                return;
            }

            properties.Add(new(
                property,
                propertyName!,
                attribute.NamedArguments
                    .FirstOrDefault(pair => pair.Key == "Description").Value.Value as string,
                attribute.NamedArguments
                    .FirstOrDefault(pair => pair.Key == "Capability").Value.Value as string));
        }

        var foreignTypes = GetForeignTypes(context, moduleType);
        if (foreignTypes is null)
        {
            return;
        }

        var isForeignUnit = InheritsFrom(moduleType, ForeignUnit);
        if (isForeignUnit)
        {
            var constructor = moduleType.InstanceConstructors.FirstOrDefault(candidate =>
                candidate.Parameters.Length == 0
                && candidate.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);
            if (constructor is null || moduleType.IsAbstract)
            {
                Report(context, InvalidForeignUnit, moduleType, moduleType.Name,
                    "a non-abstract type with an accessible parameterless constructor is required");
                return;
            }

            if (commands.Count != 0 || properties.Count != 0 || foreignTypes.Length != 0)
            {
                Report(context, InvalidForeignUnit, moduleType, moduleType.Name,
                    "a custom ForeignUnit cannot also declare generated commands or foreign types");
                return;
            }
        }

        var writer = new CodeWriter();
        var namespaceName = moduleType.ContainingNamespace.IsGlobalNamespace
            ? null
            : moduleType.ContainingNamespace.ToDisplayString();
        var extensionName = GetExtensionName(moduleType);

        writer.AppendLine("// <auto-generated/>");
        writer.AppendLine("#nullable enable");
        if (namespaceName is not null)
        {
            writer.AppendLine($"namespace {namespaceName};");
            writer.AppendLine();
        }

        writer.Block($"public static class {extensionName}", typeWriter =>
        {
            typeWriter.Block(
                $"public static global::Spellkit.Hosting.SpellkitHost Add{Sanitize(moduleType.Name)}(this global::Spellkit.Hosting.SpellkitHost host)",
                methodWriter =>
                {
                    EmitRegistration(
                        methodWriter,
                        moduleType,
                        moduleName!,
                        commands,
                        properties,
                        foreignTypes,
                        isForeignUnit,
                        instance: null);
                });

            if (!moduleType.IsStatic && !isForeignUnit)
            {
                typeWriter.AppendLine();
                typeWriter.Block(
                    $"public static global::Spellkit.Hosting.SpellkitHost AddModule(this global::Spellkit.Hosting.SpellkitHost host, {TypeName(moduleType)} instance)",
                    methodWriter =>
                    {
                        methodWriter.AppendLine(
                            "global::System.ArgumentNullException.ThrowIfNull(instance);");
                        EmitRegistration(
                            methodWriter,
                            moduleType,
                            moduleName!,
                            commands,
                            properties,
                            foreignTypes,
                            isForeignUnit: false,
                            instance: "instance");
                    });
            }
        });

        context.AddSource($"{GetHintName(moduleType)}.SpellkitCommands.generated.cs", writer.ToSourceText());
    }

    private static void EmitRegistration(
        CodeWriter writer,
        INamedTypeSymbol moduleType,
        string moduleName,
        IReadOnlyList<CommandSpec> commands,
        IReadOnlyList<PropertySpec> properties,
        IReadOnlyList<ITypeSymbol> foreignTypes,
        bool isForeignUnit,
        string instance)
    {
        writer.AppendLine($"return host.Module({Literal(moduleName)}, module =>");
        writer.StartBlock();
        if (isForeignUnit)
        {
            writer.AppendLine($"module.Unit(() => new {TypeName(moduleType)}());");
        }
        else
        {
            foreach (var foreignType in foreignTypes)
            {
                writer.AppendLine($"module.ForeignType(() => new {TypeName(foreignType)}());");
            }

            var groups = commands
                .Where(command => command.Type is not null)
                .Select(command => command.Type)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var group in groups)
            {
                writer.AppendLine($"var {GroupVariable(group)} = module.Type({Literal(group)});");
            }

            foreach (var command in commands)
            {
                EmitCommand(
                    writer,
                    moduleType,
                    command,
                    command.Type is null ? "module" : GroupVariable(command.Type),
                    instance);
            }

            foreach (var property in properties)
            {
                EmitProperty(writer, moduleType, property, instance);
            }
        }
        writer.EndBlock(");");
    }

    private static ITypeSymbol[] GetForeignTypes(SourceProductionContext context, INamedTypeSymbol moduleType)
    {
        var types = new List<ITypeSymbol>();

        foreach (var attribute in moduleType.GetAttributes().Where(IsForeignTypeAttribute))
        {
            var type = attribute.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol;
            if (type is null || type.IsAbstract || !InheritsFrom(type, ForeignType))
            {
                Report(context, InvalidForeignType, moduleType, type?.ToDisplayString() ?? "<unknown>",
                    "the type must be a non-abstract SpkForeignTypeInfo");
                return null!;
            }

            var constructor = type.InstanceConstructors.FirstOrDefault(candidate =>
                candidate.Parameters.Length == 0
                && candidate.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);
            if (constructor is null)
            {
                Report(context, InvalidForeignType, type, type.ToDisplayString(),
                    "an accessible parameterless constructor is required");
                return null!;
            }

            types.Add(type);
        }

        return types.ToArray();
    }

    private static bool ValidateMethod(SourceProductionContext context, IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Ordinary)
        {
            return Invalid(context, method, "only ordinary methods are supported");
        }

        if (method.IsGenericMethod)
        {
            return Invalid(context, method, "generic methods are not supported");
        }

        if (method.IsAsync && method.ReturnsVoid)
        {
            return Invalid(context, method, "async void commands are not supported");
        }

        if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
        {
            return Invalid(context, method, "the method must be public or internal");
        }

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            return Invalid(context, method, "ref return values are not supported");
        }

        var contextCount = 0;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                Report(context, InvalidParameter, parameter, parameter.Name, method.Name, "ref, in, and out parameters are not supported");
                return false;
            }

            if (parameter.IsParams)
            {
                Report(context, InvalidParameter, parameter, parameter.Name, method.Name, "params arrays are not supported");
                return false;
            }

            if (IsCommandContext(parameter.Type))
            {
                contextCount++;
                if (contextCount > 1 || parameter.HasExplicitDefaultValue)
                {
                    Report(context, InvalidParameter, parameter, parameter.Name, method.Name,
                        "only one non-optional SpellkitCommandContext parameter is allowed");
                    return false;
                }
            }

            if (parameter.HasExplicitDefaultValue && !TryDefaultValue(parameter, out _))
            {
                Report(context, InvalidParameter, parameter, parameter.Name, method.Name, "the default value cannot be emitted as C#");
                return false;
            }
        }

        return true;
    }

    private static bool Invalid(SourceProductionContext context, IMethodSymbol method, string reason)
    {
        Report(context, InvalidMethod, method, method.Name, reason);
        return false;
    }

    private static bool ValidateProperty(
        SourceProductionContext context,
        IPropertySymbol property)
    {
        if (property.IsIndexer)
        {
            return Invalid(context, property, "indexers are not supported");
        }

        if (property.GetMethod is null)
        {
            return Invalid(context, property, "a getter is required");
        }

        if (property.GetMethod.DeclaredAccessibility is not
            (Accessibility.Public or Accessibility.Internal))
        {
            return Invalid(context, property, "the getter must be public or internal");
        }
        if (property.ReturnsByRef || property.ReturnsByRefReadonly)
        {
            return Invalid(context, property, "ref return values are not supported");
        }

        if (property.SetMethod is not null
            && property.SetMethod.DeclaredAccessibility is not
                (Accessibility.Public or Accessibility.Internal))
        {
            return Invalid(context, property, "the setter must be public or internal");
        }
        if (property.SetMethod?.IsInitOnly == true)
        {
            return Invalid(context, property, "init-only setters are not supported");
        }

        return true;
    }

    private static bool Invalid(
        SourceProductionContext context,
        IPropertySymbol property,
        string reason)
    {
        Report(context, InvalidProperty, property, property.Name, reason);
        return false;
    }

    private static void EmitCommand(
        CodeWriter writer,
        INamedTypeSymbol moduleType,
        CommandSpec command,
        string receiver,
        string instance = null)
    {
        var method = command.Method;
        var exposedParameters = method.Parameters.Where(parameter => !IsCommandContext(parameter.Type)).ToArray();

        writer.AppendLine($"{receiver}.RawCommand({Literal(command.Name)}, {OptionalLiteral(command.Description)}, {OptionalLiteral(command.Capability)}, context =>");
        writer.StartBlock();

        var target = method.IsStatic
            ? TypeName(moduleType)
            : instance ?? $"context.Host<{TypeName(moduleType)}>()";
        var argumentIndex = 0;
        var arguments = method.Parameters.Select(parameter =>
        {
            if (IsCommandContext(parameter.Type))
            {
                return "context";
            }

            return ArgumentConversion(parameter.Type, argumentIndex++);
        });
        var invocation = $"{target}.{Identifier(method.Name)}({string.Join(", ", arguments)})";

        if (IsAsyncReturnType(method.ReturnType))
        {
            writer.AppendLine(
                $"return global::Spellkit.Hosting.SpellkitCommandConvert.FromAwaitable({invocation});");
        }
        else if (method.ReturnsVoid)
        {
            writer.AppendLine(invocation + ";");
            writer.AppendLine("return global::Spellkit.Runtime.Types.SpkNil.Instance;");
        }
        else if (ReturnsDirectSpkObject(method.ReturnType))
        {
            writer.AppendLine($"return {invocation} ?? global::Spellkit.Runtime.Types.SpkNil.Instance;");
        }
        else
        {
            writer.AppendLine("var result = " + invocation + ";");
            writer.AppendLine("return " + ReturnConversion(method.ReturnType, "result") + ";");
        }

        writer.EndBlock(exposedParameters.Length == 0 ? ");" : ",");

        for (var i = 0; i < exposedParameters.Length; i++)
        {
            var parameter = exposedParameters[i];
            var suffix = i == exposedParameters.Length - 1 ? ");" : ",";
            var metadata = parameter.HasExplicitDefaultValue
                ? $"global::Spellkit.Hosting.SpellkitCommandParameter.Optional<{TypeName(parameter.Type)}>({Literal(parameter.Name)}, {DefaultValue(parameter)})"
                : $"global::Spellkit.Hosting.SpellkitCommandParameter.Required<{TypeName(parameter.Type)}>({Literal(parameter.Name)})";
            writer.AppendLine(metadata + suffix);
        }
    }

    private static void EmitProperty(
        CodeWriter writer,
        INamedTypeSymbol moduleType,
        PropertySpec property,
        string instance)
    {
        var symbol = property.Property;
        var target = symbol.IsStatic
            ? TypeName(moduleType)
            : instance ?? $"context.Host<{TypeName(moduleType)}>()";

        writer.AppendLine(
            $"module.RawProperty({Literal(property.Name)}, "
            + $"{OptionalLiteral(property.Description)}, "
            + $"{OptionalLiteral(property.Capability)}, context =>");
        writer.StartBlock();
        writer.AppendLine($"var result = {target}.{Identifier(symbol.Name)};");
        writer.AppendLine("return " + ReturnConversion(symbol.Type, "result") + ";");
        writer.EndBlock(",");

        if (symbol.SetMethod is null)
        {
            writer.AppendLine("null, null);");
            return;
        }

        writer.AppendLine("context =>");
        writer.StartBlock();
        writer.AppendLine(
            $"{target}.{Identifier(symbol.Name)} = "
            + $"{ArgumentConversion(symbol.Type, 0)};");
        writer.AppendLine("return global::Spellkit.Runtime.Types.SpkNil.Instance;");
        writer.EndBlock(",");
        writer.AppendLine(
            $"global::Spellkit.Hosting.SpellkitCommandParameter.Required<{TypeName(symbol.Type)}>(\"value\"));");
    }

    private static string ArgumentConversion(ITypeSymbol type, int index)
    {
        var rawArgument = $"context.RawArgument({index.ToString(CultureInfo.InvariantCulture)})";
        var context = "context.ExecutionContext";
        var nullableValueType = NullableValueType(type);

        if (nullableValueType is not null)
        {
            var converted = ArgumentConversion(nullableValueType, rawArgument, context);
            return $"{rawArgument} is global::Spellkit.Runtime.Types.SpkNil "
                + $"? default({TypeName(type)}) : {converted}";
        }

        if (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var converted = ArgumentConversion(type, rawArgument, context);
            return $"{rawArgument} is global::Spellkit.Runtime.Types.SpkNil "
                + $"? null : {converted}";
        }

        return ArgumentConversion(type, rawArgument, context);
    }

    private static string ArgumentConversion(
        ITypeSymbol type,
        string rawArgument,
        string context)
    {
        if (ReturnsDirectSpkObject(type))
        {
            return type.SpecialType == SpecialType.None && TypeName(type) != "global::Spellkit.Runtime.Types.SpkObject"
                ? $"({TypeName(type)}){rawArgument}"
                : rawArgument;
        }

        return type.SpecialType switch
        {
            SpecialType.System_Object => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToObject({context}, {rawArgument})",
            SpecialType.System_String => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToString({context}, {rawArgument})",
            SpecialType.System_Boolean => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToBoolean({context}, {rawArgument})",
            SpecialType.System_Byte => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToByte({context}, {rawArgument})",
            SpecialType.System_Int16 => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToInt16({context}, {rawArgument})",
            SpecialType.System_Int32 => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToInt32({context}, {rawArgument})",
            SpecialType.System_Int64 => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToInt64({context}, {rawArgument})",
            SpecialType.System_SByte => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToSByte({context}, {rawArgument})",
            SpecialType.System_UInt16 => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToUInt16({context}, {rawArgument})",
            SpecialType.System_UInt32 => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToUInt32({context}, {rawArgument})",
            SpecialType.System_UInt64 => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToUInt64({context}, {rawArgument})",
            SpecialType.System_Single => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToSingle({context}, {rawArgument})",
            SpecialType.System_Double => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToDouble({context}, {rawArgument})",
            SpecialType.System_Decimal => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToDecimal({context}, {rawArgument})",
            SpecialType.System_Char => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToChar({context}, {rawArgument})",
            _ => $"global::Spellkit.Hosting.SpellkitCommandConvert.ToObject<{TypeName(type)}>({context}, {rawArgument})"
        };
    }

    private static string ReturnConversion(ITypeSymbol type, string value)
    {
        if (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var nonNullable = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var direct = ReturnConversion(nonNullable, value);
            return $"{value} is null ? global::Spellkit.Runtime.Types.SpkNil.Instance : {direct}";
        }

        var target = NullableValueType(type);
        if (target is not null)
        {
            var direct = ReturnConversion(target, $"{value}.Value");
            return $"{value}.HasValue ? {direct} : global::Spellkit.Runtime.Types.SpkNil.Instance";
        }

        return type.SpecialType switch
        {
            SpecialType.System_String => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromString({value})",
            SpecialType.System_Boolean => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromBoolean({value})",
            SpecialType.System_Byte => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromInteger({value})",
            SpecialType.System_Int16 => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromInteger({value})",
            SpecialType.System_Int32 => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromInteger({value})",
            SpecialType.System_Int64 => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromInteger({value})",
            SpecialType.System_SByte => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromInteger({value})",
            SpecialType.System_UInt16 => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromInteger({value})",
            SpecialType.System_UInt32 => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromInteger({value})",
            SpecialType.System_UInt64 => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromObject<ulong>({value})",
            SpecialType.System_Single => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromFloat({value})",
            SpecialType.System_Double => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromFloat({value})",
            SpecialType.System_Decimal => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromFloat((double){value})",
            SpecialType.System_Char => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromChar({value})",
            _ => $"global::Spellkit.Hosting.SpellkitCommandConvert.FromObject<{TypeName(type)}>({value})"
        };
    }

    private static bool ReturnsDirectSpkObject(ITypeSymbol type) =>
        TypeName(type) == "global::Spellkit.Runtime.Types.SpkObject"
        || type is INamedTypeSymbol namedType && InheritsFrom(namedType, "Spellkit.Runtime.Types.SpkObject");

    private static ITypeSymbol NullableValueType(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : null;

    private static string DefaultValue(IParameterSymbol parameter)
    {
        TryDefaultValue(parameter, out var value);
        return value!;
    }

    private static bool TryDefaultValue(IParameterSymbol parameter, out string value)
    {
        var constant = parameter.ExplicitDefaultValue;
        if (constant is null)
        {
            value = "default!";
            return true;
        }

        if (parameter.Type.TypeKind == TypeKind.Enum)
        {
            value = $"({TypeName(parameter.Type)}){NumericLiteral(constant)}";
            return true;
        }

        switch (constant)
        {
            case string text:
                value = Literal(text);
                return true;
            case char character:
                value = SymbolDisplay.FormatLiteral(character, quote: true);
                return true;
            case bool boolean:
                value = boolean ? "true" : "false";
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                value = NumericLiteral(constant);
                return true;
            default:
                value = null;
                return false;
        }
    }

    private static string NumericLiteral(object value) => value switch
    {
        uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
        long number => number.ToString(CultureInfo.InvariantCulture) + "L",
        ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
        float number => number.ToString("R", CultureInfo.InvariantCulture) + "F",
        double number => number.ToString("R", CultureInfo.InvariantCulture) + "D",
        decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!
    };

    private static bool IsModuleAttribute(AttributeData attribute) =>
        attribute.AttributeClass?.ToDisplayString() == ModuleAttribute;

    private static bool IsCommandAttribute(AttributeData attribute) =>
        attribute.AttributeClass?.ToDisplayString() == CommandAttribute;

    private static bool IsPropertyAttribute(AttributeData attribute) =>
        attribute.AttributeClass?.ToDisplayString() == PropertyAttribute;

    private static bool IsForeignTypeAttribute(AttributeData attribute) =>
        attribute.AttributeClass?.ToDisplayString() == ForeignTypeAttribute;

    private static bool InheritsFrom(INamedTypeSymbol type, string baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == baseType)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCommandContext(ITypeSymbol type) =>
        type.ToDisplayString() == CommandContext;

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

    private static string TypeName(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Identifier(string value) =>
        SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None ? value : "@" + value;

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private static string OptionalLiteral(string value) => value is null ? "null" : Literal(value);

    private static string GetExtensionName(INamedTypeSymbol type) =>
        Sanitize(type.Name) + "HostingExtensions";

    private static string GetHintName(INamedTypeSymbol type) =>
        Sanitize(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    private static string GroupVariable(string group) => "__type_" + Sanitize(group);

    private static string Sanitize(string value)
    {
        var chars = value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray();
        return new string(chars);
    }

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, "Spellkit.Hosting", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static void Report(
        SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        ISymbol symbol,
        params object[] arguments) =>
        context.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            symbol.Locations.FirstOrDefault() ?? Location.None,
            arguments));

    private sealed class CommandSpec
    {
        public CommandSpec(
            IMethodSymbol method,
            string name,
            string description,
            string capability,
            string type) =>
            (Method, Name, Description, Capability, Type) =
            (method, name, description, capability, type);

        public IMethodSymbol Method { get; }

        public string Name { get; }

        public string Description { get; }

        public string Capability { get; }

        public string Type { get; }

    }

    private sealed class PropertySpec
    {
        public PropertySpec(
            IPropertySymbol property,
            string name,
            string description,
            string capability) =>
            (Property, Name, Description, Capability) =
            (property, name, description, capability);

        public IPropertySymbol Property { get; }
        public string Name { get; }
        public string Description { get; }
        public string Capability { get; }
    }
}
