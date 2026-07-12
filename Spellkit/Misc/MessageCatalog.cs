using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace Spellkit.Diagnostics;

internal enum MessageGroup
{
    Parser,
    Compiler,
    Linker,
    Runtime
}

internal static class MessageCatalog
{
    private const string ResourceName = "Spellkit.Resources.messages.xml";

    private static readonly Lazy<IReadOnlyDictionary<(MessageGroup Group, string Id), string>> Messages =
        new(LoadMessages);

    public static string Get(MessageGroup group, string id) =>
        Find(group, id) ?? id;

    public static string? Find(MessageGroup group, string id) =>
        Messages.Value.TryGetValue((group, id), out var message) ? message : null;

    public static string Format(MessageGroup group, string id, params object[] args) =>
        string.Format(Get(group, id), args);

    private static IReadOnlyDictionary<(MessageGroup Group, string Id), string> LoadMessages()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded message catalog '{ResourceName}' was not found.");
        var document = XDocument.Load(stream, LoadOptions.None);
        var root = document.Root
            ?? throw new InvalidDataException("The message catalog has no root element.");

        if (root.Name != "message-catalog" || (string?)root.Attribute("version") != "1")
        {
            throw new InvalidDataException("The message catalog format is not supported.");
        }

        var messages = new Dictionary<(MessageGroup Group, string Id), string>();
        var groups = new HashSet<MessageGroup>();
        foreach (var groupElement in root.Elements("group"))
        {
            var groupName = (string?)groupElement.Attribute("id");
            if (!Enum.TryParse<MessageGroup>(groupName, ignoreCase: true, out var group)
                || !groups.Add(group))
            {
                throw new InvalidDataException($"Invalid or duplicate message group '{groupName}'.");
            }

            foreach (var messageElement in groupElement.Elements("message"))
            {
                var id = (string?)messageElement.Attribute("id");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrEmpty(messageElement.Value))
                {
                    throw new InvalidDataException($"Message group '{groupName}' contains an invalid entry.");
                }

                if (!messages.TryAdd((group, id), messageElement.Value))
                {
                    throw new InvalidDataException($"Duplicate message '{groupName}.{id}'.");
                }
            }
        }

        if (groups.Count != Enum.GetValues<MessageGroup>().Length)
        {
            throw new InvalidDataException("The message catalog does not define every required group.");
        }

        return messages;
    }
}
