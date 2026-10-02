using Realm.Domain;

namespace Realm.Infrastructure.Options;

/// <summary>One entry of the <c>members</c> option (02 sections 3.1 and 3.2). A blank optional text reads as null.</summary>
/// <param name="SortOrder">The option <c>sort_order</c>; null when unset, which means the order of the list (02 section 2.1).</param>
/// <param name="Avatar">The lower-case avatar source: <c>auto</c> (default), <c>person</c>, <c>life360</c> or <c>none</c> (02 section 2.6).</param>
/// <param name="InDrivingReport">Defaults to true (02 section 2.3 rule 5).</param>
/// <param name="StaticShowAddress">Defaults to false (D24).</param>
public sealed record MemberOption(
    string Id,
    string DisplayName,
    string? LoreTitle,
    MemberKind Kind,
    string? Person,
    string? Life360Tracker,
    string? CompanionTracker,
    string Avatar,
    string? Color,
    int? SortOrder,
    bool InDrivingReport,
    string? StaticLabel,
    string? StaticAddress,
    double? StaticLatitude,
    double? StaticLongitude,
    bool StaticShowAddress);
