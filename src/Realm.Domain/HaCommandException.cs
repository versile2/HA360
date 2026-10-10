namespace Realm.Domain;

/// <summary>Home Assistant answered a websocket command with an error, or the connection could not carry it. <see cref="Code"/> is HA's error code (<c>unauthorized</c>, <c>unknown_command</c>), or ours (<c>not_connected</c>, <c>timeout</c>).</summary>
public sealed class HaCommandException : Exception
{
    /// <param name="code">The error code.</param>
    /// <param name="message">Home Assistant's own text, or null.</param>
    public HaCommandException(string code, string? message = null)
        : base(string.IsNullOrEmpty(message) ? $"Home Assistant refused the command ({code})" : message)
    {
        Code = code;
    }

    /// <summary>The error code.</summary>
    public string Code { get; }
}
