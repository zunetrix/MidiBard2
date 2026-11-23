using System.Text;

using MidiBard.RemoteSync;

namespace MidiBard.Tests.RemoteSync;

public class SyncProtocolTests
{
    [Fact]
    public void HelloRoundTripsThroughJson()
    {
        var original = new HelloMessage
        {
            CharacterName = "Alma Cahrd",
            ContentId = 123456789L,
            PluginVersion = "1.15.0.170",
        };

        var parsed = SyncProtocol.Deserialize(SyncProtocol.Serialize(original));

        var hello = parsed.ShouldBeOfType<HelloMessage>();
        hello.V.ShouldBe(SyncProtocol.ProtocolVersion);
        hello.ProtocolVersion.ShouldBe(SyncProtocol.ProtocolVersion);
        hello.CharacterName.ShouldBe("Alma Cahrd");
        hello.ContentId.ShouldBe(123456789L);
        hello.PluginVersion.ShouldBe("1.15.0.170");
    }

    [Fact]
    public void PingPongRoundTripsWithGatewayCid()
    {
        var pong = new PongMessage { Cid = 42, T0 = 1000, T1 = 2000, T2 = 2005 };

        var parsed = SyncProtocol.Deserialize(SyncProtocol.Serialize(pong));

        var result = parsed.ShouldBeOfType<PongMessage>();
        result.Cid.ShouldBe(42);
        result.T0.ShouldBe(1000);
        result.T1.ShouldBe(2000);
        result.T2.ShouldBe(2005);
    }

    [Fact]
    public void ProgressCarriesCompensationMode()
    {
        var progress = new ProgressMessage { CompensationMode = CompensationModes.ByInstrumentNote };

        var result = SyncProtocol.Deserialize(SyncProtocol.Serialize(progress)).ShouldBeOfType<ProgressMessage>();

        result.CompensationMode.ShouldBe(CompensationModes.ByInstrumentNote);
    }

    [Fact]
    public void ProgressWithoutCompensationModeDefaultsToNone()
    {
        var result = SyncProtocol.Deserialize("""{"type":"progress","state":"playing"}""").ShouldBeOfType<ProgressMessage>();

        result.CompensationMode.ShouldBe(CompensationModes.None);
    }

    [Fact]
    public void WireFormatUsesCamelCaseAndTypeDiscriminator()
    {
        var json = SyncProtocol.SerializeToString(new WelcomeMessage
        {
            ConductorName = "Alma",
            ProgressIntervalMs = 1000,
            PingIntervalMs = 10000,
        });

        json.ShouldContain("\"type\":\"welcome\"");
        json.ShouldContain("\"conductorName\":\"Alma\"");
        json.ShouldContain($"\"v\":{SyncProtocol.ProtocolVersion}");
    }

    [Fact]
    public void UnknownTypeDeserializesToNullForForwardCompatibility()
    {
        SyncProtocol.Deserialize("""{"v":1,"type":"future-message","something":123}""")
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"v\":1}")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    public void MalformedInputDeserializesToNull(string input)
    {
        SyncProtocol.Deserialize(Encoding.UTF8.GetBytes(input)).ShouldBeNull();
    }

    [Fact]
    public void ProgressRoundTripsAllFields()
    {
        var original = new ProgressMessage
        {
            State = SyncPlaybackState.Playing,
            PositionMs = 12_345,
            DurationMs = 180_000,
            Speed = 1.25f,
            FileName = "song.mid",
            SentAt = 987_654_321,
        };

        var parsed = SyncProtocol.Deserialize(SyncProtocol.Serialize(original))
            .ShouldBeOfType<ProgressMessage>();

        parsed.ShouldBe(original);
    }

    [Fact]
    public void LifecycleMessagesRoundTrip()
    {
        var play = SyncProtocol.Deserialize(SyncProtocol.Serialize(new PlayMessage { PositionMs = 500, SentAt = 123_456 }))
            .ShouldBeOfType<PlayMessage>();
        play.PositionMs.ShouldBe(500);
        play.SentAt.ShouldBe(123_456);
        SyncProtocol.Deserialize(SyncProtocol.Serialize(new PauseMessage()))
            .ShouldBeOfType<PauseMessage>();
        SyncProtocol.Deserialize(SyncProtocol.Serialize(new StopMessage()))
            .ShouldBeOfType<StopMessage>();
        SyncProtocol.Deserialize(SyncProtocol.Serialize(new SeekMessage { PositionMs = 42 }))
            .ShouldBeOfType<SeekMessage>().PositionMs.ShouldBe(42);
        var load = SyncProtocol.Deserialize(SyncProtocol.Serialize(
                new LoadFileMessage { FileName = "a.mid", FilePath = @"C:\songs\a.mid" }))
            .ShouldBeOfType<LoadFileMessage>();
        load.FileName.ShouldBe("a.mid");
        load.FilePath.ShouldBe(@"C:\songs\a.mid");
        SyncProtocol.Deserialize(SyncProtocol.Serialize(new LoadFileMessage { FileName = "b.mid" }))
            .ShouldBeOfType<LoadFileMessage>().FilePath.ShouldBeNull();
        SyncProtocol.Deserialize(SyncProtocol.Serialize(new SyncFolderReloadMessage()))
            .ShouldBeOfType<SyncFolderReloadMessage>();
    }

    [Fact]
    public void ScheduleStartRoundTripsClockFlag()
    {
        var original = new ScheduleStartMessage
        {
            StartAtMs = 1_000_000,
            PositionMs = 2_500,
            LocalClock = true,
        };

        var parsed = SyncProtocol.Deserialize(SyncProtocol.Serialize(original))
            .ShouldBeOfType<ScheduleStartMessage>();

        parsed.ShouldBe(original);
    }

    [Fact]
    public void MidiConfigRoundTripsConfigPayload()
    {
        var original = new MidiConfigMessage
        {
            FileName = "song.mid",
            ConfigJson = """{"Tracks":[{"Index":0,"Enabled":true,"AssignedCids":[123456789]}]}""",
        };

        var parsed = SyncProtocol.Deserialize(SyncProtocol.Serialize(original))
            .ShouldBeOfType<MidiConfigMessage>();

        parsed.ShouldBe(original);
    }

    [Fact]
    public void EquipInstrumentRoundTripsTakeoutFlag()
    {
        SyncProtocol.Deserialize(SyncProtocol.Serialize(new EquipInstrumentMessage { Takeout = true }))
            .ShouldBeOfType<EquipInstrumentMessage>().Takeout.ShouldBeTrue();
        SyncProtocol.Deserialize(SyncProtocol.Serialize(new EquipInstrumentMessage { Takeout = false }))
            .ShouldBeOfType<EquipInstrumentMessage>().Takeout.ShouldBeFalse();
    }

    [Fact]
    public void StatusReportRoundTripsNullableDrift()
    {
        var withDrift = new StatusReportMessage
        {
            State = SyncPlaybackState.Paused,
            PositionMs = 777,
            DriftMs = -12,
            FileName = "b.mid",
        };
        SyncProtocol.Deserialize(SyncProtocol.Serialize(withDrift))
            .ShouldBeOfType<StatusReportMessage>().ShouldBe(withDrift);

        var withoutDrift = new StatusReportMessage { State = SyncPlaybackState.Stopped };
        SyncProtocol.Deserialize(SyncProtocol.Serialize(withoutDrift))
            .ShouldBeOfType<StatusReportMessage>().DriftMs.ShouldBeNull();
    }
}
