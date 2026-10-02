using System.Text.Json;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// Reading a websocket frame of HA. A frame is one message object or, after <c>coalesce_messages</c>, a JSON array of message objects
/// (research ha-addon-ingress section 4.3); both shapes go through <see cref="Messages"/>, so no caller has to care.
/// </summary>
public static class HaFrame
{
    /// <summary>The messages of a frame in the order HA sent them. Anything that is not a JSON object is skipped.</summary>
    public static IEnumerable<JsonElement> Messages(JsonElement frame)
    {
        switch (frame.ValueKind)
        {
            case JsonValueKind.Object:
                yield return frame;
                break;
            case JsonValueKind.Array:
                foreach (var message in frame.EnumerateArray())
                {
                    if (message.ValueKind == JsonValueKind.Object)
                    {
                        yield return message;
                    }
                }

                break;
        }
    }

    /// <summary>The message's <c>type</c>, or null when it has none.</summary>
    public static string? TypeOf(JsonElement message)
    {
        return message.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString() : null;
    }

    /// <summary>The message's command <c>id</c>, or null when it has none (the auth messages have none).</summary>
    public static int? IdOf(JsonElement message)
    {
        return message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var value) ? value : null;
    }
}
