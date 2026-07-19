using Spellkit.Hosting;
using Spellkit.Library.Binary;
using Spellkit.Library.Time;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Spellkit.Library.IO;

[SpellkitModule("io")]
[SpellkitForeignType(typeof(SpkDriveTypeInfo))]
public static class IoModule
{
    [SpellkitCommand(Type = "File")]
    internal static SpkObject? ReadText(SpellkitCommandContext host, string path, object? encoding = null)
    {
        var enc = GetEncoding(host.ExecutionContext, encoding);
        return host.ExecutionContext.HasErrors
            ? Nil
            : host.ExecutionContext.Handle(() => new SpkString(File.ReadAllText(path, enc)));
    }

    [SpellkitCommand(Type = "File")]
    internal static SpkObject? ReadLines(SpellkitCommandContext host, string path, object? encoding = null)
    {
        var enc = GetEncoding(host.ExecutionContext, encoding);
        return host.ExecutionContext.HasErrors
            ? Nil
            : host.ExecutionContext.Handle(() =>
                new SpkArray(File.ReadAllLines(path, enc).Select(line => new SpkString(line)).ToArray()));
    }

    [SpellkitCommand(Type = "File")]
    internal static void WriteAllText(SpellkitCommandContext host, string path, string data, object? encoding = null)
    {
        var enc = GetEncoding(host.ExecutionContext, encoding);
        if (!host.ExecutionContext.HasErrors)
        {
            host.ExecutionContext.Handle(() => File.WriteAllText(path, data, enc));
        }
    }

    [SpellkitCommand(Type = "File")]
    internal static void WriteAllLines(SpellkitCommandContext host, string path, SpkObject value, object? encoding = null)
    {
        var enc = GetEncoding(host.ExecutionContext, encoding);
        var sequence = SpkIterator.ToEnumerable(host.ExecutionContext, value).ToArray();

        if (host.ExecutionContext.HasErrors)
        {
            return;
        }

        var strings = sequence.Select(item => item.ToString(host.ExecutionContext).Value).ToArray();
        if (!host.ExecutionContext.HasErrors)
        {
            host.ExecutionContext.Handle(() => File.WriteAllLines(path, strings, enc));
        }
    }

    [SpellkitCommand(Type = "File")]
    internal static SpkObject? ReadAllBytes(SpellkitCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
            host.ExecutionContext.Type<SpkByteArrayTypeInfo>().Create(File.ReadAllBytes(path)));

    [SpellkitCommand(Type = "File")]
    internal static void WriteAllBytes(SpellkitCommandContext host, string path, SpkObject value) =>
        host.ExecutionContext.Handle(() => File.WriteAllBytes(path, ((SpkByteArray)value).GetBytes()));

    [SpellkitCommand(Type = "File")]
    internal static bool? Exists(SpellkitCommandContext host, string path) =>
        host.ExecutionContext.Handle(() => File.Exists(path));

    [SpellkitCommand(Type = "File")]
    internal static void Create(SpellkitCommandContext host, string path) =>
        host.ExecutionContext.Handle(() => File.Create(path).Dispose());

    [SpellkitCommand(Type = "File")]
    internal static void Delete(SpellkitCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        });

    [SpellkitCommand(Type = "File")]
    internal static SpkObject? GetAttributes(SpellkitCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
        {
            var attributes = File.GetAttributes(path);
            return SpkTuple.Create(
                new("readOnly", (SpkBool)attributes.HasFlag(FileAttributes.ReadOnly)),
                new("hidden", (SpkBool)attributes.HasFlag(FileAttributes.Hidden)),
                new("system", (SpkBool)attributes.HasFlag(FileAttributes.System)),
                new("directory", (SpkBool)attributes.HasFlag(FileAttributes.Directory)),
                new("archive", (SpkBool)attributes.HasFlag(FileAttributes.Archive)),
                new("device", (SpkBool)attributes.HasFlag(FileAttributes.Device)),
                new("normal", (SpkBool)attributes.HasFlag(FileAttributes.Normal)),
                new("temporary", (SpkBool)attributes.HasFlag(FileAttributes.Temporary)),
                new("sparseFile", (SpkBool)attributes.HasFlag(FileAttributes.SparseFile)),
                new("reparsePoint", (SpkBool)attributes.HasFlag(FileAttributes.ReparsePoint)),
                new("compressed", (SpkBool)attributes.HasFlag(FileAttributes.Compressed)),
                new("offline", (SpkBool)attributes.HasFlag(FileAttributes.Offline)),
                new("notContentIndexed", (SpkBool)attributes.HasFlag(FileAttributes.NotContentIndexed)),
                new("encrypted", (SpkBool)attributes.HasFlag(FileAttributes.Encrypted)),
                new("integrityStream", (SpkBool)attributes.HasFlag(FileAttributes.IntegrityStream)),
                new("noScrubData", (SpkBool)attributes.HasFlag(FileAttributes.NoScrubData)));
        });

    [SpellkitCommand(Type = "File")]
    internal static void SetAttributes(SpellkitCommandContext host, string path, SpkObject attributes) =>
        host.ExecutionContext.Handle(() =>
        {
            FileAttributes result = default;

            foreach (var item in SpkIterator.ToEnumerable(host.ExecutionContext, attributes))
            {
                var name = item.ToString(host.ExecutionContext).Value;
                if (!Enum.TryParse<FileAttributes>(name, out var parsed))
                {
                    host.ExecutionContext.InvalidValue(name);
                    return;
                }

                result |= parsed;
            }

            if (result != default)
            {
                File.SetAttributes(path, result);
            }
        });

    [SpellkitCommand(Type = "File")]
    internal static void Copy(SpellkitCommandContext host, string source, string destination, bool overwrite = false) =>
        host.ExecutionContext.Handle(() => File.Copy(source, destination, overwrite));

    [SpellkitCommand(Type = "File")]
    internal static void Move(SpellkitCommandContext host, string source, string destination, bool overwrite = false) =>
        host.ExecutionContext.Handle(() => File.Move(source, destination, overwrite));

    [SpellkitCommand(Type = "File")]
    internal static SpkObject? GetCreationTime(SpellkitCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
            new SpkDateTime(host.ExecutionContext.Type<SpkDateTimeTypeInfo>(), File.GetCreationTimeUtc(path).Ticks));

    [SpellkitCommand(Type = "File")]
    internal static void SetCreationTime(SpellkitCommandContext host, string path, SpkObject value) =>
        host.ExecutionContext.Handle(() => File.SetCreationTimeUtc(path, GetDateTimeUtc((SpkDateTime)value)));

    [SpellkitCommand(Type = "File")]
    internal static SpkObject? GetLastAccessTime(SpellkitCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
            new SpkDateTime(host.ExecutionContext.Type<SpkDateTimeTypeInfo>(), File.GetLastAccessTimeUtc(path).Ticks));

    [SpellkitCommand(Type = "File")]
    internal static void SetLastAccessTime(SpellkitCommandContext host, string path, SpkObject value) =>
        host.ExecutionContext.Handle(() => File.SetLastAccessTimeUtc(path, GetDateTimeUtc((SpkDateTime)value)));

    [SpellkitCommand(Type = "File")]
    internal static SpkObject? GetLastWriteTime(SpellkitCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
            new SpkDateTime(host.ExecutionContext.Type<SpkDateTimeTypeInfo>(), File.GetLastWriteTimeUtc(path).Ticks));

    [SpellkitCommand(Type = "File")]
    internal static void SetLastWriteTime(SpellkitCommandContext host, string path, SpkObject value) =>
        host.ExecutionContext.Handle(() => File.SetLastWriteTimeUtc(path, GetDateTimeUtc((SpkDateTime)value)));

    [SpellkitCommand(Type = "Path")]
    internal static string GetFullPath(string path) => Path.GetFullPath(path);

    [SpellkitCommand(Type = "Path")]
    internal static string? GetDirectory(string path) => Path.GetDirectoryName(path);

    [SpellkitCommand(Type = "Path")]
    internal static string GetExtension(string path) => Path.GetExtension(path);

    [SpellkitCommand(Type = "Path")]
    internal static string GetFileName(string path) => Path.GetFileName(path);

    [SpellkitCommand(Type = "Path")]
    internal static string? GetPathRoot(string path) => Path.GetPathRoot(path);

    [SpellkitCommand(Type = "Path")]
    internal static string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);

    [SpellkitCommand(Type = "Path")]
    internal static string? Combine(SpellkitCommandContext host, string path, string other) =>
        host.ExecutionContext.Handle(() => Path.Combine(path, other));

    private static object ExistsPath(SpellkitCommandContext host, string path)
    {
        try
        {
            return Directory.Exists(path) || File.Exists(path);
        }
        catch (ArgumentException)
        {
            return host.ExecutionContext.InvalidValue(path);
        }
    }

    [SpellkitCommand("Exists", Type = "Path")]
    internal static object PathExists(SpellkitCommandContext host, string path) => ExistsPath(host, path);

    [SpellkitCommand(Type = "Path")]
    internal static SpkObject EnumerateFiles(SpellkitCommandContext host, string path, object? mask = null)
    {
        var pattern = OptionalString(mask);
        return SpkIterator.Create(EnumeratePaths(
            host.ExecutionContext,
            () => pattern is null
                ? Directory.EnumerateFiles(path)
                : Directory.EnumerateFiles(path, pattern)));
    }

    [SpellkitCommand(Type = "Path")]
    internal static SpkObject EnumerateDirectories(SpellkitCommandContext host, string path, object? mask = null)
    {
        var pattern = OptionalString(mask);
        return SpkIterator.Create(EnumeratePaths(
            host.ExecutionContext,
            () => pattern is null
                ? Directory.EnumerateDirectories(path)
                : Directory.EnumerateDirectories(path, pattern)));
    }

    [SpellkitCommand("Exists", Type = "Directory")]
    internal static bool? DirectoryExists(SpellkitCommandContext host, string path) =>
        host.ExecutionContext.Handle(() => Directory.Exists(path));

    [SpellkitCommand("Create", Type = "Directory")]
    internal static void CreateDirectory(SpellkitCommandContext host, string path) =>
        host.ExecutionContext.Handle(() => Directory.CreateDirectory(path));

    [SpellkitCommand("Delete", Type = "Directory")]
    internal static void DeleteDirectory(SpellkitCommandContext host, string path, bool recursive = false) =>
        host.ExecutionContext.Handle(() => Directory.Delete(path, recursive));

    [SpellkitCommand("Move", Type = "Directory")]
    internal static void MoveDirectory(SpellkitCommandContext host, string path, string other) =>
        host.ExecutionContext.Handle(() => Directory.Move(path, other));

    [SpellkitCommand("Copy", Type = "Directory")]
    internal static void CopyDirectory(SpellkitCommandContext host, string path, string other) =>
        host.ExecutionContext.Handle(() =>
        {
            var source = Path.GetFullPath(path);
            var destination = Path.GetFullPath(other);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var sourcePrefix = Path.TrimEndingDirectorySeparator(source)
                + Path.DirectorySeparatorChar;

            if (destination.Equals(source, comparison)
                || destination.StartsWith(sourcePrefix, comparison))
            {
                throw new IOException("A directory cannot be copied into itself.");
            }

            var enumeration = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            Directory.CreateDirectory(destination);

            foreach (var directory in Directory.GetDirectories(source, "*", enumeration))
            {
                Directory.CreateDirectory(Path.Combine(
                    destination,
                    Path.GetRelativePath(source, directory)));
            }

            foreach (var file in Directory.GetFiles(source, "*", enumeration))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        });

    [SpellkitCommand(Type = "Drive")]
    internal static SpkObject[] GetDrives(SpellkitCommandContext host) =>
        host.ExecutionContext.Handle(() =>
            DriveInfo.GetDrives().Select(drive =>
                (SpkObject)new SpkDrive(host.ExecutionContext.Type<SpkDriveTypeInfo>(), drive)).ToArray())
        ?? Array.Empty<SpkObject>();

    private static Encoding GetEncoding(ExecutionContext context, object? encoding)
    {
        var codePage = Encoding.UTF8.CodePage;

        if (encoding is not null)
        {
            codePage = Convert.ToInt32(encoding);
        }

        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception)
        {
            if (encoding is not null)
            {
                context.InvalidValue(encoding);
            }

            return Encoding.UTF8;
        }
    }

    private static string? OptionalString(object? value) => value as string;

    private static IEnumerable<SpkObject> EnumeratePaths(
        ExecutionContext context,
        Func<IEnumerable<string>> source)
    {
        IEnumerator<string>? enumerator = null;
        Exception? failure = null;

        try
        {
            enumerator = source().GetEnumerator();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (failure is not null)
        {
            SetEnumerationError(context, failure);
            yield break;
        }

        using (var iterator = enumerator!)
        {
            while (true)
            {
                string? current = null;
                var moved = false;
                failure = null;

                try
                {
                    moved = iterator.MoveNext();
                    if (moved)
                    {
                        current = iterator.Current;
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                if (failure is not null)
                {
                    SetEnumerationError(context, failure);
                    yield break;
                }

                if (!moved)
                {
                    yield break;
                }

                yield return new SpkString(current!);
            }
        }
    }

    private static void SetEnumerationError(ExecutionContext context, Exception exception)
    {
        if (exception is ArgumentException)
        {
            context.InvalidValue();
        }
        else
        {
            context.IOFailed(exception.Message);
        }
    }

    private static DateTime GetDateTimeUtc(SpkDateTime value) =>
        value is SpkLocalDateTime local
            ? local.ToDateTimeOffset().ToUniversalTime().DateTime
            : value.ToDateTime();

    private static SpkObject Nil => SpkNil.Instance;
}

internal sealed class SpkDrive : SpkForeignObject
{
    internal SpkDrive(SpkDriveTypeInfo typeInfo, DriveInfo value) : base(typeInfo) =>
        Value = value;

    internal DriveInfo Value { get; }

    public override SpkObject Clone() => this;

    public override bool Equals(SpkObject? other) => other is SpkDrive drive && drive.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();

    public override object ToObject() => Value;

    public override string ToString() => Value.ToString();
}

internal sealed class SpkDriveTypeInfo : SpkForeignTypeInfo
{
    public override string ReflectedTypeName => "Drive";

    protected override SpkFunction? InitializeInstanceMember(SpkObject self, string name, ExecutionContext context) =>
        name switch
        {
            "Name" => Property(name, drive => drive.Name),
            "TotalSize" => Property(name, drive => drive.TotalSize),
            "TotalFreeSpace" => Property(name, drive => drive.TotalFreeSpace),
            "AvailableFreeSpace" => Property(name, drive => drive.AvailableFreeSpace),
            "Format" => Property(name, drive => drive.DriveFormat),
            "Root" => Property(name, drive => drive.RootDirectory.FullName),
            "Type" => Property(name, drive => drive.DriveType.ToString()),
            "IsReady" => Property(name, drive => drive.IsReady),
            "Label" => Property(name, drive => drive.VolumeLabel),
            _ => base.InitializeInstanceMember(self, name, context)
        };

    private static SpkFunction Property(string name, Func<DriveInfo, object?> getter) =>
        new SpkExternalFunction(name, isPropertyGetter: true, (context, self, _) =>
        {
            try
            {
                return TypeConverter.ConvertFrom(getter(((SpkDrive)self!).Value));
            }
            catch (Exception ex)
            {
                return context.IOFailed(ex.Message);
            }
        });
}

internal static class IoHandlerExtensions
{
    public static T? Handle<T>(this ExecutionContext context, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            context.IOFailed(ex.Message);
            return default;
        }
    }

    public static void Handle(this ExecutionContext context, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            context.IOFailed(ex.Message);
        }
    }
}
