namespace Realm.Domain;

/// <summary>The member the viewer is "me", and how it was found. Only the MemberId slug leaves the server-side render.</summary>
public record ViewerResolution(
    string? MemberId,
    ViewerSource Source);
