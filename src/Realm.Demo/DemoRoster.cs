using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The roster of a Demo session (02 section 9.2): four people, the wagon under Vehicles, and two entries under Not tracked (the prince, moved there
/// automatically, and the hatchback, moved by hand). Kept in memory for the circuit, so a move or a rename in Settings shows on the map and is gone
/// with the session. It sends no notification (D113).
/// </summary>
public sealed class DemoRoster : IRosterEditor
{
    /// <summary>The Home Assistant entity id of each cast member, in the roster.</summary>
    public static readonly IReadOnlyDictionary<string, string> CastIdByEntity = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["person.king"] = DemoCast.King.Id,
        ["person.queen"] = DemoCast.Queen.Id,
        ["person.jester"] = DemoCast.Jester.Id,
        ["person.cryptid"] = DemoCast.Cryptid.Id,
        ["person.prince"] = DemoCast.Prince.Id,
        ["device_tracker.wagon"] = DemoCast.Wagon.Id,
        ["device_tracker.hatchback"] = DemoCast.Chariot.Id,
    };

    private readonly object _gate = new();
    private IReadOnlyList<RosterEntry> _entries = Seed();

    /// <inheritdoc />
    public IReadOnlyList<RosterEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries;
            }
        }
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public Task MoveAsync(string entityId, RosterGroup group, int? index = null, CancellationToken cancellationToken = default)
    {
        Apply(entries => RosterRules.Move(entries, entityId, group, index));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(string entityId, string displayName, string? loreTitle, string? color, CancellationToken cancellationToken = default)
    {
        Apply(entries => RosterRules.Update(entries, entityId, displayName, loreTitle, color));
        return Task.CompletedTask;
    }

    private void Apply(Func<IReadOnlyList<RosterEntry>, IReadOnlyList<RosterEntry>> change)
    {
        lock (_gate)
        {
            _entries = change(_entries);
        }

        Changed?.Invoke();
    }

    private static IReadOnlyList<RosterEntry> Seed()
    {
        var anchor = DemoDataSource.Anchor;
        const string both = "Home Assistant + Life360";

        RosterEntry Person(string entity, DemoMember cast, string source, RosterGroup group, int order, DateTimeOffset? autoMoved = null) =>
            new(entity, RosterKind.Person, group, cast.Name, cast.Lore, cast.Color, order, source, anchor.AddDays(-60), autoMoved is null ? anchor : anchor.AddDays(-9), autoMoved);

        return RosterRules.Sorted(
        [
            Person("person.king", DemoCast.King, both, RosterGroup.People, 0),
            Person("person.queen", DemoCast.Queen, both, RosterGroup.People, 1),
            Person("person.jester", DemoCast.Jester, "Life360", RosterGroup.People, 2),
            Person("person.cryptid", DemoCast.Cryptid, "Life360", RosterGroup.People, 3),
            new RosterEntry("device_tracker.wagon", RosterKind.Tracker, RosterGroup.Vehicles, DemoCast.Wagon.Name, DemoCast.Wagon.Lore, "#A5B4FC", 0, "Home Assistant", anchor.AddDays(-60), anchor, null),
            Person("person.prince", DemoCast.Prince, "Home Assistant", RosterGroup.NotTracked, 0, autoMoved: anchor.AddDays(-2)),
            new RosterEntry("device_tracker.hatchback", RosterKind.Tracker, RosterGroup.NotTracked, DemoCast.Chariot.Name, DemoCast.Chariot.Lore, "#F9A8D4", 1, "Home Assistant", anchor.AddDays(-60), anchor.AddDays(-20), null),
        ]);
    }
}
