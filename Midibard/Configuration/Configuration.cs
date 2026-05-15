using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;

using Dalamud.Configuration;
using Dalamud.Game.Text;
using Dalamud.Plugin;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Extensions.Json;

namespace MidiBard;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public event Action? OnConfigurationChanged;
    private IDalamudPluginInterface PluginInterface { get; set; }
    private Plugin Plugin { get; set; }

    // Per-bard track assignments - must NOT be synced from the leader because each client
    // has different tracks assigned to their character. SyncTrackStatusWithMidiFileConfig
    // is the correct path to update these at song load time.
    [NoSync]
    public TrackStatus[] TrackStatus = Enumerable.Range(0, 100).Select(_ => new TrackStatus()).ToArray();

    public List<EnsembleMemberConfig> EnsembleMemberConfigs = new();
    // Rules-based track assignment for ensemble mode
    public TrackAssignmentConfig TrackAssignment = new();

    // folder / file dialogs
    public List<string> PinnedImportFolders { get; set; } = new();
    public string lastOpenedFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public string defaultPerformerFolder = DalamudApi.PluginInterface.ConfigDirectory.FullName;
    public string defaultPlaylistFolder = DalamudApi.PluginInterface.ConfigDirectory.FullName;
    // backup
    public string DefaultBackupFolder = DalamudApi.PluginInterface.ConfigDirectory.FullName;
    public int MaxBackupCount = 5;
    public bool BackupOnInit = true;

    public bool useLegacyFileDialog = false;
    // individual account Config file
    // individual windows accounts clients need sync config file
    public bool SaveConfigAfterSync = false;
    public bool SyncClients = true;

    // playback config
    public float PlaySpeed = 1f;
    public float SecondsBetweenTracks = 3;
    public int PlayMode = 0;
    public int TransposeGlobal = 0;
    public bool AdaptNotesOOR = true;
    public bool AlignMidi = false;
    public double AlignMidiStartOffset = 0;
    public AntiStackType AntiStackType = AntiStackType.Off;
    public int? SoloedTrack = null;
    public bool lazyNoteRelease = true;
    public bool autoSwitchInstrumentBySongName = true;
    public bool autoTransposeBySongName = true;
    public uint DefaultInstrumentId = 0;
    public bool ForceDefaultInstrument = false;
    public bool bmpTrackNames = true;

    // ensemble
    public bool MonitorOnEnsemble = true;
    public bool ShowAllConfiguredMembersInTrackAssign = false;
    // When true the first performance-broadcast heartbeat packet (sent to all nearby players every ~3s)
    // triggers playback start instead of the game-party NetworkEnsembleStart.
    // Allows syncing groups that span multiple parties or have players outside any party.
    public bool UseHeartbeatSync = false;
    public string HeartbeatSyncListenToCharacterName = string.Empty;
    public bool EnableEnsemblePlayMode = false;
    public bool UnequipInstrumentsOnEnsembleEnd = true;
    public int PreReadyCheckDelayMs = 500;
    public bool UpdateInstrumentBeforeReadyCheck;
    public bool PlayButtonShowEnsembleStart = false;
    public bool UiShowEnsemblePanel = false;
    public bool StopPlayingWhenEnsembleEnds = true;
    // time to wait before start non ensemble clients usually equals metronome delay
    public float HeartbeatStartDelay = 4;
    // metronome start delay
    public float EnsembleIndicatorDelay = 4;
    // when song ends metronome keeps running
    public float EnsembleStopDelay = 3;

    public bool AutoOpenPlayerWhenPerforming = true;
    public bool AutoClosePlayerWhenPerforming = false;
    public string lastUsedMidiDeviceName = "";

    // game settings
    public bool AutoSetOffAFKSwitchingTime = true;
    public bool AutoSetFps = true;
    public bool AutoSetLimitFpsWhenInactive = true;
    public bool AutoSetDisplayObjectLimit = true;

    // stream support
    public bool EnableNowPlayingFileOutput = false;

    public string NowPlayingFilePath = Path.Combine(DalamudApi.PluginInterface.ConfigDirectory.FullName, "midibard-now-playing.txt");

    public GuitarToneMode GuitarToneMode = GuitarToneMode.Off;
    public CompensationModes CompensationMode = CompensationModes.ByInstrumentNote;
    /// <summary>Per-instrument delay compensation overrides (ms). Key = sanitized instrument name. Empty = use computed averages for all instruments.</summary>
    public Dictionary<string, int> InstrumentCompensationOverrides = new();

    // MIDI editor map settings used by forge commands. Commands receive these through
    // EditorCommandServices so command logic stays independent from plugin config.
    public MidiForgeMapSettings MidiForgeMaps = MidiForgeMapDefaults.CreateDefaultSettings();
    //public bool TrimChords = false;
    //public int TrimTo = 1;
    //public bool autoSwitchInstrumentByTrackName = false;
    //public bool autoTransposeByTrackName = false;
    //public bool autoStartNewListening = false;

    // Play on multiple devices
    public bool playOnMultipleDevices = false;
    public bool useChatPlaylistSync = false;
    public bool usingFileSharingServices = true;
    public bool IgnoreDefaultPerformer = false;
    public bool IgnoreJsonConfigFile = false;

    // Lyrics
    public bool playLyrics = true;
    public XivChatType LyricsChatTarget = XivChatType.None;

    // Metadata extraction rules (regex-based)
    public List<ExtractionRule> ExtractionRules = new();

    // Post Song
    public PostSongConfig PostSong = new();

    // Legacy Post Song fields - kept for JSON migration only; do not use directly.
    [Newtonsoft.Json.JsonProperty] internal bool AutoSendSongNameToChat = false;
    [Newtonsoft.Json.JsonProperty] internal XivChatType SongNameChatTarget = XivChatType.None;
    [Newtonsoft.Json.JsonProperty] internal string postSongNameCaptureRegex = "";
    [Newtonsoft.Json.JsonProperty] internal string postSongNameCaptureOutputFormat = "";
    [Newtonsoft.Json.JsonProperty] internal string postSongNameFindRegex = "";
    [Newtonsoft.Json.JsonProperty] internal string postSongNameReplacement = "";

    // Theme
    public Vector4 themeColor = new Vector4(0.65882355f, 0.65882355f, 1f, 1f);
    public Vector4 themeColorDark => themeColor * new Vector4(0.25f, 0.25f, 0.25f, 1);
    public Vector4 themeColorTransparent => themeColor * new Vector4(1, 1, 1, 0.33f);
    public Vector4 playedSongColor = new Vector4(0.0f, 0.9804f, 1.0f, 1.0f);
    public ThemeVariant CurrentTheme = ThemeVariant.Default;

    // search
    public bool enableSearching = false;
    public bool SearchUseRegex;
    public FilterPlayedSongOptions SearchFilterPlayedOption = FilterPlayedSongOptions.ShowAll;

    public bool TempPlaylistMode = false;

    // window behavior
    public bool OpenOnStartup = false;
    public bool OpenOnLogin { get; set; } = false;
    public bool AllowMovement { get; set; } = true;
    public bool AllowResize { get; set; } = true;
    public bool ShowSettingsButton { get; set; } = true;
    public bool AllowCloseWithEscape { get; set; } = false;

    // UI
    public bool UseStandalonePlaylistWindow = false;
    public bool hidePlayerInformationFromUi = false;
    public int playlistSizeY = 10;
    public bool miniPlayer = false;
    public bool LockPlot = false;
    public string UiLanguage = DalamudApi.PluginInterface.UiLanguage ?? "en";
    // show / hide items
    public bool ShowTrackSelection = true;
    public int PlaylistMaxVisibleRows = 15;
    public int TrackSelectionMaxVisibleRows = 8;
    public bool UiShowGuitarToneMode = false;
    public bool UiShowPlaySpeed = false;
    public bool UiShowTransposeGlobal = false;
    public bool UiShowAdaptNotesOOR = false;
    public bool UiShowAutoAlignMidi = false;
    public bool UiShowAdsLinks = true;
    public bool showNowPlayingInfo = true;

    // Column visibility - persisted per window
    public SongsWindowColumnSettings SongsWindowColumns = new();
    public PlaylistWindowColumnSettings PlaylistWindowColumns = new();

    // Song file ID sync
    /// <summary>
    /// When enabled, songs participate in file-ID sync: a SyncId is embedded in the
    /// file name as "[N]" so renamed files can be re-identified during sync.
    /// </summary>
    public bool UseSyncByFileId = false;

    //[JsonIgnore] public bool OverrideGuitarTones => GuitarToneMode == GuitarToneMode.Override;

    public void Migrate()
    {
        MigratePostSong();
        MigrateChatTypes();
        MigrateEnsembleIndicatorDelay();
    }

    private void MigrateEnsembleIndicatorDelay()
    {
        if (EnsembleIndicatorDelay < 0)
        {
            EnsembleIndicatorDelay = Math.Abs(EnsembleIndicatorDelay);
        }
    }

    /// <summary>
    /// Converts legacy <c>ChatType</c> integer values (0–4) stored in older config files
    /// to the correct <see cref="XivChatType"/> equivalents.  Safe to call multiple times.
    /// </summary>
    private void MigrateChatTypes()
    {
        LyricsChatTarget = ConvertLegacyChatType(LyricsChatTarget);
        PostSong.ChatTarget = ConvertLegacyChatType(PostSong.ChatTarget);
    }

    private static XivChatType ConvertLegacyChatType(XivChatType value) => (int)value switch
    {
        1 => XivChatType.Say,
        2 => XivChatType.Party,
        3 => XivChatType.Echo,
        4 => XivChatType.Yell,
        _ => value,
    };

    /// <summary>
    /// Migrate PostSong settings from legacy flat fields to the new <see cref="PostSong"/> object.
    /// </summary>
    public void MigratePostSong()
    {
        // Only migrate when the PostSong object still has defaults (fresh or not yet migrated)
        // and at least one legacy field has a non-default value.
        bool hasLegacyData = AutoSendSongNameToChat
            || SongNameChatTarget != XivChatType.None
            || !string.IsNullOrEmpty(postSongNameCaptureRegex)
            || !string.IsNullOrEmpty(postSongNameCaptureOutputFormat)
            || !string.IsNullOrEmpty(postSongNameFindRegex)
            || !string.IsNullOrEmpty(postSongNameReplacement);

        if (!hasLegacyData)
            return;

        PostSong.Enabled = AutoSendSongNameToChat;
        PostSong.ChatTarget = SongNameChatTarget;
        PostSong.Mode = PostSongMode.FilepathRegex;
        PostSong.CaptureRegex = postSongNameCaptureRegex;
        PostSong.OutputFormat = postSongNameCaptureOutputFormat;
        PostSong.FindRegex = postSongNameFindRegex;
        PostSong.Replacement = postSongNameReplacement;

        // Clear legacy fields so migration doesn't run again after save/load.
        AutoSendSongNameToChat = false;
        SongNameChatTarget = XivChatType.None;
        postSongNameCaptureRegex = "";
        postSongNameCaptureOutputFormat = "";
        postSongNameFindRegex = "";
        postSongNameReplacement = "";
    }

    public void Initialize(Plugin plugin, IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
        Plugin = plugin;
        InitExtractionRules();
        InitCaptureRules();
        MidiForgeMaps ??= MidiForgeMapDefaults.CreateDefaultSettings();
        MidiForgeMapDefaults.Normalize(MidiForgeMaps);

        // reset track status
        ResetTrackStatus();
        // enable first track by default
        TrackStatus[0].Enabled = true;
    }

    [Newtonsoft.Json.JsonIgnore]
    private static readonly Dictionary<ExtractionField, ExtractionRule> DefaultExtractionRules = new()
    {
        [ExtractionField.Artist] = new ExtractionRule
        {
            Field = ExtractionField.Artist,
            Enabled = true,
            Label = "Artist before dash. e.g: Artist - Song Name",
            RegexPattern = @"^(.+?)\s*-\s*",
            OutputFormat = "$1",
            IgnoreCase = true,
        },
        [ExtractionField.SongName] = new ExtractionRule
        {
            Field = ExtractionField.SongName,
            Enabled = true,
            Label = "Song name after dash. e.g: Artist - Song Name",
            RegexPattern = @"^.+?\s*-\s*(.+)$",
            OutputFormat = "$1",
            IgnoreCase = true,
        },
    };

    private void InitExtractionRules()
    {
        ExtractionRules ??= new List<ExtractionRule>();

        foreach (ExtractionField field in Enum.GetValues(typeof(ExtractionField)))
        {
            if (!ExtractionRules.Any(r => r.Field == field))
            {
                var seed = DefaultExtractionRules.TryGetValue(field, out var def)
                    ? def
                    : new ExtractionRule { Field = field };
                ExtractionRules.Add(seed);
            }
        }
    }

    private void InitCaptureRules()
    {
        TrackAssignment.CaptureRules ??= new();

        if (TrackAssignment.CaptureRules.Count == 0)
            TrackAssignment.CaptureRules.AddRange(TrackAssignmentConfig.DefaultCaptureRules());
    }

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
        OnConfigurationChanged?.Invoke();
    }

    private void UpdateFrom(Configuration other)
    {
        var type = typeof(Configuration);

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite)
                continue;

            if (Attribute.IsDefined(prop, typeof(NoSyncAttribute)))
                continue;

            if (Attribute.IsDefined(prop, typeof(Newtonsoft.Json.JsonIgnoreAttribute)))
                continue;

            var oldValue = prop.GetValue(this);
            var newValue = prop.GetValue(other);

#if DEBUG
            if (!AreEqual(oldValue, newValue))
            {
                LogChange(prop.Name, oldValue, newValue);

            }
#endif
            prop.SetValue(this, newValue);
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (Attribute.IsDefined(field, typeof(NoSyncAttribute)))
                continue;

            if (Attribute.IsDefined(field, typeof(Newtonsoft.Json.JsonIgnoreAttribute)))
                continue;

            var oldValue = field.GetValue(this);
            var newValue = field.GetValue(other);

#if DEBUG
            if (!AreEqual(oldValue, newValue))
            {
                LogChange(field.Name, oldValue, newValue);

            }
#endif

            field.SetValue(this, newValue);
        }
    }

    static bool AreEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a == null || b == null)
            return false;

        if (a is IList listA && b is IList listB)
            return listA.Count == listB.Count;

        return a.Equals(b);
    }

    static void LogChange(string name, object? oldVal, object? newVal)
    {
        static string Format(object? v)
        {
            if (v == null) return "null";
            if (v is IList list) return $"List(count={list.Count})";
            return v.ToString() ?? "?";
        }

        DalamudApi.PluginLog.Debug($"[ConfigSync] {name}: {Format(oldVal)} → {Format(newVal)}");
    }

    public void UpdateFromJson(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return;

        var incoming = configurationJson.JsonDeserialize<Configuration>();
        if (incoming == null)
            return;

        UpdateFrom(incoming);
    }

    public void ToggleSearchFilterPlayedOption()
    {
        var totalOptions = Enum.GetValues(typeof(FilterPlayedSongOptions)).Length;
        SearchFilterPlayedOption = (FilterPlayedSongOptions)(((int)SearchFilterPlayedOption + 1) % totalOptions);
    }

    public void AddEnsembleMemberConfig(EnsembleMemberConfig newConfig)
    {
        var existing = EnsembleMemberConfigs.FirstOrDefault(p => p.Cid == newConfig.Cid);
        if (existing == null)
        {
            EnsembleMemberConfigs.Add(newConfig);
        }
    }

    public void LinkEnsembleMember(ulong sourceCid, ulong targetCid)
    {
        var source = EnsembleMemberConfigs.FirstOrDefault(x => x.Cid == sourceCid);
        var target = EnsembleMemberConfigs.FirstOrDefault(x => x.Cid == targetCid);

        if (source == null || target == null || source == target)
            return;

        // Move member
        target.LinkedEnsembleMembers.Add(new EnsembleMember
        {
            Cid = source.Cid,
            Name = source.Name
        });

        EnsembleMemberConfigs.Remove(source);
    }

    public void UnlinkEnsembleMember(ulong parentCid, ulong linkedCid)
    {
        var parent = EnsembleMemberConfigs.FirstOrDefault(x => x.Cid == parentCid);

        if (parent == null)
            return;

        if (parent.LinkedEnsembleMembers == null || parent.LinkedEnsembleMembers.Count == 0)
            return;

        var linked = parent.LinkedEnsembleMembers
            .FirstOrDefault(x => x.Cid == linkedCid);

        if (linked == null)
            return;

        parent.LinkedEnsembleMembers.Remove(linked);

        // Add back to main list only if not already there
        if (!EnsembleMemberConfigs.Any(x => x.Cid == linked.Cid))
        {
            EnsembleMemberConfigs.Add(new EnsembleMemberConfig
            {
                Cid = linked.Cid,
                Name = linked.Name,
                TrackAssignmentRegex = "",
                LinkedEnsembleMembers = new List<EnsembleMember>()
            });
        }
    }

    public void ResetTrackStatus()
    {
        TrackStatus = TrackStatus = Enumerable.Range(0, 100).Select(_ => new TrackStatus()).ToArray();
    }

    // TODO: find better way to set plugin dependency
    public void SetTransposeGlobal(int transpose, Plugin plugin)
    {
        bool isDrumTrackPlaying = false;
        if (plugin.CurrentBardPlayback?.TrackInfos?.Any() == true)
        {
            foreach (var trackInfo in plugin.CurrentBardPlayback?.TrackInfos)
            {
                var insID = trackInfo.InstrumentIdFromTrackName((ushort)plugin.Config.DefaultInstrumentId, plugin.Config.ForceDefaultInstrument);
                if (trackInfo.IsEnabled(plugin.Config.TrackStatus) && insID >= 10 && insID <= 14)
                {
                    isDrumTrackPlaying = true;
                    break;
                }
            }
        }

        if (isDrumTrackPlaying)
        {
            TransposeGlobal = 0;
            return;
        }

        TransposeGlobal = transpose;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NoSyncAttribute : Attribute { }
