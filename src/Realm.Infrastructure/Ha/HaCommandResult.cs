namespace Realm.Infrastructure.Ha;

/// <summary>
/// The answer to one websocket command (<see cref="HaWebSocketConnection.CallAsync"/>): <c>success</c> of Home Assistant's <c>result</c> message, or, when it was an
/// error, its error code and text. Our own codes are <c>not_connected</c> (no established connection when the command was sent), <c>connection_lost</c> (the connection
/// ended before the answer came) and <c>timeout</c>.
/// </summary>
/// <param name="Success">True when Home Assistant did what was asked.</param>
/// <param name="ErrorCode">The error code of a failure; null on success.</param>
/// <param name="ErrorMessage">Home Assistant's text of a failure, when it sent one.</param>
public sealed record HaCommandResult(bool Success, string? ErrorCode = null, string? ErrorMessage = null);
