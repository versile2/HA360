using System.Buffers;
using System.Text.Json;
using Realm.Domain;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// The websocket command that creates a zone (0.2.2, D119): <c>{"id":n,"type":"zone/create","name":..,"latitude":..,"longitude":..,"radius":..,"icon":"mdi:..","passive":false}</c>.
/// Home Assistant's <c>zone</c> integration takes exactly these fields and answers with the stored item; the command needs an administrator, so a user that is not one gets
/// the error <c>unauthorized</c>. Pure message building, so it is tested without a socket.
/// </summary>
public static class HaZoneCommand
{
    /// <summary>The command's <c>type</c>.</summary>
    public const string Type = "zone/create";

    /// <summary>Writes the fields of the command after <c>id</c> and <c>type</c>. The name is trimmed and the radius kept inside the range the slider offers.</summary>
    public static void WriteFields(Utf8JsonWriter writer, NewZone zone)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(zone);
        writer.WriteString("name", zone.Name.Trim());
        writer.WriteNumber("latitude", zone.Latitude);
        writer.WriteNumber("longitude", zone.Longitude);
        writer.WriteNumber("radius", NewZone.ClampRadius(zone.RadiusM));
        writer.WriteString("icon", zone.Icon);
        writer.WriteBoolean("passive", false);
    }

    /// <summary>The whole message for command id <paramref name="id"/>, as UTF-8 JSON.</summary>
    public static byte[] Build(int id, NewZone zone)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteString("type", Type);
            WriteFields(writer, zone);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
