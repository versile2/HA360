namespace Realm.Domain;

/// <summary>One drawn zone. Subtitle is an empty string when there is none.</summary>
public record PlaceVm(
    string Id,
    string DisplayName,
    string Subtitle,
    PlaceKind Kind,
    double Lat,
    double Lon,
    double RadiusM,
    IReadOnlyList<string> MemberIdsInside,
    IReadOnlyList<string> VehicleIdsInside);
