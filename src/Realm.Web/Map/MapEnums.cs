using System.Text.Json.Serialization;

namespace Realm.Web.Map;

// The closed sets of the interop payloads (03 section 4.5). Every member carries the exact string JavaScript expects, so the wire
// spelling is visible here and does not depend on a naming policy (the checker in tests/contract/payloadShape.mjs pins the same sets).

/// <summary>The status of a member's pin, the first matching row of 01 section 4.3.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MemberStatus>))]
public enum MemberStatus
{
    [JsonStringEnumMemberName("static")]
    Static,

    [JsonStringEnumMemberName("offline")]
    Offline,

    [JsonStringEnumMemberName("stale")]
    Stale,

    [JsonStringEnumMemberName("driving")]
    Driving,

    [JsonStringEnumMemberName("atPlace")]
    AtPlace,

    [JsonStringEnumMemberName("out")]
    Out,

    [JsonStringEnumMemberName("nofix")]
    NoFix,
}

/// <summary>The top-right badge of a member pin (01 section 4.2); no badge is <c>null</c> in the payload.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PinBadge>))]
public enum PinBadge
{
    [JsonStringEnumMemberName("driving")]
    Driving,

    [JsonStringEnumMemberName("stale")]
    Stale,

    [JsonStringEnumMemberName("offline")]
    Offline,

    [JsonStringEnumMemberName("home")]
    Home,
}

/// <summary>The glyph of a vehicle pin (01 section 4.7).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MapGlyph>))]
public enum MapGlyph
{
    [JsonStringEnumMemberName("pickup")]
    Pickup,

    [JsonStringEnumMemberName("car")]
    Car,
}

/// <summary>The two layout modes of v1 (01 section 3.1).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MapLayoutMode>))]
public enum MapLayoutMode
{
    [JsonStringEnumMemberName("compact")]
    Compact,

    [JsonStringEnumMemberName("expanded")]
    Expanded,
}

/// <summary>Where the camera stands relative to the default view, computed in JavaScript (01 section 4.11).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecenterState>))]
public enum RecenterState
{
    [JsonStringEnumMemberName("away")]
    Away,

    [JsonStringEnumMemberName("default")]
    Default,

    [JsonStringEnumMemberName("me")]
    Me,
}
