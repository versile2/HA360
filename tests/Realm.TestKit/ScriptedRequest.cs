namespace Realm.TestKit;

/// <summary>One request seen by <see cref="ScriptedHttpHandler"/>; header values of one name are joined with a comma.</summary>
public sealed record ScriptedRequest(HttpMethod Method, Uri? Uri, IReadOnlyDictionary<string, string> Headers, string? Body, DateTimeOffset At);
