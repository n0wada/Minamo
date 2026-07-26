using Spellkit.Hosting;

namespace Spellkit.Examples.QuestConsole;

public sealed class QuestGame
{
    public bool CourierQuestKnown { get; private set; }

    public bool CourierQuestAccepted { get; private set; }

    public void HearAboutCourier()
    {
        CourierQuestKnown = true;
        Console.WriteLine("\nGuard: A courier vanished near the old bridge.");
    }

    public void AcceptCourierQuest()
    {
        CourierQuestAccepted = true;
        Console.WriteLine("\nQuest accepted: Find the missing courier.");
    }

    public void ShowBoard()
    {
        Console.WriteLine(CourierQuestAccepted
            ? "\nQuest board: You have one active quest."
            : "\nQuest board: Ask the guard about work in the town square.");
    }

    public string Status() => CourierQuestAccepted
        ? "courier quest accepted"
        : CourierQuestKnown ? "courier quest available" : "no active quest";
}

[SpellkitModule("game")]
public sealed class QuestCommands(QuestGame game)
{
    [SpellkitCommand(Description = "Makes the guard reveal the missing-courier quest.")]
    public void HearAboutCourier() => game.HearAboutCourier();

    [SpellkitCommand(Description = "Returns whether the courier quest can be accepted.")]
    public bool CanAcceptCourierQuest() => game.CourierQuestKnown && !game.CourierQuestAccepted;

    [SpellkitCommand(Description = "Accepts the courier quest.")]
    public void AcceptCourierQuest() => game.AcceptCourierQuest();

    [SpellkitCommand(Description = "Shows the host-owned quest-board state.")]
    public void ShowBoard() => game.ShowBoard();

    [SpellkitCommand(Description = "Returns whether the courier quest is active.")]
    public bool HasCourierQuest() => game.CourierQuestAccepted;
}
