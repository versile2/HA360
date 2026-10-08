namespace Realm.Domain;

/// <summary>
/// The roster as the Settings dialog sees it (Settings, "Who's on the map"): every entry in its group, and the two things the owner can do to one: move it (to
/// another group, or to another place in its group) and rename it, retitle it or recolour it. A change is stored and applied at once; <see cref="Changed"/>
/// follows. Implemented by the Live roster service (stored in the database) and by the Demo session (kept in memory for the circuit).
/// </summary>
public interface IRosterEditor
{
    /// <summary>All entries, People first, then Vehicles, then Not tracked, each in sort order.</summary>
    IReadOnlyList<RosterEntry> Entries { get; }

    /// <summary>Raised after <see cref="Entries"/> changed (by an edit, or by a discovery that found or retired something). Raised off the UI thread.</summary>
    event Action? Changed;

    /// <summary>
    /// Puts the entry into <paramref name="group"/> at <paramref name="index"/> (0 is first; null or too large means last). Moving it out of Not tracked
    /// clears <see cref="RosterEntry.AutoMovedUtc"/>. An unknown entity id does nothing.
    /// </summary>
    Task MoveAsync(string entityId, RosterGroup group, int? index = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the display name (trimmed, 1 to 32 characters; a blank name keeps the old one), the title (trimmed, at most 48 characters; blank means none) and the
    /// colour (<c>#RRGGBB</c>; anything else keeps the old one). An unknown entity id does nothing.
    /// </summary>
    Task UpdateAsync(string entityId, string displayName, string? loreTitle, string? color, CancellationToken cancellationToken = default);
}

/// <summary>An editor with no entries, for a host that has no roster (a test, or a session that lists nobody).</summary>
public sealed class EmptyRosterEditor : IRosterEditor
{
    /// <summary>The one instance.</summary>
    public static readonly EmptyRosterEditor Instance = new();

    private EmptyRosterEditor()
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<RosterEntry> Entries => [];

    /// <inheritdoc />
    public event Action? Changed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task MoveAsync(string entityId, RosterGroup group, int? index = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task UpdateAsync(string entityId, string displayName, string? loreTitle, string? color, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
