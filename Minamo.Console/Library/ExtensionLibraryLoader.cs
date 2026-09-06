using Minamo.Hosting;
using Minamo.Linker;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Minamo.Library;

internal static class ExtensionLibraryLoader
{
    private const string ManifestName = "minamo.json";

    internal static MinamoHost AddConfiguredExtensionLibraries(this MinamoHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var manifestPath = Path.Combine(AppContext.BaseDirectory, ManifestName);
        if (!File.Exists(manifestPath))
        {
            return host;
        }

        var manifest = JsonSerializer.Deserialize<LibraryManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })
            ?? throw new InvalidOperationException($"Library manifest '{manifestPath}' is empty.");
        foreach (var extension in manifest.Extensions ?? [])
        {
            var assemblyPath = ResolveAssemblyPath(manifestPath, extension);
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            RegisterAssembly(host, assembly, extension);
        }

        return host;
    }

    internal static void RegisterAssembly(MinamoHost host, Assembly assembly, string displayName)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(assembly);

        var modules = assembly.GetExportedTypes()
            .Where(type => type.GetCustomAttribute<MinamoModuleAttribute>(inherit: false) is not null)
            .ToArray();
        if (modules.Length == 0)
        {
            throw new InvalidOperationException(
                $"Extension assembly '{displayName}' does not export a public MinamoModule.");
        }

        foreach (var module in modules)
        {
            RegisterModule(host, module, displayName);
        }
    }

    private static void RegisterModule(MinamoHost host, Type module, string displayName)
    {
        var extensionTypeName = module.Namespace is null
            ? module.Name + "HostingExtensions"
            : module.Namespace + "." + module.Name + "HostingExtensions";
        var extensions = module.Assembly.GetType(extensionTypeName)
            ?? throw new InvalidOperationException(
                $"Minamo module '{module.FullName}' in extension assembly '{displayName}' "
                + "does not contain generated hosting registration code.");

        MethodInfo registration;
        object?[] arguments;
        if ((module.IsAbstract && module.IsSealed)
            || typeof(ForeignUnit).IsAssignableFrom(module))
        {
            registration = extensions.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SingleOrDefault(method =>
                    method.ReturnType == typeof(MinamoHost)
                    && method.GetParameters() is [{ ParameterType: var parameterType }]
                    && parameterType == typeof(MinamoHost))
                ?? throw new InvalidOperationException(
                    $"Minamo module '{module.FullName}' in extension assembly '{displayName}' "
                    + "does not expose a generated static registration method.");
            arguments = [host];
        }
        else
        {
            var instance = Activator.CreateInstance(module)
                ?? throw new InvalidOperationException(
                    $"Minamo module '{module.FullName}' in extension assembly '{displayName}' "
                    + "requires a public parameterless constructor.");
            registration = extensions.GetMethod(
                "AddModule",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                binder: null,
                types: [typeof(MinamoHost), module],
                modifiers: null)
                ?? throw new InvalidOperationException(
                    $"Minamo module '{module.FullName}' in extension assembly '{displayName}' "
                    + "does not expose a generated instance registration method.");
            arguments = [host, instance];
        }

        try
        {
            registration.Invoke(null, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
        {
            throw new InvalidOperationException(
                $"Failed to register Minamo module '{module.FullName}' from extension assembly '{displayName}'.",
                inner);
        }
    }

    private static string ResolveAssemblyPath(string manifestPath, string? assembly)
    {
        if (string.IsNullOrWhiteSpace(assembly))
        {
            throw new InvalidOperationException("Each enabled extension must specify an assembly.");
        }

        var directory = Path.GetDirectoryName(manifestPath)!;
        var path = Path.IsPathRooted(assembly)
            ? Path.GetFullPath(assembly)
            : Path.GetFullPath(Path.Combine(directory, assembly));
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Extension assembly '{assembly}' was not found.");
        }

        return path;
    }

    private sealed class LibraryManifest
    {
        public List<string>? Extensions { get; init; }
    }
}
