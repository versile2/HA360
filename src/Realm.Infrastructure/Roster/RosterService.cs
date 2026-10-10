using Microsoft.Extensions.Logging;
using Realm.Domain;

namespace Realm.Infrastructure.Roster;

/// <summary>
/// The Live roster (D113, 02 section 2.7): who is on the map, kept in memory and stored in the app database (<c>roster</c> table), never in the add-on options.
/// It is read from the database once, by <see cref="LoadAsync"/>; the discovery refresher runs <see cref="ReconcileAsync"/> after every discovery (new and retired
/// entities, <see cref="RosterRules.Reconcile"/>); the Settings dialog moves and edits entries through <see cref="IRosterEditor"/>. Every change is written through
/// <see cref="IRealmWriter"/> (the one writer of the database) before it becomes visible, so a failed write changes nothing. <see cref="Changed"/> follows each
/// change that Settings shows.
/// </summary>
public sealed class RosterService : IRosterEditor
{
    private readonly IRealmQueries _queries;
    private readonly IRealmWriter _writer;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile IReadOnlyList<RosterEntry> _entries = [];
    private volatile IReadOnlyDictionary<string, RosterIdentity> _identities = new Dictionary<string, RosterIdentity>();
    private bool _loaded;

    public RosterService(IRealmQueries queries, IRealmWriter writer, TimeProvider time, ILogger<RosterService> logger)
    {
        _queries = queries;
        _writer = writer;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<RosterEntry> Entries => _entries;

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>True once <see cref="LoadAsync"/> has read the database.</summary>
    public bool Loaded => Volatile.Read(ref _loaded);

    /// <summary>Reads the roster from the database. The first call does it; later calls do nothing.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_loaded)
            {
                return;
            }

            _entries = RosterRules.Sorted(await _queries.GetRosterAsync(cancellationToken));
            Volatile.Write(ref _loaded, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Applies one discovery pass (<see cref="RosterRules.Reconcile"/>) and stores what changed. Returns the reconciliation, whose notices the caller sends once
    /// this has returned (it is stored by then). <see cref="Changed"/> is raised when the pass changed anything Settings shows.
    /// </summary>
    public async Task<RosterReconciliation> ReconcileAsync(IReadOnlyList<RosterCandidate> candidates, IReadOnlySet<string> recentTrackers, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        RosterReconciliation result;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            result = RosterRules.Reconcile(_entries, candidates, recentTrackers, _time.GetUtcNow());
            if (result.Changed.Count > 0)
            {
                await _writer.WriteRosterAsync(result.Changed, cancellationToken);
            }

            _entries = result.Entries;
        }
        finally
        {
            _gate.Release();
        }

        if (result.VisibleChange)
        {
            RaiseChanged();
        }

        return result;
    }

    /// <inheritdoc />
    public Task MoveAsync(string entityId, RosterGroup group, int? index = null, CancellationToken cancellationToken = default) =>
        ApplyAsync(entries => RosterRules.Move(entries, entityId, group, index), cancellationToken);

    /// <inheritdoc />
    public Task UpdateAsync(string entityId, string displayName, string? loreTitle, string? color, CancellationToken cancellationToken = default) =>
        ApplyAsync(entries => RosterRules.Update(entries, entityId, displayName, loreTitle, color), cancellationToken);

    /// <inheritdoc />
    public Task EditAsync(string entityId, string displayName, string? loreTitle, string? color, string? icon, CancellationToken cancellationToken = default) =>
        ApplyAsync(entries => RosterRules.Edit(entries, entityId, displayName, loreTitle, color, icon), cancellationToken);

    /// <inheritdoc />
    public Task SetKeepHistoryAsync(string entityId, bool keep, CancellationToken cancellationToken = default) =>
        ApplyAsync(entries => RosterRules.SetKeepHistory(entries, entityId, keep), cancellationToken);

    /// <inheritdoc />
    public Task ResetAsync(string entityId, CancellationToken cancellationToken = default) =>
        ApplyAsync(entries => RosterRules.Reset(entries, entityId), cancellationToken);

    /// <inheritdoc />
    public RosterIdentity IdentityOf(string entityId) => _identities.GetValueOrDefault(entityId) ?? RosterIdentity.Unknown;

    /// <summary>Replaces what is known about who each entity is (the last discovery's entities and friendly names). Raises <see cref="Changed"/> when it differs from before, so Settings shows it.</summary>
    public void SetIdentities(IReadOnlyDictionary<string, RosterIdentity> identities)
    {
        var before = _identities;
        _identities = identities;
        if (before.Count != identities.Count || identities.Any(pair => !before.TryGetValue(pair.Key, out var old) || !SameIdentity(old, pair.Value)))
        {
            RaiseChanged();
        }
    }

    private static bool SameIdentity(RosterIdentity left, RosterIdentity right) =>
        left.HasPhoto == right.HasPhoto && left.Life360Name == right.Life360Name && left.Entities.SequenceEqual(right.Entities);

    // Computes the next roster from the current one under the gate, stores the rows that differ, and only then makes it current.
    private async Task ApplyAsync(Func<IReadOnlyList<RosterEntry>, IReadOnlyList<RosterEntry>> edit, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        var changed = false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var before = _entries;
            var after = RosterRules.Sorted(edit(before));
            var known = before.ToDictionary(e => e.EntityId, StringComparer.Ordinal);
            var rows = after.Where(e => !known.TryGetValue(e.EntityId, out var old) || old != e).ToList();
            if (rows.Count == 0)
            {
                return;
            }

            try
            {
                await _writer.WriteRosterAsync(rows, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("The roster could not be stored ({ErrorType}); the change was not made", ex.GetType().Name);
                throw;
            }

            _entries = after;
            changed = true;
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A roster subscriber failed");
        }
    }
}
