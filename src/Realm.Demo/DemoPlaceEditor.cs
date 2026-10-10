using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The Demo's <see cref="IPlaceEditor"/> (0.2.2, D119): the creation of a place is simulated. The values are checked as the Live editor checks them, the place is added to
/// the session's own list in memory (<see cref="DemoDataSource.AddPlace"/>) and nothing leaves the process, so screenshots and the end-to-end tests can run the whole flow.
/// </summary>
public sealed class DemoPlaceEditor : IPlaceEditor
{
    private readonly DemoDataSource _source;

    /// <param name="source">The Demo data the place is added to.</param>
    public DemoPlaceEditor(DemoDataSource source) => _source = source;

    /// <inheritdoc />
    public Task<PlaceCreateResult> CreateAsync(NewZone zone, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (zone.Problem() is { } problem)
        {
            return Task.FromResult(PlaceCreateResult.Failure(problem));
        }

        _source.AddPlace(zone);
        return Task.FromResult(PlaceCreateResult.Success);
    }
}
