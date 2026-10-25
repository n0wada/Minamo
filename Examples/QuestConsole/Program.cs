using Minamo.Hosting;

namespace Minamo.Examples.QuestConsole;

internal static class Program
{
    private static async Task<int> Main()
    {
        var host = new MinamoHost();
        var environment = new MinamoEnvironment()
            .UseOutput(Console.Write);

        using var instance = host.CreateInstance(environment);
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "town.nami");

        Console.WriteLine("Quest Console");
        Console.WriteLine("A Script-owned quest state driven by a C# interactive-select host.");

        var initialization = await instance.ExecuteFileAsync(scriptPath);
        if (!initialization.Success)
        {
            Console.Error.WriteLine(initialization.Failure?.Message);
            return 1;
        }

        var opened = await instance.ExecuteAsync("open quest.town");
        if (!opened.Success || opened.Select is null)
        {
            Console.Error.WriteLine(opened.Failure?.Message ?? "The town select was not opened.");
            return 1;
        }

        using var town = opened.Select;
        await RunSelect(town);
        if (!town.IsCompleted)
        {
            Console.WriteLine("\nThe town console was cancelled.");
            return 0;
        }

        Console.WriteLine("\nThe town console closes.");
        return 0;
    }

    private static async Task RunSelect(MinamoSelect select)
    {
        while (!select.IsCompleted)
        {
            var token = select.Token;
            IReadOnlyList<MinamoSelectAction> actions = select.Actions.Where(action => !action.IsHidden).ToArray();
            Console.WriteLine();
            Console.WriteLine("[town]");
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                Console.WriteLine($"  {i + 1}. {DisplayText(action)}");
            }

            Console.Write("Select a number or ID (or 'quit'): ");
            var input = Console.ReadLine()?.Trim();
            if (input is null or "quit")
            {
                return;
            }

            var selectedAction = int.TryParse(input, out var index) && index is > 0 and <= int.MaxValue
                && index <= actions.Count
                ? actions[index - 1]
                : actions.FirstOrDefault(candidate => string.Equals(
                    candidate.Id,
                    input,
                    StringComparison.Ordinal));
            if (selectedAction is null)
            {
                Console.WriteLine($"Action '{input}' is not available.");
                continue;
            }

            await select.SelectAsync(selectedAction.Id, expectedToken: token);
        }
    }

    private static string DisplayText(MinamoSelectAction action)
    {
        var metadata = action.Metadata?.GetValue<Dictionary<string, object?>>();
        return metadata is not null
            && metadata.TryGetValue("text", out var text)
            && text is string value
                ? value
                : action.Id;
    }
}
