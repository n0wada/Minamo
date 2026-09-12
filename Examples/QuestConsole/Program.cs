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

        using var town = await instance.OpenSelectAsync("quest.town");
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
            var choices = select.Choices;
            Console.WriteLine();
            Console.WriteLine("[town]");
            for (var i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                Console.WriteLine($"  {i + 1}. {DisplayText(choice)}");
            }

            Console.Write("Select a number or ID (or 'quit'): ");
            var input = Console.ReadLine()?.Trim();
            if (input is null or "quit")
            {
                return;
            }

            var selectedChoice = int.TryParse(input, out var index) && index is > 0 and <= int.MaxValue
                && index <= choices.Count
                ? choices[index - 1]
                : choices.FirstOrDefault(candidate => string.Equals(
                    candidate.Id,
                    input,
                    StringComparison.Ordinal));
            if (selectedChoice is null)
            {
                Console.WriteLine($"Choice '{input}' is not available.");
                continue;
            }

            await select.SelectAsync(selectedChoice);
        }
    }

    private static string DisplayText(MinamoChoice choice)
    {
        var metadata = choice.Metadata?.GetValue<Dictionary<string, object?>>();
        return metadata is not null
            && metadata.TryGetValue("text", out var text)
            && text is string value
                ? value
                : choice.Id;
    }
}
