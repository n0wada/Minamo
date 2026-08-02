using Spellkit.Hosting;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Spellkit.Library;

internal static class ExtensionLibraryLoader
{
    private const string ManifestName = "spellkit.json";

    internal static SpellkitHost AddConfiguredExtensionLibraries(this SpellkitHost host)
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
            var libraries = assembly.GetExportedTypes()
                .Where(type => !type.IsAbstract && typeof(ISpellkitLibrary).IsAssignableFrom(type))
                .Select(type => (ISpellkitLibrary?)Activator.CreateInstance(type))
                .Where(library => library is not null)
                .Cast<ISpellkitLibrary>()
                .ToArray();
            if (libraries.Length == 0)
            {
                throw new InvalidOperationException($"Extension assembly '{extension}' does not export an ISpellkitLibrary.");
            }

            foreach (var library in libraries)
            {
                library.Register(host);
            }
        }

        return host;
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
