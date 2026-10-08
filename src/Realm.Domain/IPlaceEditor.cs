namespace Realm.Domain;

/// <summary>
/// Adds a place (0.2.2, D119): the Live implementation asks Home Assistant to create a zone (websocket <c>zone/create</c>) and never keeps a copy, because zones mirror
/// Home Assistant and the new zone comes back through the normal zone sync; the Demo implementation only simulates it (no Home Assistant is involved).
/// </summary>
public interface IPlaceEditor
{
    /// <summary>Creates the place. Never throws for a refusal: the result says what happened, in a sentence fit for a toast. Nothing is kept locally when it fails.</summary>
    Task<PlaceCreateResult> CreateAsync(NewZone zone, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of <see cref="IPlaceEditor.CreateAsync"/>.</summary>
/// <param name="Ok">True when the place was created.</param>
/// <param name="Message">For a failure, one sentence for the owner; null on success.</param>
public sealed record PlaceCreateResult(bool Ok, string? Message = null)
{
    /// <summary>The place was created.</summary>
    public static PlaceCreateResult Success { get; } = new(true);

    /// <summary>The place was not created; <paramref name="message"/> says why.</summary>
    public static PlaceCreateResult Failure(string message) => new(false, message);

    /// <summary>The sentence for a Home Assistant error code (<c>unauthorized</c>, <c>unknown_command</c>, ...). The code itself is shown only when it is not one we know.</summary>
    public static string Describe(string? code) =>
        code switch
        {
            "unauthorized" => "Home Assistant did not allow this app to add a place. Adding a place needs an administrator.",
            "unknown_command" => "This Home Assistant cannot add a place from the app. Add it in Settings, Areas, labels and zones.",
            "invalid_format" => "Home Assistant did not accept these values for a place.",
            "not_connected" or "connection_lost" or "timeout" => "Home Assistant is not connected right now. Try again in a moment.",
            { Length: > 0 and <= 64 } => $"Home Assistant refused to add the place ({code}).",
            _ => "Home Assistant refused to add the place.",
        };
}

/// <summary>An editor for a session that cannot add places: every attempt fails with a sentence.</summary>
public sealed class UnavailablePlaceEditor : IPlaceEditor
{
    /// <summary>The one instance.</summary>
    public static readonly UnavailablePlaceEditor Instance = new();

    private UnavailablePlaceEditor()
    {
    }

    /// <inheritdoc />
    public Task<PlaceCreateResult> CreateAsync(NewZone zone, CancellationToken cancellationToken = default) =>
        Task.FromResult(PlaceCreateResult.Failure("Adding a place is not available here."));
}
