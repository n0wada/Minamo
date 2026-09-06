using Minamo.Hosting;
using Minamo.Library.Time;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Minamo.Library.IO;

[MinamoModule("io")]
[MinamoForeignType(typeof(MinamoDriveTypeInfo))]
public static class IoModule
{
    [MinamoCommand(Type = "File")]
    internal static MinamoObject? ReadText(MinamoCommandContext host, string path, object? encoding = null)
    {
        var enc = GetEncoding(host.ExecutionContext, encoding);
        return host.ExecutionContext.HasErrors
            ? Nil
            : host.ExecutionContext.Handle(() => new MinamoString(File.ReadAllText(path, enc)));
    }

    [MinamoCommand(Type = "File")]
    internal static MinamoObject? ReadLines(MinamoCommandContext host, string path, object? encoding = null)
    {
        var enc = GetEncoding(host.ExecutionContext, encoding);
        return host.ExecutionContext.HasErrors
            ? Nil
            : host.ExecutionContext.Handle(() =>
                new MinamoArray(File.ReadAllLines(path, enc).Select(line => new MinamoString(line)).ToArray()));
    }

    [MinamoCommand(Type = "File")]
    internal static void WriteAllText(MinamoCommandContext host, string path, string data, object? encoding = null)
    {
        var enc = GetEncoding(host.ExecutionContext, encoding);
        if (!host.ExecutionContext.HasErrors)
        {
            host.ExecutionContext.Handle(() => File.WriteAllText(path, data, enc));
        }
    }

    [MinamoCommand(Type = "File")]
    internal static void WriteAllLines(MinamoCommandContext host, string path, MinamoObject value, object? encoding = null)
    {
        var enc = GetEncoding(host.ExecutionContext, encoding);
        var sequence = MinamoIterator.ToEnumerable(host.ExecutionContext, value).ToArray();

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

    [MinamoCommand(Type = "File")]
    internal static MinamoObject? ReadAllBytes(MinamoCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
            host.ExecutionContext.Type<MinamoByteArrayTypeInfo>().Create(File.ReadAllBytes(path)));

    [MinamoCommand(Type = "File")]
    internal static void WriteAllBytes(MinamoCommandContext host, string path, MinamoObject value) =>
        host.ExecutionContext.Handle(() => File.WriteAllBytes(path, ((MinamoByteArray)value).GetBytes()));

    [MinamoCommand(Type = "File")]
    internal static bool? Exists(MinamoCommandContext host, string path) =>
        host.ExecutionContext.Handle(() => File.Exists(path));

    [MinamoCommand(Type = "File")]
    internal static void Create(MinamoCommandContext host, string path) =>
        host.ExecutionContext.Handle(() => File.Create(path).Dispose());

    [MinamoCommand(Type = "File")]
    internal static void Delete(MinamoCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        });

    [MinamoCommand(Type = "File")]
    internal static MinamoObject? GetAttributes(MinamoCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
        {
            var attributes = File.GetAttributes(path);
            return MinamoTuple.Create(
                new("readOnly", (MinamoBool)attributes.HasFlag(FileAttributes.ReadOnly)),
                new("hidden", (MinamoBool)attributes.HasFlag(FileAttributes.Hidden)),
                new("system", (MinamoBool)attributes.HasFlag(FileAttributes.System)),
                new("directory", (MinamoBool)attributes.HasFlag(FileAttributes.Directory)),
                new("archive", (MinamoBool)attributes.HasFlag(FileAttributes.Archive)),
                new("device", (MinamoBool)attributes.HasFlag(FileAttributes.Device)),
                new("normal", (MinamoBool)attributes.HasFlag(FileAttributes.Normal)),
                new("temporary", (MinamoBool)attributes.HasFlag(FileAttributes.Temporary)),
                new("sparseFile", (MinamoBool)attributes.HasFlag(FileAttributes.SparseFile)),
                new("reparsePoint", (MinamoBool)attributes.HasFlag(FileAttributes.ReparsePoint)),
                new("compressed", (MinamoBool)attributes.HasFlag(FileAttributes.Compressed)),
                new("offline", (MinamoBool)attributes.HasFlag(FileAttributes.Offline)),
                new("notContentIndexed", (MinamoBool)attributes.HasFlag(FileAttributes.NotContentIndexed)),
                new("encrypted", (MinamoBool)attributes.HasFlag(FileAttributes.Encrypted)),
                new("integrityStream", (MinamoBool)attributes.HasFlag(FileAttributes.IntegrityStream)),
                new("noScrubData", (MinamoBool)attributes.HasFlag(FileAttributes.NoScrubData)));
        });

    [MinamoCommand(Type = "File")]
    internal static void SetAttributes(MinamoCommandContext host, string path, MinamoObject attributes) =>
        host.ExecutionContext.Handle(() =>
        {
            FileAttributes result = default;

            foreach (var item in MinamoIterator.ToEnumerable(host.ExecutionContext, attributes))
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

    [MinamoCommand(Type = "File")]
    internal static void Copy(MinamoCommandContext host, string source, string destination, bool overwrite = false) =>
        host.ExecutionContext.Handle(() => File.Copy(source, destination, overwrite));

    [MinamoCommand(Type = "File")]
    internal static void Move(MinamoCommandContext host, string source, string destination, bool overwrite = false) =>
        host.ExecutionContext.Handle(() => File.Move(source, destination, overwrite));

    [MinamoCommand(Type = "File")]
    internal static MinamoObject? GetCreationTime(MinamoCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
            new MinamoDateTime(host.ExecutionContext.Type<MinamoDateTimeTypeInfo>(), File.GetCreationTimeUtc(path).Ticks));

    [MinamoCommand(Type = "File")]
    internal static void SetCreationTime(MinamoCommandContext host, string path, MinamoObject value) =>
        host.ExecutionContext.Handle(() => File.SetCreationTimeUtc(path, GetDateTimeUtc((MinamoDateTime)value)));

    [MinamoCommand(Type = "File")]
    internal static MinamoObject? GetLastAccessTime(MinamoCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
            new MinamoDateTime(host.ExecutionContext.Type<MinamoDateTimeTypeInfo>(), File.GetLastAccessTimeUtc(path).Ticks));

    [MinamoCommand(Type = "File")]
    internal static void SetLastAccessTime(MinamoCommandContext host, string path, MinamoObject value) =>
        host.ExecutionContext.Handle(() => File.SetLastAccessTimeUtc(path, GetDateTimeUtc((MinamoDateTime)value)));

    [MinamoCommand(Type = "File")]
    internal static MinamoObject? GetLastWriteTime(MinamoCommandContext host, string path) =>
        host.ExecutionContext.Handle(() =>
            new MinamoDateTime(host.ExecutionContext.Type<MinamoDateTimeTypeInfo>(), File.GetLastWriteTimeUtc(path).Ticks));

    [MinamoCommand(Type = "File")]
    internal static void SetLastWriteTime(MinamoCommandContext host, string path, MinamoObject value) =>
        host.ExecutionContext.Handle(() => File.SetLastWriteTimeUtc(path, GetDateTimeUtc((MinamoDateTime)value)));

    [MinamoCommand(Type = "Path")]
    internal static string GetFullPath(string path) => Path.GetFullPath(path);

    [MinamoCommand(Type = "Path")]
    internal static string? GetDirectory(string path) => Path.GetDirectoryName(path);

    [MinamoCommand(Type = "Path")]
    internal static string GetExtension(string path) => Path.GetExtension(path);

    [MinamoCommand(Type = "Path")]
    internal static string GetFileName(string path) => Path.GetFileName(path);

    [MinamoCommand(Type = "Path")]
    internal static string? GetPathRoot(string path) => Path.GetPathRoot(path);

    [MinamoCommand(Type = "Path")]
    internal static string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);

    [MinamoCommand(Type = "Path")]
    internal static string? Combine(MinamoCommandContext host, string path, string other) =>
        host.ExecutionContext.Handle(() => Path.Combine(path, other));

    private static object ExistsPath(MinamoCommandContext host, string path)
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

    [MinamoCommand("Exists", Type = "Path")]
    internal static object PathExists(MinamoCommandContext host, string path) => ExistsPath(host, path);

    [MinamoCommand(Type = "Path")]
    internal static MinamoObject EnumerateFiles(MinamoCommandContext host, string path, object? mask = null)
    {
        var pattern = OptionalString(mask);
        return MinamoIterator.Create(EnumeratePaths(
            host.ExecutionContext,
            () => pattern is null
                ? Directory.EnumerateFiles(path)
                : Directory.EnumerateFiles(path, pattern)));
    }

    [MinamoCommand(Type = "Path")]
    internal static MinamoObject EnumerateDirectories(MinamoCommandContext host, string path, object? mask = null)
    {
        var pattern = OptionalString(mask);
        return MinamoIterator.Create(EnumeratePaths(
            host.ExecutionContext,
            () => pattern is null
                ? Directory.EnumerateDirectories(path)
                : Directory.EnumerateDirectories(path, pattern)));
    }

    [MinamoCommand("Exists", Type = "Directory")]
    internal static bool? DirectoryExists(MinamoCommandContext host, string path) =>
        host.ExecutionContext.Handle(() => Directory.Exists(path));

    [MinamoCommand("Create", Type = "Directory")]
    internal static void CreateDirectory(MinamoCommandContext host, string path) =>
        host.ExecutionContext.Handle(() => Directory.CreateDirectory(path));

    [MinamoCommand("Delete", Type = "Directory")]
    internal static void DeleteDirectory(MinamoCommandContext host, string path, bool recursive = false) =>
        host.ExecutionContext.Handle(() => Directory.Delete(path, recursive));

    [MinamoCommand("Move", Type = "Directory")]
    internal static void MoveDirectory(MinamoCommandContext host, string path, string other) =>
        host.ExecutionContext.Handle(() => Directory.Move(path, other));

    [MinamoCommand("Copy", Type = "Directory")]
    internal static void CopyDirectory(MinamoCommandContext host, string path, string other) =>
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

    [MinamoCommand(Type = "Drive")]
    internal static MinamoObject[] GetDrives(MinamoCommandContext host) =>
        host.ExecutionContext.Handle(() =>
            DriveInfo.GetDrives().Select(drive =>
                (MinamoObject)new MinamoDrive(host.ExecutionContext.Type<MinamoDriveTypeInfo>(), drive)).ToArray())
        ?? Array.Empty<MinamoObject>();

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

    private static IEnumerable<MinamoObject> EnumeratePaths(
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

                yield return new MinamoString(current!);
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

    private static DateTime GetDateTimeUtc(MinamoDateTime value) =>
        value is MinamoLocalDateTime local
            ? local.ToDateTimeOffset().ToUniversalTime().DateTime
            : value.ToDateTime();

    private static MinamoObject Nil => MinamoNil.Instance;
}

internal sealed class MinamoDrive : MinamoForeignObject
{
    internal MinamoDrive(MinamoDriveTypeInfo typeInfo, DriveInfo value) : base(typeInfo) =>
        Value = value;

    internal DriveInfo Value { get; }

    public override MinamoObject Clone() => this;

    public override bool Equals(MinamoObject? other) => other is MinamoDrive drive && drive.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();

    public override object ToObject() => Value;

    public override string ToString() => Value.ToString();
}

internal sealed class MinamoDriveTypeInfo : MinamoForeignTypeInfo
{
    public override string ReflectedTypeName => "Drive";

    protected override MinamoFunction? InitializeInstanceMember(MinamoObject self, string name, ExecutionContext context) =>
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

    private static MinamoFunction Property(string name, Func<DriveInfo, object?> getter) =>
        new MinamoExternalFunction(name, isPropertyGetter: true, (context, self, _) =>
        {
            try
            {
                return TypeConverter.ConvertFrom(getter(((MinamoDrive)self!).Value));
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
