using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Places;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

// Adding a place (0.2.2, D119): the zone/create message, the websocket command channel (a fake server answers it), and the service that turns a refusal into a sentence and rings the zone refresher.
public class ZoneCreateTests
{
    private static readonly NewZone Grandmas = new("  Grandma's  ", 33.12345, -84.54321, 150, "mdi:home-heart");

    // ---- the message ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheMessage_HasTheTypeAndExactlyTheFieldsOfZoneCreate()
    {
        using var document = JsonDocument.Parse(HaZoneCommand.Build(7, Grandmas));
        var root = document.RootElement;

        Assert.Equal(7, root.GetProperty("id").GetInt32());
        Assert.Equal("zone/create", root.GetProperty("type").GetString());
        Assert.Equal("Grandma's", root.GetProperty("name").GetString());
        Assert.Equal(33.12345, root.GetProperty("latitude").GetDouble());
        Assert.Equal(-84.54321, root.GetProperty("longitude").GetDouble());
        Assert.Equal(150, root.GetProperty("radius").GetDouble());
        Assert.Equal("mdi:home-heart", root.GetProperty("icon").GetString());
        Assert.False(root.GetProperty("passive").GetBoolean());
        Assert.Equal(["id", "type", "name", "latitude", "longitude", "radius", "icon", "passive"], root.EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData(1, 25)]
    [InlineData(100, 100)]
    [InlineData(99999, 2000)]
    public void TheRadius_IsKeptInsideTheRangeTheSliderOffers(double given, double sent)
    {
        using var document = JsonDocument.Parse(HaZoneCommand.Build(1, Grandmas with { RadiusM = given }));

        Assert.Equal(sent, document.RootElement.GetProperty("radius").GetDouble());
    }

    // ---- the command channel of the websocket -----------------------------------------------------------------------------------------

    [Fact]
    public async Task ACommand_IsSentAfterTheHandshake_AndItsSuccessIsReturned()
    {
        await using var rig = await ConnectionRig.StartAsync();
        rig.Server.ReplyOk("zone/create", """{"zone":{"id":"grandma_s"}}""");
        var session = await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        var result = await rig.Guard(rig.Connection.CallAsync(HaZoneCommand.Type, writer => HaZoneCommand.WriteFields(writer, Grandmas), TimeSpan.FromSeconds(15), CancellationToken.None), "the zone/create answer");

        Assert.True(result.Success);
        using var sent = JsonDocument.Parse(await session.WaitForMessageAsync("zone/create"));
        Assert.Equal("Grandma's", sent.RootElement.GetProperty("name").GetString());
        Assert.True(sent.RootElement.GetProperty("id").GetInt32() > 0);
    }

    [Theory]
    [InlineData("unauthorized")]
    [InlineData("invalid_format")]
    public async Task ARefusal_ComesBackWithItsCodeAndMessage(string code)
    {
        await using var rig = await ConnectionRig.StartAsync();
        rig.Server.ReplyError("zone/create", code, "Not allowed.");
        await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        var result = await rig.Guard(rig.Connection.CallAsync(HaZoneCommand.Type, writer => HaZoneCommand.WriteFields(writer, Grandmas), TimeSpan.FromSeconds(15), CancellationToken.None), "the refusal");

        Assert.False(result.Success);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal("Not allowed.", result.ErrorMessage);
    }

    [Fact]
    public async Task AHomeAssistantWithoutTheCommand_AnswersUnknownCommand()
    {
        await using var rig = await ConnectionRig.StartAsync();
        await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        var result = await rig.Guard(rig.Connection.CallAsync(HaZoneCommand.Type, writer => HaZoneCommand.WriteFields(writer, Grandmas), TimeSpan.FromSeconds(15), CancellationToken.None), "the unknown_command answer");

        Assert.False(result.Success);
        Assert.Equal("unknown_command", result.ErrorCode);
    }

    [Fact]
    public async Task WithoutAnEstablishedConnection_TheCommandFailsAtOnce_NotConnected()
    {
        await using var rig = await ConnectionRig.StartAsync(start: false);

        var result = await rig.Connection.CallAsync(HaZoneCommand.Type, writer => HaZoneCommand.WriteFields(writer, Grandmas), TimeSpan.FromSeconds(15), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("not_connected", result.ErrorCode);
    }

    [Fact]
    public async Task ASilentHomeAssistant_TimesOutOnTheManualClock()
    {
        await using var rig = await ConnectionRig.StartAsync();
        rig.Server.SilentCommands.Add("zone/create");
        var session = await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);
        var timeout = TimeSpan.FromSeconds(15);

        var call = rig.Connection.CallAsync(HaZoneCommand.Type, writer => HaZoneCommand.WriteFields(writer, Grandmas), timeout, CancellationToken.None);
        await rig.Guard(session.WaitForMessageAsync("zone/create"), "the command reaching the server");
        await rig.TimerAsync(timeout);
        rig.Time.Advance(timeout);
        var result = await rig.Guard(call, "the timeout");

        Assert.False(result.Success);
        Assert.Equal("timeout", result.ErrorCode);
    }

    // ---- the service ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ASuccess_CreatesTheZoneAtHomeAssistant_RingsTheRefresherNow_AndAgainAfterThreeSeconds()
    {
        var gateway = new FakeHaGateway();
        var signal = new ZoneRefreshSignal();
        var time = new ManualTimeProvider(ConnectionRig.Start);
        var service = new ZoneService(gateway, signal, time, NullLogger<ZoneService>.Instance);

        var result = await service.CreateAsync(Grandmas);

        Assert.True(result.Ok);
        Assert.Equal("Grandma's", Assert.Single(gateway.CreatedZones).Name);
        Assert.True(signal.Reader.TryRead(out _), "rung at once");
        Assert.False(signal.Reader.TryRead(out _));
        await time.WaitForTimerAsync(ZoneService.SecondRefreshDelay);
        time.Advance(ZoneService.SecondRefreshDelay);
        Assert.True(await signal.Reader.WaitToReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)), "rung again");
    }

    [Theory]
    [InlineData("unauthorized", "administrator")]
    [InlineData("unknown_command", "cannot add a place")]
    [InlineData("invalid_format", "did not accept")]
    [InlineData("not_connected", "not connected")]
    [InlineData("timeout", "not connected")]
    [InlineData("some_other_code", "some_other_code")]
    public async Task ARefusal_IsASentence_AndNothingIsKept_AndNothingRings(string code, string expectedPart)
    {
        var gateway = new FakeHaGateway { CreateZoneFailure = new HaCommandException(code, "details that stay out of the sentence") };
        var signal = new ZoneRefreshSignal();
        var service = new ZoneService(gateway, signal, new ManualTimeProvider(ConnectionRig.Start), NullLogger<ZoneService>.Instance);

        var result = await service.CreateAsync(Grandmas);

        Assert.False(result.Ok);
        Assert.Contains(expectedPart, result.Message);
        Assert.DoesNotContain("details that stay out", result.Message);
        Assert.False(signal.Reader.TryRead(out _));
    }

    [Fact]
    public async Task AnUnexpectedFailure_IsAGenericSentence()
    {
        var gateway = new FakeHaGateway { CreateZoneFailure = new InvalidOperationException("boom") };
        var service = new ZoneService(gateway, new ZoneRefreshSignal(), new ManualTimeProvider(ConnectionRig.Start), NullLogger<ZoneService>.Instance);

        var result = await service.CreateAsync(Grandmas);

        Assert.False(result.Ok);
        Assert.DoesNotContain("boom", result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task AnInvalidZone_NeverReachesHomeAssistant()
    {
        var gateway = new FakeHaGateway();
        var service = new ZoneService(gateway, new ZoneRefreshSignal(), new ManualTimeProvider(ConnectionRig.Start), NullLogger<ZoneService>.Instance);

        var result = await service.CreateAsync(Grandmas with { Name = "   " });

        Assert.False(result.Ok);
        Assert.Equal("Give the place a name.", result.Message);
        Assert.Empty(gateway.CreatedZones);
    }
}
