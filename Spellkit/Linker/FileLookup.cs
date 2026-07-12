using Spellkit.Compiler;
using System.Collections.Generic;
using System.IO;

namespace Spellkit.Linker;

public sealed class FileLookup
{
    private const string LibDirectory = "lib";
    private const string LogFile = "spellkit_error.log";

    private readonly BuilderOptions options;
    private readonly string[] startupPaths;
    private readonly string[] standardPaths;
    private readonly string[] additionalPaths;
    private readonly bool allowCurrentPath;

    internal BuilderOptions BuilderOptions => options;

    internal static readonly FileLookup Default = new(BuilderOptions.Default(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>());

    internal FileLookup WithOptions(BuilderOptions replacement) =>
        new(
            replacement,
            startupPaths,
            standardPaths,
            additionalPaths,
            allowCurrentPath);

    public static FileLookupBuilder For(BuilderOptions options) => Standard(options);

    public static FileLookupBuilder Standard(BuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(options, includeDefaultStandardPaths: true, allowCurrentPath: true);
    }

    public static FileLookupBuilder Restricted(BuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(options, includeDefaultStandardPaths: false, allowCurrentPath: false);
    }

    private FileLookup(
        BuilderOptions options,
        string[] startupPaths,
        string[] standardPaths,
        string[] additionalPaths,
        bool allowCurrentPath = true)
    {
        this.options = options;
        this.startupPaths = startupPaths;
        this.standardPaths = standardPaths;
        this.additionalPaths = additionalPaths;
        this.allowCurrentPath = allowCurrentPath;
    }

    public static FileLookup Create(BuilderOptions options, string startupPath, string[]? additionalPaths = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var startupPaths = startupPath is not null
            ? new string[] { startupPath, Path.Combine(startupPath, LibDirectory) }
            : Array.Empty<string>();

        return new
        (
            options,
            startupPaths,
            GetBasePaths(),
            additionalPaths ?? Array.Empty<string>()
        );
    }

    public bool Find(string? currentPath, string fileName, out string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (options.LinkerLog is not null)
        {
            WriteToLog(new string('=', 80));
            WriteToLog($"Resolving module or assembly \"{fileName}\":");
        }

        if (allowCurrentPath
            && currentPath is not null
            && TryGetPathWithinRoot(currentPath, fileName, out fullPath)
            && File.Exists(fullPath))
        {
            if (options.LinkerLog is not null)
            {
                WriteToLog($"Found");
                WriteToLog($"Load from \"{fullPath}\"");
            }

            return true;
        }

        if (LookIn(fileName, startupPaths, out fullPath!)
            || LookIn(fileName, standardPaths, out fullPath!)
            || LookIn(fileName, additionalPaths, out fullPath!))
        {
            return true;
        }

        if (options.LinkerLog is not null)
        {
            WriteToLog($"Not found");
        }

        return false;
    }

    private bool LookIn(string fileName, string[] dirs, out string? path)
    {
        path = null;

        foreach (var p in dirs)
        {
            if (!TryGetPathWithinRoot(p, fileName, out path))
            {
                continue;
            }

            if (options.LinkerLog is not null)
            {
                WriteToLog($"Probing {path}");
            }

            if (File.Exists(path))
            {
                if (options.LinkerLog is not null)
                {
                    WriteToLog($"Found");
                    WriteToLog($"Load from \"{path}\"");
                }

                return true;
            }
        }

        return false;
    }

    private static bool TryGetPathWithinRoot(string root, string fileName, out string path)
    {
        path = string.Empty;

        if (Path.IsPathRooted(fileName))
        {
            return false;
        }

        try
        {
            var fullRoot = ResolvePathLinks(Path.GetFullPath(root));
            var candidate = ResolvePathLinks(
                Path.GetFullPath(Path.Combine(Path.GetFullPath(root), fileName)));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var rootWithSeparator = Path.TrimEndingDirectorySeparator(fullRoot)
                + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(rootWithSeparator, comparison))
            {
                return false;
            }

            path = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException
            or IOException
            or NotSupportedException
            or PathTooLongException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ResolvePathLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The path has no root.", nameof(path));
        var current = root;
        var relative = Path.GetRelativePath(root, fullPath);

        foreach (var segment in relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            FileSystemInfo? entry = Directory.Exists(next)
                ? new DirectoryInfo(next)
                : File.Exists(next)
                    ? new FileInfo(next)
                    : null;
            var target = entry?.ResolveLinkTarget(returnFinalTarget: true);
            current = target?.FullName ?? next;
        }

        return Path.GetFullPath(current);
    }

    private static string[] GetBasePaths()
    {
        var var = Environment.GetEnvironmentVariable("SPELLKIT_LIBS");

        if (string.IsNullOrEmpty(var))
        {
            return Array.Empty<string>();
        }

        return var.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private void WriteToLog(string str)
    {
        var fn = options.LinkerLog!;

        if (!Path.IsPathRooted(fn))
        {
            fn = Path.Combine(Environment.CurrentDirectory, fn);
        }

        try
        {
            File.AppendAllText(fn, str + Environment.NewLine);
        }
        catch (Exception ex)
        {
            //Attempt to log this error
            try
            {
                File.AppendAllLines(Path.Combine(Environment.CurrentDirectory, LogFile),
                    new[] {
                        new string('=', 80),
                        $"Unable to write log file \"{fn}\" because of an error:",
                        ex.Message
                    });
            }
            catch { } //If it doesn't work, don't fail
        }
    }

    public sealed class FileLookupBuilder
    {
        private readonly BuilderOptions options;
        private readonly List<string> startupPaths = new();
        private readonly List<string> standardPaths = new();
        private readonly List<string> additionalPaths = new();
        private bool includeDefaultStandardPaths;
        private bool allowCurrentPath;

        internal FileLookupBuilder(
            BuilderOptions options,
            bool includeDefaultStandardPaths,
            bool allowCurrentPath)
        {
            this.options = options;
            this.includeDefaultStandardPaths = includeDefaultStandardPaths;
            this.allowCurrentPath = allowCurrentPath;
        }

        public FileLookupBuilder AddStartupPath(string path)
        {
            AddWithLib(startupPaths, path);
            return this;
        }

        public FileLookupBuilder AddPath(string path)
        {
            Add(additionalPaths, path);
            return this;
        }

        public FileLookupBuilder AddPaths(params string[] paths)
        {
            ArgumentNullException.ThrowIfNull(paths);
            foreach (var path in paths)
            {
                AddPath(path);
            }

            return this;
        }

        public FileLookupBuilder UseDefaultStandardPaths(bool enabled = true)
        {
            includeDefaultStandardPaths = enabled;
            return this;
        }

        public FileLookupBuilder AllowCurrentPath(bool enabled = true)
        {
            allowCurrentPath = enabled;
            return this;
        }

        public FileLookup Build()
        {
            var standard = new List<string>(standardPaths);

            if (includeDefaultStandardPaths)
            {
                standard.AddRange(GetBasePaths());
            }

            return new(
                options,
                startupPaths.ToArray(),
                standard.ToArray(),
                additionalPaths.ToArray(),
                allowCurrentPath);
        }

        private static void AddWithLib(ICollection<string> target, string path)
        {
            Add(target, path);
            Add(target, Path.Combine(path, LibDirectory));
        }

        private static void Add(ICollection<string> target, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Lookup paths cannot be empty.", nameof(path));
            }

            target.Add(path);
        }
    }
}
