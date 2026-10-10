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

    // Who each Demo entry is in Home Assistant and Life360, for the edit panel (D117): the entity ids are the ones the Demo data source uses for its fixes.
    private static readonly IReadOnlyDictionary<string, RosterIdentity> Identities = new Dictionary<string, RosterIdentity>(StringComparer.Ordinal)
    {
        ["person.king"] = PersonIdentity("person.king", DemoCast.King.Name, "king", true),
        ["person.queen"] = PersonIdentity("person.queen", DemoCast.Queen.Name, "queen", true),
        ["person.jester"] = PersonIdentity("person.jester", DemoCast.Jester.Name, "jester", false),
        ["person.cryptid"] = PersonIdentity("person.cryptid", DemoCast.Cryptid.Name, "cryptid", false),
        ["person.prince"] = new RosterIdentity([new RosterIdentityEntity("person.prince", DemoCast.Prince.Name, "Person")], null, false),
        ["device_tracker.wagon"] = new RosterIdentity([new RosterIdentityEntity("device_tracker.wagon", DemoCast.Wagon.Name, "Tracker")], null, false),
        ["device_tracker.hatchback"] = new RosterIdentity([new RosterIdentityEntity("device_tracker.hatchback", DemoCast.Chariot.Name, "Tracker")], null, false),
    };

    private readonly object _gate = new();
    private IReadOnlyList<RosterEntry> _entries;

    /// <summary>The roster a Demo session starts with (see the class remarks).</summary>
    public DemoRoster()
        : this(everyone: false)
    {
    }

    private DemoRoster(bool everyone) => _entries = Seed(everyone);

    /// <summary>
    /// A roster with all seven roles on the map: the prince under People and the hatchback under Vehicles, as the Demo of 0.1 showed them. For tests that
    /// need the whole cast; the Demo itself starts with the prince and the hatchback under Not tracked.
    /// </summary>
    public static DemoRoster EveryoneOnTheMap() => new(everyone: true);

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

    /// <inheritdoc />
    public Task EditAsync(string entityId, string displayName, string? loreTitle, string? color, string? icon, CancellationToken cancellationToken = default)
    {
        Apply(entries => RosterRules.Edit(entries, entityId, displayName, loreTitle, color, icon));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ResetAsync(string entityId, CancellationToken cancellationToken = default)
    {
        Apply(entries => RosterRules.Reset(entries, entityId));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetKeepHistoryAsync(string entityId, bool keep, CancellationToken cancellationToken = default)
    {
        Apply(entries => RosterRules.SetKeepHistory(entries, entityId, keep));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string SnapshotIdOf(RosterEntry entry) => CastIdByEntity.GetValueOrDefault(entry.EntityId, entry.Id);

    /// <inheritdoc />
    public RosterIdentity IdentityOf(string entityId) => Identities.GetValueOrDefault(entityId) ?? RosterIdentity.Unknown;

    private static RosterIdentity PersonIdentity(string entityId, string name, string key, bool life360) =>
        life360
            ? new RosterIdentity(
                [
                    new RosterIdentityEntity(entityId, name, "Person"),
                    new RosterIdentityEntity($"device_tracker.life360_{key}", "Life360 " + name, "Life360 tracker"),
                    new RosterIdentityEntity($"device_tracker.{key}_phone", name + "'s phone", "Phone app tracker"),
                ],
                name,
                false)
            : new RosterIdentity([new RosterIdentityEntity(entityId, name, "Person"), new RosterIdentityEntity($"device_tracker.life360_{key}", "Life360 " + name, "Life360 tracker")], name, false);

    private void Apply(Func<IReadOnlyList<RosterEntry>, IReadOnlyList<RosterEntry>> change)
    {
        lock (_gate)
        {
            _entries = change(_entries);
        }

        Changed?.Invoke();
    }

    private static IReadOnlyList<RosterEntry> Seed(bool everyone)
    {
        var anchor = DemoDataSource.Anchor;
        const string both = "Home Assistant + Life360";

        RosterEntry Person(string entity, DemoMember cast, string source, RosterGroup group, int order, DateTimeOffset? autoMoved = null) =>
            new(entity, RosterKind.Person, group, cast.Name, cast.Lore, cast.Color, order, source, anchor.AddDays(-60), autoMoved is null ? anchor : anchor.AddDays(-9), autoMoved)
            {
                SourceName = cast.Name,
                SourceTitle = cast.Lore,
                SourceColor = cast.Color,
            };

        return RosterRules.Sorted(
        [
            Person("person.king", DemoCast.King, both, RosterGroup.People, 0),
            Person("person.queen", DemoCast.Queen, both, RosterGroup.People, 1),
            Person("person.jester", DemoCast.Jester, "Life360", RosterGroup.People, 2),
            Person("person.cryptid", DemoCast.Cryptid, "Life360", RosterGroup.People, 3),
            new RosterEntry("device_tracker.wagon", RosterKind.Tracker, RosterGroup.Vehicles, DemoCast.Wagon.Name, DemoCast.Wagon.Lore, "#A5B4FC", 0, "Home Assistant", anchor.AddDays(-60), anchor, null) { SourceName = DemoCast.Wagon.Name, SourceTitle = DemoCast.Wagon.Lore, SourceColor = "#A5B4FC", Icon = RosterIcons.TokenOf(DemoCast.Wagon.Glyph) },
            everyone
                ? Person("person.prince", DemoCast.Prince, "Home Assistant", RosterGroup.People, 4)
                : Person("person.prince", DemoCast.Prince, "Home Assistant", RosterGroup.NotTracked, 0, autoMoved: anchor.AddDays(-2)),
            everyone
                ? new RosterEntry("device_tracker.hatchback", RosterKind.Tracker, RosterGroup.Vehicles, DemoCast.Chariot.Name, DemoCast.Chariot.Lore, "#F9A8D4", 1, "Home Assistant", anchor.AddDays(-60), anchor.AddDays(-20), null) { SourceName = DemoCast.Chariot.Name, SourceTitle = DemoCast.Chariot.Lore, SourceColor = "#F9A8D4" }
                : new RosterEntry("device_tracker.hatchback", RosterKind.Tracker, RosterGroup.NotTracked, DemoCast.Chariot.Name, DemoCast.Chariot.Lore, "#F9A8D4", 1, "Home Assistant", anchor.AddDays(-60), anchor.AddDays(-20), null) { SourceName = DemoCast.Chariot.Name, SourceTitle = DemoCast.Chariot.Lore, SourceColor = "#F9A8D4" },
        ]);
    }
}
