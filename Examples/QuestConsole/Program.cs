using Spellkit.Hosting;

namespace Spellkit.Examples.QuestConsole;

internal static class Program
{
    private static int Main()
    {
        var host = new SpellkitHost();
        var environment = new SpellkitEnvironment()
            .UseOutput(Console.Write);

        using var instance = host.CreateInstance(environment);
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "town.kit");

        Console.WriteLine("Quest Console");
        Console.WriteLine("A Script-owned quest state driven by a C# interactive-select host.");

        var initialization = instance.ExecuteFile(scriptPath);
        if (!initialization.Success)
        {
            Console.Error.WriteLine(initialization.Failure?.Message);
            return 1;
        }

        using var town = instance.OpenSelect("quest.town");
        RunSelect(town);
        if (!town.IsCompleted)
        {
            Console.WriteLine("\nThe town console was cancelled.");
            return 0;
        }

        Console.WriteLine("\nThe town console closes.");
        return 0;
    }

    private static void RunSelect(SpellkitSelectSession select)
    {
        while (!select.IsCompleted)
        {
            var choices = select.Choices;
            Console.WriteLine();
            Console.WriteLine("[town]");
            for (var i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                Console.WriteLine($"  {i + 1}. {choice.Label}");
                if (!string.IsNullOrWhiteSpace(choice.Description))
                {
                    Console.WriteLine($"     {choice.Description}");
                }
            }

            Console.Write("Select a number or ID (or 'quit'): ");
            var input = Console.ReadLine()?.Trim();
            if (input is null or "quit")
            {
                select.Cancel();
                return;
            }

            string choiceId = int.TryParse(input, out var index) && index is > 0 and <= int.MaxValue
                && index <= choices.Count
                ? choices[index - 1].Id
                : input;
            if (!choices.Any(choice => choice.Id == choiceId))
            {
                Console.WriteLine("That choice is not available.");
                continue;
            }

            select.Select(choiceId);
        }
    }
}
