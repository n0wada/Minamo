using Spellkit.Runtime.Types;
using System.Collections;
using System.Reflection;
using System.Text;

namespace Spellkit;

internal static class CommandLine
{
    private abstract class OptionValue
    {
        public abstract SpellkitObject ToSpellkitObject();
    }

    private sealed class ScalarValue(string? value) : OptionValue
    {
        public string? Value { get; } = value;

        public override SpellkitObject ToSpellkitObject() => Value is null ? SpellkitBool.True : new SpellkitString(Value);
    }

    private sealed class ArrayValue(IEnumerable<string?> values) : OptionValue
    {
        public List<string?> Values { get; } = new(values);

        public override SpellkitObject ToSpellkitObject() =>
            new SpellkitTuple(Values.Select(value =>
                (SpellkitObject)(value is null ? SpellkitBool.True : new SpellkitString(value))).ToArray());
    }

    public static CommandLineOptions Read(string[] args)
    {
        var values = Parse(args);
        var options = Bind(values);

        if (values.Count > 0)
        {
            var userArguments = new List<SpellkitObject>();

            foreach (var (name, value) in values)
            {
                if (!name.StartsWith('-'))
                {
                    throw new CommandLineException($"Unknown switch -{name}.");
                }

                userArguments.Add(new SpellkitLabel(name.TrimStart('-'), value.ToSpellkitObject()));
            }

            options.UserArguments = new SpellkitTuple(userArguments.ToArray());
        }

        return options;
    }

    public static string GenerateHelp<T>(string prefix = "-") => GenerateHelp(typeof(T), prefix);

    public static string GenerateHelp(Type type, string prefix = "-")
    {
        const int helpLength = 60;
        var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                                      BindingFlags.Static | BindingFlags.Instance)
            .Select(member => (member, binding: member.GetCustomAttribute<BindingAttribute>()))
            .Where(item => item.binding?.Help is not null)
            .ToArray();
        var names = members.Select(item => item.binding!.Names.Length == 0
                ? "<default>"
                : string.Join(", ", item.binding.Names.Select(name => prefix + name)))
            .ToArray();
        var padding = names.Length == 0 ? 0 : names.Max(name => name.Length);
        var output = new StringBuilder();

        for (var i = 0; i < members.Length; i++)
        {
            output.Append(names[i].PadRight(padding));
            output.Append("    ");
            AppendWrapped(output, members[i].binding!.Help!, padding + 4, helpLength);
        }

        return output.ToString();
    }

    private static CommandLineOptions Bind(Dictionary<string, OptionValue> values)
    {
        var options = new CommandLineOptions();

        foreach (var property in typeof(CommandLineOptions).GetProperties())
        {
            var binding = property.GetCustomAttribute<BindingAttribute>();
            if (binding is null)
            {
                continue;
            }

            var keys = binding.Names.Length == 0 ? new[] { "$default" } : binding.Names;
            var selected = new List<string?>();
            string? selectedKey = null;

            foreach (var key in keys)
            {
                if (!values.Remove(key, out var value))
                {
                    continue;
                }

                selectedKey = key;
                if (value is ArrayValue array)
                {
                    selected.AddRange(array.Values);
                }
                else
                {
                    selected.Add(((ScalarValue)value).Value);
                }
            }

            if (selectedKey is null)
            {
                continue;
            }

            try
            {
                property.SetValue(options, ConvertValue(selectedKey, selected, property.PropertyType));
            }
            catch (CommandLineException)
            {
                throw;
            }
            catch
            {
                throw InvalidValue(selectedKey);
            }
        }

        return options;
    }

    private static object ConvertValue(string key, List<string?> values, Type type)
    {
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var array = Array.CreateInstance(elementType, values.Count);

            for (var i = 0; i < values.Count; i++)
            {
                array.SetValue(ConvertScalar(key, values[i], elementType), i);
            }

            return array;
        }

        if (values.Count > 1)
        {
            throw new CommandLineException($"Command line switch -{key} doesn't support multiple values.");
        }

        return ConvertScalar(key, values[0], type);
    }

    private static object ConvertScalar(string key, string? value, Type type)
    {
        if (type == typeof(string) && value is not null)
        {
            return value;
        }

        if (type == typeof(int) && int.TryParse(value, out var integer))
        {
            return integer;
        }

        if (type == typeof(bool))
        {
            return value is null || string.Equals(bool.TrueString, value, StringComparison.OrdinalIgnoreCase);
        }

        if (type.IsEnum && Enum.TryParse(type, value, true, out var enumeration))
        {
            return enumeration!;
        }

        throw InvalidValue(key);
    }

    private static Dictionary<string, OptionValue> Parse(string[] args)
    {
        var values = new Dictionary<string, OptionValue>(StringComparer.OrdinalIgnoreCase);
        string? pending = null;

        void Add(string key, string? value)
        {
            value = value?.Trim('"');

            if (!values.TryGetValue(key, out var existing))
            {
                values.Add(key, new ScalarValue(value));
            }
            else if (existing is ArrayValue array)
            {
                array.Values.Add(value);
            }
            else
            {
                values[key] = new ArrayValue(new[] { ((ScalarValue)existing).Value, value });
            }
        }

        foreach (var argument in args)
        {
            var value = argument.Trim();
            if (value.Length == 0)
            {
                throw new CommandLineException("Empty command line argument.");
            }

            if (value[0] == '-')
            {
                if (pending is not null)
                {
                    Add(pending, null);
                }

                pending = value[1..];
            }
            else if (pending is not null)
            {
                Add(pending, value);
                pending = null;
            }
            else
            {
                Add("$default", value);
            }
        }

        if (pending is not null)
        {
            Add(pending, null);
        }

        return values;
    }

    private static void AppendWrapped(StringBuilder output, string text, int padding, int width)
    {
        while (text.Length > width)
        {
            var split = text.LastIndexOf(' ', width);
            if (split <= 0)
            {
                break;
            }

            output.AppendLine(text[..split]);
            output.Append(' ', padding);
            text = text[(split + 1)..];
        }

        output.AppendLine(text);
    }

    private static CommandLineException InvalidValue(string key) =>
        new($"Invalid value for command line switch -{key}.");
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, Inherited = false)]
internal sealed class BindingAttribute(params string[] names) : Attribute
{
    public string[] Names { get; } = names;

    public string? Help { get; set; }

    public string? Category { get; set; }
}

internal sealed class CommandLineException(string message) : Exception(message);

internal sealed class CommandLineOptions
{
    private const string Compiler = "Compiler settings";
    private const string Linker = "Linker settings";
    private const string General = "General settings";

    [Binding(Help = "A .kit file to execute. Multiple files or directories are supported.", Category = Compiler)]
    public string[]? FileNames { get; set; }

    [Binding("out", Help = "Output directory for generated bytecode files.", Category = Compiler)]
    public string? OutputDirectory { get; set; }

    [Binding("il", Help = "Print bytecode or write it to .il files when -out is specified.", Category = Compiler)]
    public bool GenerateBytecode { get; set; }

    [Binding("debug", Help = "Compile in debug mode.", Category = Compiler)]
    public bool Debug { get; set; }

    [Binding("nopt", Help = "Disable compiler optimizations.", Category = Compiler)]
    public bool NoOptimizations { get; set; }

    [Binding("nowarn", Help = "Suppress compiler warnings.", Category = Compiler)]
    public bool NoWarnings { get; set; }

    [Binding("ignore", Help = "Ignore a compiler or linker warning. May be repeated.", Category = General)]
    public int[]? IgnoreWarnings { get; set; }

    [Binding("nowarnlinker", Help = "Suppress linker warnings.", Category = Linker)]
    public bool NoWarningsLinker { get; set; }

    [Binding("nolang", Help = "Do not import the lang module.", Category = Compiler)]
    public bool NoLang { get; set; }

    [Binding("linklog", Help = "Write linker tracing to a file.", Category = Linker)]
    public string? LinkerLog { get; set; }

    [Binding("path", Help = "Add a module lookup path. May be repeated.", Category = Linker)]
    public string[]? Paths { get; set; }

    [Binding("nologo", Help = "Hide the startup header.", Category = General)]
    public bool NoLogo { get; set; }

    [Binding("time", Help = "Measure execution time.", Category = General)]
    public bool MeasureTime { get; set; }

    [Binding("i", Help = "Enter the REPL after executing files.", Category = General)]
    public bool StayInteractive { get; set; }

    [Binding("do", "-do", Help = "Run a named interactive select after executing files.", Category = General)]
    public string? SelectName { get; set; }

    [Binding("v", "-version", Help = "Display version information and exit.", Category = General)]
    public bool ShowVersion { get; set; }

    [Binding("h", "-help", Help = "Display command-line help and exit.", Category = General)]
    public bool ShowHelp { get; set; }

    public SpellkitTuple? UserArguments { get; set; }

    public IEnumerable<string> GetFileNames()
    {
        foreach (var item in FileNames ?? Array.Empty<string>())
        {
            if (Directory.Exists(item))
            {
                foreach (var file in Directory.GetFiles(item, "*.kit").OrderBy(file => file))
                {
                    yield return file;
                }
            }
            else
            {
                yield return item;
            }
        }
    }

    public override string ToString()
    {
        var entries = typeof(CommandLineOptions).GetProperties()
            .Select(property => (property, binding: property.GetCustomAttribute<BindingAttribute>()))
            .Where(item => item.binding is not null)
            .Select(item => (binding: item.binding!, value: item.property.GetValue(this)))
            .Where(item => item.value is not null && item.value is not false)
            .Select(item =>
            {
                var name = item.binding.Names.FirstOrDefault() ?? "<file>";
                var value = item.value is string || item.value is not IEnumerable sequence
                    ? item.value!.ToString()!
                    : string.Join(';', sequence.Cast<object>());
                return (name: "-" + name, value: item.value is bool ? "" : value);
            })
            .ToArray();

        if (entries.Length == 0)
        {
            return "<none>";
        }

        var padding = entries.Max(entry => entry.name.Length) + 1;
        return string.Join(Environment.NewLine,
            entries.Select(entry => entry.name.PadRight(padding) + entry.value));
    }
}
