using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Realm.Domain;
using Realm.Infrastructure.Diagnostics;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// The Live <c>diagnostics.json</c> (03 sections 2.11 and 9.3): the file has the fields of 2.11 in their order with <c>mode</c> "live"; every value is something a
/// service keeps (a counter, a depth, an instant, a state word) or something the current snapshot already shows; each of the nine fixed warning codes is raised by
/// exactly its condition, at the boundary the specification draws, and by nothing else; and the file holds states, counts and codes only, never a location, an
/// address, a name, a token or the path of the database. Time is manual and every id and name is fictional.
/// </summary>
public sealed class DiagnosticsSnapshotBuilderTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 15, 0, 0, TimeSpan.Zero);

    // The options of the web host's endpoint: camel-case names and string enums.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly string[] TopLevel =
    [
        "schema", "version", "mode", "uptimeSeconds", "zone", "zoneDataOk", "circuits", "connections", "counts", "ha", "ingestion", "db", "members", "warnings",
    ];

    // The nine codes of 03 section 2.11, in the order it lists them.
    private static readonly string[] NineCodes =
    [
        "ha_unavailable",
        "ha_auth_failed",
        "ha_ws_down_over_60s",
        "ingest_drops",
        "writer_queue_over_80pct",
        "unclean_shutdown",
        "zone_data_missing",
        "payload_schema_mismatch",
        "vehicle_sensor_stale",
    ];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "realm-diagnostics-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // ---- the shape -------------------------------------------------------------------------------------------------

    [Fact]
    public void TheFile_HasTheFieldsOfSection2_11InOrder_WithModeLive()
    {
        var rig = new Rig();

        using var document = rig.Document();
        var root = document.RootElement;

        Assert.Equal(TopLevel, root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal("1.2.3", root.GetProperty("version").GetString());
        Assert.Equal("live", root.GetProperty("mode").GetString());
        Assert.Equal("UTC", root.GetProperty("zone").GetString());
        Assert.True(root.GetProperty("zoneDataOk").GetBoolean());
        Assert.Equal(["open", "disconnected"], Names(root, "circuits"));
        Assert.Equal(["members", "vehicles", "places"], Names(root, "counts"));
        Assert.Equal(["websocketState", "reconnects", "lastMessageUtc", "watchedEntities", "messagesPerMinute"], Names(root, "ha"));
        Assert.Equal(["eventsPerMinute", "queueDepth", "dropped"], Names(root, "ingestion"));
        Assert.Equal(["schemaVersion", "uncleanShutdownAtStart", "sizeBytes", "writerQueueDepth", "lastCommitUtc"], Names(root, "db"));
        Assert.Equal(JsonValueKind.Array, root.GetProperty("warnings").ValueKind);
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public void TheCircuits_AreLeftAtZero_ForTheWebHostToFill()
    {
        var snapshot = new Rig().Get();

        Assert.Equal(new DiagnosticsSnapshot.CircuitCounts(0, 0), snapshot.Circuits);
    }

    [Fact]
    public void TheConnections_AreTheSnapshotsFourEntries()
    {
        var rig = new Rig();

        var snapshot = rig.Get();

        Assert.Equal(rig.State.Current.Connections, snapshot.Connections);
        Assert.Equal(
            [ConnectionNames.HomeAssistant, ConnectionNames.Life360Trackers, ConnectionNames.FordPass, ConnectionNames.VehiclePlaceholder],
            snapshot.Connections.Select(connection => connection.Name));
    }

    [Fact]
    public void TheValues_AreWhatTheServicesKeep()
    {
        var rig = new Rig(databasePath: DatabaseFileOf(12_345));
        rig.Time.Advance(TimeSpan.FromSeconds(90));
        var now = rig.Time.GetUtcNow();
        rig.Counters.RecordWsConnected();
        rig.Counters.RecordWsReconnect();
        rig.Counters.RecordWsReconnect();
        rig.Counters.RecordWsMessage(now.AddSeconds(-20));
        rig.Counters.RecordWsMessage(now.AddSeconds(-5));
        rig.Counters.SetWatchedEntities(31);
        rig.Counters.RecordIngestItem(now.AddSeconds(-4));
        rig.Counters.RecordIngestItem(now.AddSeconds(-3));
        rig.Counters.RecordIngestItem(now.AddSeconds(-2));
        rig.Counters.SetIngestQueueDepth(6);
        rig.Counters.RecordStartup(1, uncleanShutdown: false);
        rig.Counters.SetWriterQueue(120, 0);
        rig.Counters.RecordDbCommit(100, now.AddSeconds(-1));
        rig.Publish(NewSnapshot(
            now,
            ConnectionState.Connected,
            members: [Member("king", Freshness.Fresh), Member("queen", Freshness.Stale)],
            vehicles: [Vehicle("wagon", now.AddMinutes(-5))],
            zone: "UTC"));

        var snapshot = rig.Get();

        Assert.Equal(90, snapshot.UptimeSeconds);
        Assert.Equal("UTC", snapshot.Zone);
        Assert.True(snapshot.ZoneDataOk);
        Assert.Equal(new DiagnosticsSnapshot.EntityCounts(2, 1, 0), snapshot.Counts);
        Assert.Equal(new DiagnosticsSnapshot.HaCounters("connected", 2, now.AddSeconds(-5), 31, 2), snapshot.Ha);
        Assert.Equal(new DiagnosticsSnapshot.IngestionCounters(3, 6, 0), snapshot.Ingestion);
        Assert.Equal(new DiagnosticsSnapshot.DbCounters(1, false, 12_345, 120, now.AddSeconds(-1)), snapshot.Db);
        Assert.Empty(snapshot.Warnings);
    }

    [Fact]
    public void TheUptime_IsMeasuredFromTheCountersStart_AndNeverNegative()
    {
        var rig = new Rig();

        Assert.Equal(0, rig.Get().UptimeSeconds);

        rig.Time.Advance(TimeSpan.FromHours(26) + TimeSpan.FromSeconds(7.9));

        Assert.Equal((26 * 3600) + 7, rig.Get().UptimeSeconds);
    }

    [Fact]
    public void BeforeTheFirstMessageAndTheFirstCommit_TheirInstantsAreNull()
    {
        var snapshot = new Rig().Get();

        Assert.Null(snapshot.Ha.LastMessageUtc);
        Assert.Null(snapshot.Db.LastCommitUtc);
    }

    [Fact]
    public void TheMessageAndEventRates_AreTheLastMinuteAtTheMomentTheFileIsRead()
    {
        var rig = new Rig();
        for (var i = 0; i < 10; i++)
        {
            rig.Counters.RecordWsMessage(Start);
            rig.Counters.RecordIngestItem(Start);
        }

        Assert.Equal(10, rig.Get().Ha.MessagesPerMinute);
        Assert.Equal(10, rig.Get().Ingestion.EventsPerMinute);

        rig.Time.Advance(TimeSpan.FromSeconds(61));

        Assert.Equal(0, rig.Get().Ha.MessagesPerMinute);
        Assert.Equal(0, rig.Get().Ingestion.EventsPerMinute);
    }

    [Fact]
    public void TheIngestionDropCount_IsTheSkippedItemsPlusTheRowsTheWriterRefused()
    {
        var rig = new Rig();
        rig.Counters.RecordIngestSkipped();
        rig.Counters.RecordIngestSkipped();
        rig.Counters.SetWriterQueue(10_000, 5);

        Assert.Equal(7, rig.Get().Ingestion.Dropped);
    }

    [Theory]
    [InlineData(HaConnectionState.NotConfigured, "notConfigured")]
    [InlineData(HaConnectionState.Connecting, "connecting")]
    [InlineData(HaConnectionState.Authenticating, "authenticating")]
    [InlineData(HaConnectionState.Connected, "connected")]
    [InlineData(HaConnectionState.Reconnecting, "reconnecting")]
    [InlineData(HaConnectionState.AuthFailed, "authFailed")]
    public void TheWebsocketState_IsOneShortWord(HaConnectionState state, string word)
    {
        var rig = new Rig { Status = new HaConnectionStatus(state, Start, Start, 1, null) };

        Assert.Equal(word, rig.Get().Ha.WebsocketState);
    }

    [Fact]
    public void WithoutAWebsocket_TheStateIsNotConfigured()
    {
        var rig = new Rig { Status = null };

        Assert.Equal("notConfigured", rig.Get().Ha.WebsocketState);
    }

    [Fact]
    public void EveryEnumInTheFile_IsAWordInCamelCase()
    {
        var rig = new Rig();
        rig.Publish(NewSnapshot(Start, ConnectionState.NotConnected, members: [Member("king", Freshness.NoFix)]));

        using var document = rig.Document();

        Assert.Equal("notConnected", document.RootElement.GetProperty("connections")[0].GetProperty("state").GetString());
        Assert.Equal("noFix", document.RootElement.GetProperty("members")[0].GetProperty("freshness").GetString());
    }

    // ---- the members -----------------------------------------------------------------------------------------------

    [Fact]
    public void AMember_IsItsOptionSlugWithItsFreshnessAndThresholdAndNothingElse()
    {
        var rig = new Rig();
        rig.Thresholds["king"] = 50;
        rig.Publish(NewSnapshot(Start, ConnectionState.Connected, members: [Member("king", Freshness.Stale), Member("prince", Freshness.Static)]));

        using var document = rig.Document();
        var members = document.RootElement.GetProperty("members").EnumerateArray().ToArray();

        Assert.Equal(["king", "prince"], members.Select(member => member.GetProperty("id").GetString()));
        Assert.All(members, member => Assert.Equal(["id", "freshness", "staleAfterMinutes"], member.EnumerateObject().Select(property => property.Name)));
        Assert.Equal(["stale", "static"], members.Select(member => member.GetProperty("freshness").GetString()));
        Assert.Equal(50, members[0].GetProperty("staleAfterMinutes").GetInt32());   // the pipeline's threshold, with the heartbeat in it
        Assert.Equal(rig.Options.UiStaleAfterMinutes, members[1].GetProperty("staleAfterMinutes").GetInt32());   // none: the option
    }

    // ---- the database ----------------------------------------------------------------------------------------------

    [Fact]
    public void TheDatabaseSize_IsTheFilesLength_AndZeroForAFileThatIsNotThere()
    {
        var present = new Rig(databasePath: DatabaseFileOf(4_096));
        var missing = new Rig(databasePath: Path.Combine(_folder, "missing", "realm.db"));

        Assert.Equal(4_096, present.Get().Db.SizeBytes);
        Assert.Equal(0, missing.Get().Db.SizeBytes);
    }

    // ---- no warning for a healthy host -----------------------------------------------------------------------------

    [Fact]
    public void AHealthyHost_RaisesNoWarning()
    {
        var rig = new Rig();
        rig.Counters.RecordStartup(1, uncleanShutdown: false);
        rig.Counters.RecordWsMessage(Start);
        rig.Counters.SetWriterQueue(100, 0);
        rig.Publish(NewSnapshot(Start, ConnectionState.Connected, members: [Member("king", Freshness.Fresh)], vehicles: [Vehicle("wagon", Start.AddMinutes(-1))]));

        Assert.Empty(rig.Get().Warnings);
    }

    [Fact]
    public void TheNineCodes_AreExactlyTheOnesOfTheSpecification_AndNothingElseIsDeclared()
    {
        var declared = typeof(WarningCodes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(NineCodes.Order(StringComparer.Ordinal).ToArray(), declared.Order(StringComparer.Ordinal).ToArray());
        Assert.All(declared, code => Assert.Matches("^[a-z0-9_]+$", code));   // a code is a word for the operator, never prose
    }

    // ---- ha_unavailable --------------------------------------------------------------------------------------------

    [Fact]
    public void HaUnavailable_IsRaisedWhenTheHomeAssistantEntryReadsUnavailable()
    {
        var rig = new Rig { Status = null };
        rig.Publish(NewSnapshot(Start, ConnectionState.Unavailable));

        Assert.Equal(["ha_unavailable"], rig.Get().Warnings);
    }

    [Theory]
    [InlineData(ConnectionState.Connected)]
    [InlineData(ConnectionState.Reconnecting)]
    [InlineData(ConnectionState.NotConnected)]
    public void HaUnavailable_IsAbsentForAnyOtherState(ConnectionState state)
    {
        var rig = new Rig();
        rig.Publish(NewSnapshot(Start, state));

        Assert.DoesNotContain("ha_unavailable", rig.Get().Warnings);
    }

    [Fact]
    public void AHostWhoseOptionsWereRefused_RaisesHaUnavailable_FromTheStateItWasBuiltWith()
    {
        // No websocket (null status), and a state built as NotConfigured: the Home Assistant entry reads Unavailable at once (R3-07).
        var time = new ManualTimeProvider(Start);
        var options = OptionsBinding.Defaults;
        var counters = new ServiceCounters(time);
        var builder = new DiagnosticsSnapshotBuilder(RealmState.CreateInitial(options, time, refused: true), options, counters, time, Path.Combine(_folder, "realm.db"), () => null, () => new Dictionary<string, int>(), "1.2.3");

        var snapshot = builder.GetSnapshot();

        Assert.Equal(["ha_unavailable"], snapshot.Warnings);
        Assert.Equal("notConfigured", snapshot.Ha.WebsocketState);
        Assert.Equal("live", snapshot.Mode);
    }

    // ---- ha_auth_failed --------------------------------------------------------------------------------------------

    [Fact]
    public void HaAuthFailed_IsRaisedWhenTheWebsocketIsInItsAuthFailedState()
    {
        var rig = new Rig { Status = new HaConnectionStatus(HaConnectionState.AuthFailed, Start.AddSeconds(-10), null, 1, null) };
        rig.Publish(NewSnapshot(Start, ConnectionState.Unavailable));   // AuthFailed is Unavailable at once (02 section 1.8)

        Assert.Equal(["ha_unavailable", "ha_auth_failed"], rig.Get().Warnings);
    }

    [Theory]
    [InlineData(HaConnectionState.NotConfigured)]
    [InlineData(HaConnectionState.Connecting)]
    [InlineData(HaConnectionState.Authenticating)]
    [InlineData(HaConnectionState.Connected)]
    [InlineData(HaConnectionState.Reconnecting)]
    public void HaAuthFailed_IsAbsentInEveryOtherState(HaConnectionState state)
    {
        var rig = new Rig { Status = new HaConnectionStatus(state, Start.AddSeconds(-10), Start, 1, null) };

        Assert.DoesNotContain("ha_auth_failed", rig.Get().Warnings);
    }

    // ---- ha_ws_down_over_60s ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(61, true)]
    [InlineData(60, false)]   // "over 60 seconds": exactly 60 is not
    [InlineData(59, false)]
    [InlineData(3600, true)]
    public void TheWebsocketDownOver60s_IsRaisedByAnOutageLongerThanSixtySeconds(int outageSeconds, bool raised)
    {
        var rig = new Rig { Status = new HaConnectionStatus(HaConnectionState.Reconnecting, Start.AddSeconds(-outageSeconds), null, 3, null) };

        Assert.Equal(raised, rig.Get().Warnings.Contains("ha_ws_down_over_60s"));
    }

    [Theory]
    [InlineData(HaConnectionState.Connecting)]
    [InlineData(HaConnectionState.Authenticating)]
    [InlineData(HaConnectionState.AuthFailed)]
    public void TheWebsocketDownOver60s_CountsTheOutageOfAnyStateThatIsNotConnected(HaConnectionState state)
    {
        var rig = new Rig { Status = new HaConnectionStatus(state, Start.AddSeconds(-120), null, 4, null) };

        Assert.Contains("ha_ws_down_over_60s", rig.Get().Warnings);
    }

    [Theory]
    [InlineData(151, true)]   // 90 s of silence make the connection count as lost; 61 s after that it has been down for over 60 s
    [InlineData(150, false)]
    [InlineData(91, false)]
    [InlineData(30, false)]
    public void ASilentOpenSocket_IsDownFromTheEndOfItsNinetySeconds(int silenceSeconds, bool raised)
    {
        var rig = new Rig { Status = new HaConnectionStatus(HaConnectionState.Connected, null, Start.AddSeconds(-silenceSeconds), 0, null) };

        Assert.Equal(raised, rig.Get().Warnings.Contains("ha_ws_down_over_60s"));
    }

    [Fact]
    public void TheWebsocketDownOver60s_NeedsAWebsocketWithAnOutageToCountFrom()
    {
        var none = new Rig { Status = null };
        var noToken = new Rig { Status = new HaConnectionStatus(HaConnectionState.NotConfigured, null, null, 0, null) };

        Assert.DoesNotContain("ha_ws_down_over_60s", none.Get().Warnings);
        Assert.DoesNotContain("ha_ws_down_over_60s", noToken.Get().Warnings);
    }

    [Fact]
    public void TheWebsocketDownOver60s_FollowsTheClockWithoutAnyoneTellingTheBuilder()
    {
        var rig = new Rig { Status = new HaConnectionStatus(HaConnectionState.Reconnecting, Start, null, 1, null) };

        Assert.DoesNotContain("ha_ws_down_over_60s", rig.Get().Warnings);

        rig.Time.Advance(TimeSpan.FromSeconds(60));
        Assert.DoesNotContain("ha_ws_down_over_60s", rig.Get().Warnings);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Contains("ha_ws_down_over_60s", rig.Get().Warnings);
    }

    // ---- ingest_drops ----------------------------------------------------------------------------------------------

    [Fact]
    public void IngestDrops_IsRaisedByASkippedItem()
    {
        var rig = new Rig();
        rig.Counters.RecordIngestSkipped();

        Assert.Equal(["ingest_drops"], rig.Get().Warnings);
    }

    [Fact]
    public void IngestDrops_IsRaisedByARowTheWriterDropped()
    {
        var rig = new Rig();
        rig.Counters.SetWriterQueue(8_000, 1);

        Assert.Equal(["ingest_drops"], rig.Get().Warnings);
    }

    [Fact]
    public void IngestDrops_IsAbsentWhileNothingWasDropped()
    {
        var rig = new Rig();
        rig.Counters.SetWriterQueue(8_000, 0);
        rig.Counters.RecordIngestItem(Start);

        Assert.DoesNotContain("ingest_drops", rig.Get().Warnings);
    }

    // ---- writer_queue_over_80pct -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(10_000, true)]
    [InlineData(8_001, true)]
    [InlineData(8_000, false)]   // 80 % of the 10 000 rows of 02 section 7.3 is not "over"
    [InlineData(100, false)]
    [InlineData(0, false)]
    public void WriterQueueOver80Pct_IsRaisedByMoreThanEightThousandQueuedRows(int depth, bool raised)
    {
        var rig = new Rig();
        rig.Counters.SetWriterQueue(depth, 0);

        Assert.Equal(raised, rig.Get().Warnings.Contains("writer_queue_over_80pct"));
        Assert.Equal(depth, rig.Get().Db.WriterQueueDepth);
    }

    // ---- unclean_shutdown ------------------------------------------------------------------------------------------

    [Fact]
    public void UncleanShutdown_IsRaisedWhenTheLastRunDidNotStopCleanly()
    {
        var rig = new Rig();
        rig.Counters.RecordStartup(1, uncleanShutdown: true);

        var snapshot = rig.Get();

        Assert.Equal(["unclean_shutdown"], snapshot.Warnings);
        Assert.True(snapshot.Db.UncleanShutdownAtStart);
    }

    [Fact]
    public void UncleanShutdown_IsAbsentAfterACleanStop()
    {
        var rig = new Rig();
        rig.Counters.RecordStartup(1, uncleanShutdown: false);

        var snapshot = rig.Get();

        Assert.Empty(snapshot.Warnings);
        Assert.False(snapshot.Db.UncleanShutdownAtStart);
    }

    // ---- zone_data_missing -----------------------------------------------------------------------------------------

    [Fact]
    public void ZoneDataMissing_IsRaisedWhenTheStartUpSelfCheckFailed()
    {
        var rig = new Rig();
        rig.Counters.RecordZoneData(ok: false);

        var snapshot = rig.Get();

        Assert.Equal(["zone_data_missing"], snapshot.Warnings);
        Assert.False(snapshot.ZoneDataOk);
    }

    [Fact]
    public void ZoneDataMissing_IsRaisedWhenTheZoneHomeAssistantReportedIsNotOneThisMachineKnows()
    {
        var rig = new Rig();
        rig.Publish(NewSnapshot(Start, ConnectionState.Connected, zone: "Nowhere/Fictional"));

        var snapshot = rig.Get();

        Assert.Equal(["zone_data_missing"], snapshot.Warnings);
        Assert.False(snapshot.ZoneDataOk);
        Assert.Equal("Nowhere/Fictional", snapshot.Zone);
    }

    [Fact]
    public void ZoneDataMissing_IsAbsentWhileTheSelfCheckPassedAndTheZoneIsUtc()
    {
        var rig = new Rig();
        rig.Counters.RecordZoneData(ok: true);

        var snapshot = rig.Get();

        Assert.Empty(snapshot.Warnings);
        Assert.True(snapshot.ZoneDataOk);
    }

    // ---- payload_schema_mismatch -----------------------------------------------------------------------------------

    [Fact]
    public void PayloadSchemaMismatch_IsRaisedOnceTheMapScriptReportedADifferentSchema()
    {
        var rig = new Rig();
        Assert.DoesNotContain("payload_schema_mismatch", rig.Get().Warnings);

        rig.Counters.MarkPayloadSchemaMismatch();

        Assert.Equal(["payload_schema_mismatch"], rig.Get().Warnings);
    }

    // ---- vehicle_sensor_stale --------------------------------------------------------------------------------------

    [Fact]
    public void VehicleSensorStale_IsRaisedWhenAVehiclesLastRefreshIsOlderThanTheThreshold()
    {
        var rig = new Rig();
        var limit = TimeSpan.FromMinutes(rig.Options.UiVehicleStaleAfterMinutes);
        rig.Publish(NewSnapshot(Start, ConnectionState.Connected, vehicles: [Vehicle("wagon", Start - limit - TimeSpan.FromSeconds(1), Freshness.Stale)]));

        Assert.Equal(["vehicle_sensor_stale"], rig.Get().Warnings);
    }

    [Fact]
    public void VehicleSensorStale_IsAbsentAtTheThresholdItself_AndForARecentRefresh()
    {
        var rig = new Rig();
        var limit = TimeSpan.FromMinutes(rig.Options.UiVehicleStaleAfterMinutes);
        rig.Publish(NewSnapshot(Start, ConnectionState.Connected, vehicles: [Vehicle("wagon", Start - limit), Vehicle("car", Start.AddMinutes(-1))]));

        Assert.DoesNotContain("vehicle_sensor_stale", rig.Get().Warnings);
    }

    [Fact]
    public void VehicleSensorStale_IsAbsentForAVehicleHomeAssistantNeverReportedOnAndForAPlaceholder()
    {
        var rig = new Rig();
        rig.Publish(NewSnapshot(
            Start,
            ConnectionState.Connected,
            vehicles: [Vehicle("wagon", null, Freshness.NoFix), Vehicle("spare", Start.AddDays(-30), Freshness.Stale, placeholder: true)]));

        Assert.DoesNotContain("vehicle_sensor_stale", rig.Get().Warnings);
    }

    [Fact]
    public void VehicleSensorStale_FollowsTheOptionsThreshold()
    {
        var rig = new Rig(OptionsBinding.Defaults with { UiVehicleStaleAfterMinutes = 10 });
        rig.Publish(NewSnapshot(Start, ConnectionState.Connected, vehicles: [Vehicle("wagon", Start.AddMinutes(-11), Freshness.Stale)]));

        Assert.Contains("vehicle_sensor_stale", rig.Get().Warnings);

        rig.Publish(NewSnapshot(Start, ConnectionState.Connected, vehicles: [Vehicle("wagon", Start.AddMinutes(-9))]));

        Assert.DoesNotContain("vehicle_sensor_stale", rig.Get().Warnings);
    }

    // ---- all nine together -----------------------------------------------------------------------------------------

    [Fact]
    public void AllNineCodes_AreRaisedTogether_InTheOrderOfTheSpecification()
    {
        var rig = new Rig { Status = new HaConnectionStatus(HaConnectionState.AuthFailed, Start.AddMinutes(-5), null, 5, null) };
        rig.Counters.RecordIngestSkipped();
        rig.Counters.SetWriterQueue(9_000, 0);
        rig.Counters.RecordStartup(1, uncleanShutdown: true);
        rig.Counters.RecordZoneData(ok: false);
        rig.Counters.MarkPayloadSchemaMismatch();
        rig.Publish(NewSnapshot(Start, ConnectionState.Unavailable, vehicles: [Vehicle("wagon", Start.AddHours(-3), Freshness.Stale)]));

        Assert.Equal(NineCodes, rig.Get().Warnings);
    }

    [Fact]
    public void ACodeCarriesNoValue_ItIsRaisedOnceHoweverOftenItsConditionHolds()
    {
        var rig = new Rig();
        for (var i = 0; i < 25; i++)
        {
            rig.Counters.RecordIngestSkipped();
        }

        Assert.Equal(["ingest_drops"], rig.Get().Warnings);
    }

    // ---- what the file never holds ---------------------------------------------------------------------------------

    [Fact]
    public void TheFile_HoldsNoLocationNoAddressNoNameNoTokenAndNoPath()
    {
        var path = DatabaseFileOf(2_048);
        var rig = new Rig(databasePath: path);
        rig.Counters.RecordWsMessage(Start);
        rig.Publish(NewSnapshot(
            Start,
            ConnectionState.Connected,
            members:
            [
                Member("king", Freshness.Fresh) with
                {
                    DisplayName = "Kingsley Fictional",
                    LoreTitle = "Warden of the Fictional Marches",
                    Lat = 38.123456,
                    Lon = -77.654321,
                    Street = "Fictional Parkway",
                    City = "Faketown",
                    Region = "Nowhere",
                    FullAddress = "1 Fictional Parkway, Faketown",
                    BatteryPct = 83,
                    AvatarUrl = "/avatars/king",
                },
            ],
            vehicles: [Vehicle("wagon", Start.AddMinutes(-1), lat: 38.654321, lon: -77.123456)]));

        var text = JsonSerializer.Serialize(rig.Get(), Json);

        foreach (var forbidden in new[] { "\"lat\"", "\"lon\"", "latitude", "longitude", "address", "battery", "token", "entity", "avatar" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var value in new[] { "Kingsley", "Warden", "38.123456", "77.654321", "Fictional Parkway", "Faketown", "Nowhere", "38.654321", "77.123456", "Wagon", Path.GetFileName(_folder), "realm-2048" })
        {
            Assert.DoesNotContain(value, text, StringComparison.Ordinal);
        }

        Assert.Contains("\"king\"", text, StringComparison.Ordinal);   // the member appears by its option slug, and by nothing else
    }

    [Fact]
    public void NoValueTheBuilderIsGiven_CanReachTheFile_ExceptTheOnesTheSpecificationNames()
    {
        // The builder's inputs are the state, the options, the counters, a clock, a path it measures, a status and thresholds. The file is the record of
        // 2.11, which has a place for none of the things above: a string field holds the mode, the zone, a version, a state word, an id, or a code.
        var stringFields = typeof(DiagnosticsSnapshot).GetNestedTypes().Append(typeof(DiagnosticsSnapshot))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.DeclaringType!.Name + "." + property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["DiagnosticsSnapshot.Mode", "DiagnosticsSnapshot.Version", "DiagnosticsSnapshot.Zone", "HaCounters.WebsocketState", "MemberEntry.Id"],
            stringFields);
    }

    // ---- rig and builders ------------------------------------------------------------------------------------------

    private static string[] Names(JsonElement root, string property) => root.GetProperty(property).EnumerateObject().Select(member => member.Name).ToArray();

    // A file of the given length in a folder of this test, whose path must never reach the output.
    private string DatabaseFileOf(int length)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "realm-" + length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".db");
        File.WriteAllBytes(path, new byte[length]);
        return path;
    }

    private static RealmSnapshot NewSnapshot(
        DateTimeOffset now,
        ConnectionState homeAssistant,
        IReadOnlyList<MemberVm>? members = null,
        IReadOnlyList<VehicleVm>? vehicles = null,
        string zone = "UTC") =>
        new(
            ServerNowUtc: now,
            StatsVersion: 0,
            WeekStart: DayOfWeek.Monday,
            Members: members ?? [],
            Vehicles: vehicles ?? [],
            Places: [],
            Connections:
            [
                new ConnectionVm(ConnectionNames.HomeAssistant, homeAssistant, now),
                new ConnectionVm(ConnectionNames.Life360Trackers, ConnectionState.Connected, now),
                new ConnectionVm(ConnectionNames.FordPass, ConnectionState.NotConnected, null),
                new ConnectionVm(ConnectionNames.VehiclePlaceholder, ConnectionState.NotConnected, null),
            ],
            Zone: zone,
            UnitSystem: UnitSystem.Imperial);

    private static MemberVm Member(string id, Freshness freshness) =>
        new(
            Id: id,
            DisplayName: id,
            LoreTitle: null,
            AvatarUrl: null,
            Color: "#C0FFEE",
            Kind: freshness == Freshness.Static ? MemberKind.Static : MemberKind.Live,
            Lat: null,
            Lon: null,
            AccuracyM: null,
            BatteryPct: null,
            Charging: null,
            BatteryAsOfUtc: null,
            IsDriving: false,
            SpeedMps: null,
            Street: null,
            City: null,
            Region: null,
            FullAddress: null,
            PlaceId: null,
            SinceUtc: null,
            LastUpdateUtc: null,
            SortOrder: 0,
            Freshness: freshness);

    private static VehicleVm Vehicle(string id, DateTimeOffset? lastUpdate, Freshness freshness = Freshness.Fresh, bool placeholder = false, double? lat = null, double? lon = null) =>
        new(
            Id: id,
            Name: "Wagon",
            LoreTitle: null,
            Glyph: VehicleGlyph.Pickup,
            Lat: lat,
            Lon: lon,
            Street: null,
            PlaceId: null,
            Ignition: null,
            RemoteStartSecondsLeft: null,
            FuelPct: null,
            OdometerM: null,
            LastUpdateUtc: lastUpdate,
            SpeedMps: null,
            IsMoving: false,
            Freshness: freshness,
            IsPlaceholder: placeholder,
            PlaceholderNote: null);

    // A builder over a manual clock, counters of its own and a state that reads healthy: Home Assistant connected, nobody and nothing yet.
    private sealed class Rig
    {
        public Rig(RealmOptions? options = null, string? databasePath = null)
        {
            Options = options ?? OptionsBinding.Defaults;
            Time = new ManualTimeProvider(Start);
            Counters = new ServiceCounters(Time);
            State = new RealmState(NewSnapshot(Start, ConnectionState.Connected));
            Status = new HaConnectionStatus(HaConnectionState.Connected, null, Start, 0, null);
            Builder = new DiagnosticsSnapshotBuilder(State, Options, Counters, Time, databasePath ?? Path.Combine(Path.GetTempPath(), "realm-diagnostics-absent", "realm.db"), () => Status, () => Thresholds, "1.2.3");
        }

        public RealmOptions Options { get; }

        public ManualTimeProvider Time { get; }

        public ServiceCounters Counters { get; }

        public RealmState State { get; }

        /// <summary>What the websocket reports now; null when it does not run.</summary>
        public HaConnectionStatus? Status { get; set; }

        /// <summary>The effective stale thresholds the pipeline reports, by member id.</summary>
        public Dictionary<string, int> Thresholds { get; } = [];

        public DiagnosticsSnapshotBuilder Builder { get; }

        public DiagnosticsSnapshot Get() => Builder.GetSnapshot();

        public void Publish(RealmSnapshot snapshot) => State.Publish(snapshot);

        public JsonDocument Document() => JsonDocument.Parse(JsonSerializer.Serialize(Get(), Json));
    }
}
