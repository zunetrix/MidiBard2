using System;
using System.Text.Json;

namespace MidiBard.RemoteSync;

/// <summary>
/// Conductor/Follower sync protocol: versioned JSON text frames.
/// Binary frames are reserved for MIDI file chunks (added in the file-transfer phase).
///
/// Every message carries an optional <see cref="SyncMessage.Cid"/> (ContentId):
/// on Follower→Conductor messages it identifies the originating client, on targeted
/// Conductor→Follower messages the addressee. This lets one gateway client per
/// machine multiplex its local siblings (reached over local IPC) through a single
/// WebSocket connection. <c>0</c> means "the connected client itself".
/// </summary>
internal static class SyncProtocol
{
    /// <summary>v2: QPC-based <see cref="SyncClock"/> and four-timestamp ping/pong (<see cref="PongMessage.T2"/>).</summary>
    public const int ProtocolVersion = 2;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] Serialize(SyncMessage message)
        => JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), JsonOptions);

    public static string SerializeToString(SyncMessage message)
        => JsonSerializer.Serialize(message, message.GetType(), JsonOptions);

    /// <summary>
    /// Parses a text frame. Returns <c>null</c> for malformed JSON or an unknown
    /// message type (forward compatibility: unknown types are skipped, not fatal).
    /// </summary>
    public static SyncMessage? Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var type = typeElement.GetString();
            return type switch
            {
                HelloMessage.TypeName => document.Deserialize<HelloMessage>(JsonOptions),
                WelcomeMessage.TypeName => document.Deserialize<WelcomeMessage>(JsonOptions),
                PingMessage.TypeName => document.Deserialize<PingMessage>(JsonOptions),
                PongMessage.TypeName => document.Deserialize<PongMessage>(JsonOptions),
                SyncErrorMessage.TypeName => document.Deserialize<SyncErrorMessage>(JsonOptions),
                ProgressMessage.TypeName => document.Deserialize<ProgressMessage>(JsonOptions),
                PlayMessage.TypeName => document.Deserialize<PlayMessage>(JsonOptions),
                PauseMessage.TypeName => document.Deserialize<PauseMessage>(JsonOptions),
                StopMessage.TypeName => document.Deserialize<StopMessage>(JsonOptions),
                SeekMessage.TypeName => document.Deserialize<SeekMessage>(JsonOptions),
                LoadFileMessage.TypeName => document.Deserialize<LoadFileMessage>(JsonOptions),
                SyncFolderReloadMessage.TypeName => document.Deserialize<SyncFolderReloadMessage>(JsonOptions),
                ScheduleStartMessage.TypeName => document.Deserialize<ScheduleStartMessage>(JsonOptions),
                StatusReportMessage.TypeName => document.Deserialize<StatusReportMessage>(JsonOptions),
                MidiConfigMessage.TypeName => document.Deserialize<MidiConfigMessage>(JsonOptions),
                EquipInstrumentMessage.TypeName => document.Deserialize<EquipInstrumentMessage>(JsonOptions),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static SyncMessage? Deserialize(string json)
        => Deserialize(System.Text.Encoding.UTF8.GetBytes(json));
}

internal abstract record SyncMessage
{
    public int V { get; init; } = SyncProtocol.ProtocolVersion;

    public abstract string Type { get; }

    /// <summary>
    /// ContentId for gateway multiplexing: origin on Follower→Conductor messages,
    /// target on directed Conductor→Follower messages. 0 = the connected client itself.
    /// </summary>
    public long Cid { get; init; }
}

/// <summary>First message a follower sends after the connection is established.</summary>
internal sealed record HelloMessage : SyncMessage
{
    public const string TypeName = "hello";
    public override string Type => TypeName;

    public int ProtocolVersion { get; init; } = SyncProtocol.ProtocolVersion;
    public string CharacterName { get; init; } = string.Empty;
    public long ContentId { get; init; }
    public string PluginVersion { get; init; } = string.Empty;
}

/// <summary>Conductor's reply to a valid <see cref="HelloMessage"/>.</summary>
internal sealed record WelcomeMessage : SyncMessage
{
    public const string TypeName = "welcome";
    public override string Type => TypeName;

    public int ProtocolVersion { get; init; } = SyncProtocol.ProtocolVersion;
    public string ConductorName { get; init; } = string.Empty;
    public int ProgressIntervalMs { get; init; }
    public int PingIntervalMs { get; init; }
}

/// <summary>Clock-sync probe. <see cref="T0"/> is the follower's send time (its <see cref="SyncClock"/>).</summary>
internal sealed record PingMessage : SyncMessage
{
    public const string TypeName = "ping";
    public override string Type => TypeName;

    public long T0 { get; init; }
}

/// <summary>
/// Conductor's reply to a <see cref="PingMessage"/>: echoes <see cref="T0"/> and adds
/// its receive time <see cref="T1"/> and send time <see cref="T2"/>, so the time the
/// pong spent queued on the conductor is excluded from RTT and offset.
/// </summary>
internal sealed record PongMessage : SyncMessage
{
    public const string TypeName = "pong";
    public override string Type => TypeName;

    /// <summary>Follower clock when the ping was written to the socket.</summary>
    public long T0 { get; init; }

    /// <summary>Conductor clock when the ping frame was fully received.</summary>
    public long T1 { get; init; }

    /// <summary>Conductor clock when the pong was written to the socket.</summary>
    public long T2 { get; init; }
}

internal sealed record SyncErrorMessage : SyncMessage
{
    public const string TypeName = "error";
    public override string Type => TypeName;

    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>Playback state carried by <see cref="ProgressMessage"/> and <see cref="StatusReportMessage"/>.</summary>
internal static class SyncPlaybackState
{
    public const string Playing = "playing";
    public const string Paused = "paused";
    public const string Stopped = "stopped";
}

/// <summary>
/// Conductor → Followers, periodic while a song is loaded. Followers correct
/// drift against it while both sides are playing. <see cref="SentAt"/> is the
/// conductor's <see cref="SyncClock"/> unless relayed by a gateway, which
/// rewrites it to the local machine clock before re-broadcasting over IPC.
/// </summary>
internal sealed record ProgressMessage : SyncMessage
{
    public const string TypeName = "progress";
    public override string Type => TypeName;

    public string State { get; init; } = SyncPlaybackState.Stopped;
    public long PositionMs { get; init; }
    public long DurationMs { get; init; }
    public float Speed { get; init; } = 1f;
    public string? FileName { get; init; }
    public long SentAt { get; init; }

    /// <summary>
    /// Instrument attack compensation every client must apply (conductor's
    /// <see cref="Configuration.RemoteSyncCompensationMode"/>). Carried on every
    /// progress frame so late joiners and gateway siblings converge within one interval.
    /// </summary>
    public CompensationModes CompensationMode { get; init; } = CompensationModes.None;
}

/// <summary>
/// Conductor → Followers: playback started (or resumed) at <see cref="PositionMs"/>.
/// <see cref="SentAt"/> is the conductor's clock at send time (gateway-rewritten like
/// <see cref="ProgressMessage.SentAt"/>); followers add the elapsed delivery time to
/// the start position so they don't begin a full network latency behind. 0 = unknown.
/// </summary>
internal sealed record PlayMessage : SyncMessage
{
    public const string TypeName = "play";
    public override string Type => TypeName;

    public long PositionMs { get; init; }
    public long SentAt { get; init; }
}

internal sealed record PauseMessage : SyncMessage
{
    public const string TypeName = "pause";
    public override string Type => TypeName;
}

internal sealed record StopMessage : SyncMessage
{
    public const string TypeName = "stop";
    public override string Type => TypeName;
}

internal sealed record SeekMessage : SyncMessage
{
    public const string TypeName = "seek";
    public override string Type => TypeName;

    public long PositionMs { get; init; }
}

/// <summary>
/// Conductor → Followers: a song was loaded. Followers load the same file from their
/// sync folder, falling back to <see cref="FilePath"/> when it exists locally (clients
/// on the conductor's machine, or a shared drive, need no file transfer).
/// </summary>
internal sealed record LoadFileMessage : SyncMessage
{
    public const string TypeName = "load";
    public override string Type => TypeName;

    public string FileName { get; init; } = string.Empty;

    /// <summary>Full path on the conductor's machine; only meaningful where that path resolves.</summary>
    public string? FilePath { get; init; }
}

/// <summary>Conductor → Followers: rebuild the temp playlist from the sync folder contents.</summary>
internal sealed record SyncFolderReloadMessage : SyncMessage
{
    public const string TypeName = "syncFolderReload";
    public override string Type => TypeName;
}

/// <summary>
/// Conductor → Followers: start playback at <see cref="PositionMs"/> when the clock
/// reaches <see cref="StartAtMs"/>. The timestamp is in the conductor's clock unless
/// <see cref="LocalClock"/> is set (a gateway already converted it before relaying).
/// </summary>
internal sealed record ScheduleStartMessage : SyncMessage
{
    public const string TypeName = "scheduleStart";
    public override string Type => TypeName;

    public long StartAtMs { get; init; }
    public long PositionMs { get; init; }
    public bool LocalClock { get; init; }
}

/// <summary>
/// Conductor → Followers: the current song's track/instrument/performer assignment
/// (the same <c>MidiFileConfig</c> payload the local <c>UpdateMidiFileConfig</c> IPC
/// carries). Followers apply it to the loaded playback - waiting for an in-flight
/// load - enabling only the tracks assigned to their own ContentId.
/// </summary>
internal sealed record MidiConfigMessage : SyncMessage
{
    public const string TypeName = "midiConfig";
    public override string Type => TypeName;

    /// <summary>File the config belongs to; followers refuse to apply it to a different song.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>MidiFileConfig serialized with the plugin's Newtonsoft settings (same payload as local IPC).</summary>
    public string ConfigJson { get; init; } = string.Empty;
}

/// <summary>Conductor → Followers: equip (takeout) or put away the instrument of the follower's assigned track.</summary>
internal sealed record EquipInstrumentMessage : SyncMessage
{
    public const string TypeName = "equipInstrument";
    public override string Type => TypeName;

    public bool Takeout { get; init; }
}

/// <summary>Follower → Conductor, periodic: diagnostic state for the conductor's followers table.</summary>
internal sealed record StatusReportMessage : SyncMessage
{
    public const string TypeName = "status";
    public override string Type => TypeName;

    public string State { get; init; } = SyncPlaybackState.Stopped;
    public long PositionMs { get; init; }
    /// <summary>Last measured drift vs the conductor (ms, positive = ahead); null when unknown.</summary>
    public long? DriftMs { get; init; }
    public string? FileName { get; init; }

    /// <summary>
    /// Phase [0, 50) of this follower's solo-packet slot grid on the conductor's clock;
    /// null when no solo send session is active (not playing, or send hook unavailable).
    /// </summary>
    public int? SoloGridPhaseMs { get; init; }

    /// <summary>Send session counter: changes whenever the game opened a new session (new random phase).</summary>
    public int? SoloGridSessionId { get; init; }
}
