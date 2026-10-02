using Realm.Domain;
using Realm.Infrastructure.Ha;
using Xunit;

namespace Realm.Ingestion.Tests;

// 02 section 1.8 and 03 section 9.3: the websocket's state as the HomeAssistant ConnectionVm. "Connected" needs an open socket and a state event or ping
// reply within 90 s; an outage reads Reconnecting for its first 15 s and Unavailable after that; AuthFailed and NotConfigured are Unavailable at once.
public class HaConnectionStatusTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static HaConnectionStatus Status(HaConnectionState state, DateTimeOffset? outageSince = null, DateTimeOffset? lastActivity = null)
    {
        return new HaConnectionStatus(state, outageSince, lastActivity, 1, null);
    }

    [Theory]
    [InlineData(0, ConnectionState.Connected)]
    [InlineData(10, ConnectionState.Connected)]
    [InlineData(90, ConnectionState.Connected)]
    [InlineData(91, ConnectionState.Reconnecting)] // the silence limit ran out one second ago: an outage that is 1 s old
    [InlineData(105, ConnectionState.Reconnecting)] // 15 s into that outage
    [InlineData(106, ConnectionState.Unavailable)]
    public void An_open_socket_is_Connected_while_something_arrived_within_90_seconds(int silentSeconds, ConnectionState expected)
    {
        var status = Status(HaConnectionState.Connected, lastActivity: T0);

        Assert.Equal(expected, status.ToConnectionState(T0.AddSeconds(silentSeconds)));
    }

    [Theory]
    [InlineData(HaConnectionState.Connecting)]
    [InlineData(HaConnectionState.Authenticating)]
    [InlineData(HaConnectionState.Reconnecting)]
    public void An_outage_is_Reconnecting_for_its_first_15_seconds_and_Unavailable_after_that(HaConnectionState state)
    {
        var status = Status(state, outageSince: T0);

        Assert.Equal(ConnectionState.Reconnecting, status.ToConnectionState(T0));
        Assert.Equal(ConnectionState.Reconnecting, status.ToConnectionState(T0.AddSeconds(15)));
        Assert.Equal(ConnectionState.Unavailable, status.ToConnectionState(T0.AddSeconds(15).AddMilliseconds(1)));
        Assert.Equal(ConnectionState.Unavailable, status.ToConnectionState(T0.AddMinutes(10)));
    }

    [Theory]
    [InlineData(HaConnectionState.AuthFailed)]
    [InlineData(HaConnectionState.NotConfigured)]
    public void A_refused_token_and_a_missing_token_are_Unavailable_at_once(HaConnectionState state)
    {
        var status = Status(state, outageSince: T0);

        Assert.Equal(ConnectionState.Unavailable, status.ToConnectionState(T0));
    }

    [Fact]
    public void The_ConnectionVm_is_the_HomeAssistant_entry_and_carries_the_last_activity_as_the_last_sync()
    {
        var status = Status(HaConnectionState.Connected, lastActivity: T0);

        var vm = status.ToConnectionVm(T0.AddSeconds(5));

        Assert.Equal(ConnectionNames.HomeAssistant, vm.Name);
        Assert.Equal(ConnectionState.Connected, vm.State);
        Assert.Equal(T0, vm.LastSyncUtc);
    }

    [Fact]
    public void Before_the_first_connection_there_is_no_last_sync()
    {
        var vm = Status(HaConnectionState.Connecting, outageSince: T0).ToConnectionVm(T0);

        Assert.Null(vm.LastSyncUtc);
        Assert.Equal(ConnectionState.Reconnecting, vm.State);
    }
}
