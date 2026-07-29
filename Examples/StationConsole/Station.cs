using Spellkit.Hosting;
using Spellkit.Runtime.Types;

namespace Spellkit.Examples.StationConsole;

[SpellkitModule("station")]
public sealed class StationCommands
{
    private readonly Station station;

    internal StationCommands(Station station) => this.station = station;

    [SpellkitCommand(Description = "Returns a compact station status line.",
        Capability = "station.read")]
    public string Status() => station.Status();

    [SpellkitProperty(Description = "Returns the current oxygen percentage.",
        Capability = "station.read")]
    public double OxygenLevel => station.OxygenLevel;

    [SpellkitCommand(Description = "Writes a message to the station console.",
        Capability = "station.control")]
    public void Broadcast(string message) =>
        Console.WriteLine($"[station] {message}");

    [SpellkitCommand(Description = "Returns the instance-scoped reactor handle.",
        Capability = "station.read")]
    public SpellkitObject Reactor(SpellkitCommandContext context) =>
        context.Resource(station.ReactorResource);

    [SpellkitCommand(Description = "Returns an instance-scoped handle for a named door.",
        Capability = "station.read")]
    public SpellkitObject Door(SpellkitCommandContext context, string zone) =>
        context.Resource(station.GetDoorResource(zone));
}

internal sealed class Station
{
    private readonly Dictionary<string, StationDoor> doors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["engineering"] = new("engineering"),
            ["habitat"] = new("habitat"),
            ["observatory"] = new("observatory")
        };
    private readonly Dictionary<string, StationDoorResource> doorResources;

    public StationReactor Reactor { get; } = new();

    public StationReactorResource ReactorResource { get; }

    public double OxygenLevel { get; set; } = 42;

    public Station()
    {
        ReactorResource = new(Reactor);
        doorResources = doors.ToDictionary(
            pair => pair.Key,
            pair => new StationDoorResource(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    public StationDoor GetDoor(string zone) =>
        doors.TryGetValue(zone, out var door)
            ? door
            : throw new ArgumentException($"Unknown station zone '{zone}'.", nameof(zone));

    public StationDoorResource GetDoorResource(string zone) =>
        doorResources.TryGetValue(zone, out var door)
            ? door
            : throw new ArgumentException($"Unknown station zone '{zone}'.", nameof(zone));

    public string Status()
    {
        var locked = doors.Values.Where(door => door.Locked).Select(door => door.Zone);
        var lockedText = string.Join(", ", locked);
        return $"oxygen={OxygenLevel:F0}%, reactor={Reactor.Output:F0}%, "
            + $"locked=[{(lockedText.Length == 0 ? "none" : lockedText)}]";
    }
}

internal sealed class StationReactor
{
    public double Output { get; private set; } = 55;

    public void SetOutput(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Output must be between 0 and 100.");
        }

        Output = value;
    }
}

internal sealed class StationDoor(string zone)
{
    public string Zone { get; } = zone;

    public bool Locked { get; private set; }

    public void Lock() => Locked = true;

    public void Unlock() => Locked = false;
}

[SpellkitResource("Station.Reactor")]
internal sealed class StationReactorResource(StationReactor reactor) : SpellkitResource
{
    [SpellkitCommand]
    public double Output() => reactor.Output;

    [SpellkitCommand(Description = "Changes reactor output.", Capability = "station.control")]
    public void SetOutput(double value) => reactor.SetOutput(value);

    protected override void OnRelease() =>
        Console.WriteLine("[resource] reactor handle released");
}

[SpellkitResource("Station.Door")]
internal sealed class StationDoorResource(StationDoor door) : SpellkitResource
{
    [SpellkitCommand]
    public string Zone() => door.Zone;

    [SpellkitCommand]
    public bool Locked() => door.Locked;

    [SpellkitCommand(Description = "Locks this door.", Capability = "station.control")]
    public void Lock() => door.Lock();

    [SpellkitCommand(Description = "Unlocks this door.", Capability = "station.control")]
    public void Unlock() => door.Unlock();

    protected override void OnRelease() =>
        Console.WriteLine($"[resource] {door.Zone} door handle released");
}
