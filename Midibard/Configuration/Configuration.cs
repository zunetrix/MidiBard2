using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

using Dalamud.Configuration;
using Dalamud.Plugin;

using MidiBard.Extensions.Json;
using MidiBard.Managers;

namespace MidiBard;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public event Action? OnConfigurationChanged;
    private IDalamudPluginInterface PluginInterface { get; set; }

    // Using property with ShouldSerialize to control serialization
    // public bool ShouldSerializeTrackStatus() => false;
    // [Newtonsoft.Json.JsonIgnore]
    public TrackStatus[] TrackStatus = Enumerable.Repeat(new TrackStatus(), 100).ToArray();

    // public ChannelStatus[] ChannelStatus = Enumerable.Repeat(new ChannelStatus(), 16).ToArray();
    public List<EnsembleMemberConfig> EnsembleMemberConfigs = new();

    public List<string> RecentUsedPlaylists { get; set; } = new List<string>();

    // folder / file dialogs
    public List<string> PinnedImportFolders { get; set; } = new List<string>();
    public string lastOpenedFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public string defaultPerformerFolder = DalamudApi.PluginInterface.ConfigDirectory.FullName;
    public string defaultPlaylistFolder = DalamudApi.PluginInterface.ConfigDirectory.FullName;
    public bool useLegacyFileDialog = false;
    // individual account Config file
    // individual windows accounts clients need sync config file
    public bool SaveConfigAfterSync = false;

    // playback config
    public float PlaySpeed = 1f;
    public float SecondsBetweenTracks = 3;
    public int PlayMode = 0;
    public int TransposeGlobal = 0;
    public bool AdaptNotesOOR = true;
    public bool AlignMidi = false;
    public double AlignMidiStartOffset = 0;
    public AntiStackType AntiStackType = AntiStackType.Off;
    public bool LowLatencyMode => false;
    public bool MonitorOnEnsemble = true;
    public bool AutoOpenPlayerWhenPerforming = true;
    public bool AutoClosePlayerWhenPerforming = false;
    public int? SoloedTrack = null;
    public bool lazyNoteRelease = true;
    public string lastUsedMidiDeviceName = "";
    public bool autoRestoreListening = false;
    public bool autoSwitchInstrumentBySongName = true;
    public bool autoTransposeBySongName = true;
    public uint DefaultInstrumentId = 0;
    public bool bmpTrackNames = true;
    public bool StopPlayingWhenEnsembleEnds = true;
    public bool SyncClients = true;
    public bool AutoSetOffAFKSwitchingTime = true;
    public float EnsembleIndicatorDelay = -4;
    public bool UseEnsembleIndicator = false;
    public bool UpdateInstrumentBeforeReadyCheck;
    public GuitarToneMode GuitarToneMode = GuitarToneMode.Off;
    public CompensationModes CompensationMode = CompensationModes.ByInstrumentNote;
    public int[] ManualInstrumentCompensation = EnsembleManager.GetCompensationAver();
    //public bool TrimChords = false;
    //public int TrimTo = 1;
    //public bool autoSwitchInstrumentByTrackName = false;
    //public bool autoTransposeByTrackName = false;
    //public bool autoStartNewListening = false;

    // Play on multiple devices
    public bool playOnMultipleDevices = false;
    public bool useChatPlaylistSync = false;
    public bool usingFileSharingServices = true;
    public bool lockTracks = false;

    // Lyrics
    public bool playLyrics = true;
    public ChatType LyricsChatTarget = ChatType.Current;

    // Post Song
    public bool autoPostSongName = false;
    public ChatType SongNameChatTarget = ChatType.Current;
    public string postSongNameCaptureRegex = "";
    public string postSongNameCaptureOutputFormat = "";
    public string postSongNameFindRegex = "";
    public string postSongNameReplacement = "";

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
    public bool UiShowGuitarToneMode = false;
    public bool UiShowPlaySpeed = false;
    public bool UiShowTransposeGlobal = false;
    public bool UiShowAdaptNotesOOR = false;
    public bool UiShowAutoAlignMidi = false;
    public bool showNowPlayingInfo = true;

    //[JsonIgnore] public bool OverrideGuitarTones => GuitarToneMode == GuitarToneMode.Override;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;

        // reset track status
        foreach (var curentTrack in TrackStatus)
        {
            curentTrack.Enabled = false;
        }
        // enable first track bye default
        TrackStatus[0].Enabled = true;
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

    public void LinkEnsembleMember(long sourceCid, long targetCid)
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

    public void UnlinkEnsembleMember(long parentCid, long linkedCid)
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
        TrackStatus = Enumerable.Repeat(new TrackStatus(), 100).ToArray().JsonSerialize().JsonDeserialize<TrackStatus[]>();
    }

    public string GetChatCommand(ChatType chatType)
    {
        return chatType switch
        {
            ChatType.Current => string.Empty,
            ChatType.Say => "/s ",
            ChatType.Party => "/p ",
            ChatType.Echo => "/echo ",
            _ => string.Empty
        };
    }

    // TODO: find better way to set plugin dependency
    public void SetTransposeGlobal(int transpose, Plugin plugin)
    {
        bool isDrumTrackPlaying = false;
        if (plugin.CurrentBardPlayback?.TrackInfos?.Length > 0)
        {
            foreach (var trackInfo in plugin.CurrentBardPlayback?.TrackInfos)
            {
                var insID = trackInfo.InstrumentIDFromTrackName;
                if (trackInfo.IsEnabled && insID >= 10 && insID <= 14)
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
