using Spellkit.Hosting;

namespace Spellkit.Examples.OrderWorkflow;

[SpellkitModule("orders")]
public sealed class OrderCommands
{
    private readonly OrderLedger ledger;

    internal OrderCommands(OrderLedger ledger) => this.ledger = ledger;

    [SpellkitCommand(Description = "Records that an accepted order needs payment.",
        Capability = "orders.write")]
    public void RequestPayment(string id) => ledger.RequestPayment(id);

    [SpellkitCommand(Description = "Creates a shipment with a script-selected delivery plan.",
        Capability = "orders.write")]
    public void CreateShipment(string id, string carrier, string priority) =>
        ledger.CreateShipment(id, carrier, priority);

    [SpellkitCommand(Description = "Returns the current lifecycle state for an order.",
        Capability = "orders.read")]
    public string Status(string id) => ledger.Status(id);
}

internal sealed class OrderLedger
{
    private readonly Dictionary<string, string> statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> timeline = new();

    internal void RequestPayment(string id)
    {
        statuses[id] = "payment requested";
        timeline.Add($"{id}: payment requested");
    }

    internal void CreateShipment(string id, string carrier, string priority)
    {
        statuses[id] = $"shipped via {carrier} ({priority})";
        timeline.Add($"{id}: shipped via {carrier} ({priority})");
    }

    internal string Status(string id) =>
        statuses.TryGetValue(id, out var status) ? status : "not accepted";

    internal IReadOnlyList<string> Timeline => timeline;
}
