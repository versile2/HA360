using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// One person of the fictional demo cast (02 section 9.2). Every string of the fixture lives in <see cref="DemoCast"/>;
/// tests and the exporter read them from there.
/// </summary>
/// <param name="Id">The role id: king, queen, jester, cryptid or prince.</param>
/// <param name="Lore">The lore title shown beside the name.</param>
/// <param name="Color">The member colour (UX 7.5).</param>
/// <param name="PersonUserId">The HA user id that resolves to this member in the demo; null for most.</param>
/// <param name="PhoneCapable">True only where the screen-interactive sensor is enabled (the default fixture of 02 section 9.4 gives phone-use data to this member alone).</param>
/// <param name="Address">The Life360 free-text address fed to the address parser; null where the fixture shows none.</param>
/// <param name="StaticLabel">The pin text of a static member (D24); null for live members.</param>
public record DemoMember(
    string Id,
    string Name,
    string Lore,
    string Color,
    MemberKind Kind,
    int SortOrder,
    string? PersonUserId,
    bool PhoneCapable,
    string? Address,
    string? StaticLabel);
