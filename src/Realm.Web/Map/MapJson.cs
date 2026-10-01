using System.Text.Json;

namespace Realm.Web.Map;

/// <summary>
/// The serializer options of the map interop: System.Text.Json web defaults (camelCase names, case-insensitive reading), which are what
/// Blazor's JS interop uses (03 section 4.2). <c>PayloadContractTests</c> writes the golden payloads with these options.
/// </summary>
public static class MapJson
{
    /// <summary>Shared and read-only once used; do not modify.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
