namespace Realm.Domain;

public enum ConnectionState
{
    Connected,
    Reconnecting,
    Unavailable,
    NotConnected,
}
