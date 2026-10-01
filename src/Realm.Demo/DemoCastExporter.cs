using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Realm.Demo;

/// <summary>
/// Writes the ids and strings of <see cref="DemoCast"/> and <see cref="DemoPlaces"/> as JSON, so the TypeScript tests read
/// the fictional cast from the same source as the C# tests (03 section 7.2, 8.1). The CLI mode export-demo-cast is a
/// thin call of this class.
/// </summary>
public static class DemoCastExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep apostrophes and the middle dot as written; nothing here is rendered as HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The cast as indented JSON: members, vehicles, the chariot note and all 15 places (the drawn flag marks the 14 that are shown).</summary>
    public static string ToJson()
    {
        var export = new
        {
            Members = DemoCast.Members.Select(member => new
            {
                member.Id,
                member.Name,
                member.Lore,
                member.Color,
                Kind = member.Kind.ToString().ToLowerInvariant(),
                member.SortOrder,
                member.PersonUserId,
                member.PhoneCapable,
                member.Address,
                member.StaticLabel,
            }),
            Vehicles = DemoCast.Vehicles.Select(vehicle => new
            {
                vehicle.Id,
                vehicle.Name,
                vehicle.Lore,
                Glyph = vehicle.Glyph.ToString().ToLowerInvariant(),
                vehicle.SortOrder,
                vehicle.IsPlaceholder,
                vehicle.PlaceholderNote,
            }),
            DemoCast.ChariotNote,
            Places = DemoPlaces.All.Select(place => new
            {
                place.Id,
                place.ZoneName,
                place.Name,
                place.Subtitle,
                Kind = place.Kind.ToString().ToLowerInvariant(),
                place.Lat,
                place.Lon,
                place.RadiusM,
                Drawn = DemoPlaces.Drawn.Contains(place),
            }),
        };

        return JsonSerializer.Serialize(export, Options);
    }

    /// <summary>Writes <see cref="ToJson"/> to <paramref name="path"/> (UTF-8, no byte-order mark), creating the folder if needed.</summary>
    public static void WriteTo(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, ToJson() + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
