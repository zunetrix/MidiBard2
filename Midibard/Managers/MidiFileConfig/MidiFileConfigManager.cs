using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using Dalamud.Interface.ImGuiNotification;

using Newtonsoft.Json;

namespace MidiBard.Managers;

internal class MidiFileConfigManager
{
    private readonly Plugin Plugin;
    public bool UsingDefaultPerformer = false;
    public DefaultPerformer defaultPerformer { get; set; }
    public string DefaultPerformerFileName = "MidiBardDefaultPerformer.json";
    private readonly JsonSerializerSettings JsonSerializerSettings = new()
    {
        //TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        //TypeNameHandling = TypeNameHandling.Objects
    };

    public MidiFileConfigManager(Plugin plugin)
    {
        Plugin = plugin;
        LoadDefaultPerformer();
    }

    public void Save(MidiFileConfig config, string path)
    {
        UsingDefaultPerformer = false;
        var fullName = GetMidiConfigFileInfo(path).FullName;

        // remove -1 element in AssignedCids added by GetFirstCidInParty before save to file
        foreach (var track in config.Tracks)
        {
            track.AssignedCids = track.AssignedCids.Where(cid => cid > 0).ToList();
        }

        File.WriteAllText(fullName, JsonConvert.SerializeObject(config, Formatting.Indented, JsonSerializerSettings));
    }

    public FileInfo GetMidiConfigFileInfo(string songPath)
    {
        var configFileInfo = new FileInfo(Path.ChangeExtension(songPath, ".json"));
        return configFileInfo;
    }

    public MidiFileConfig? GetMidiConfigFromFile(string songPath)
    {
        // BML stream midi
        if (songPath == null)
            return null;

        var configFile = GetMidiConfigFileInfo(songPath);
        MidiFileConfig config = null;
        if (!configFile.Exists) return null;
        string fileContent = "";
        try
        {
            using (FileStream fs = File.Open(configFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                StreamReader sr = new StreamReader(fs);
                fileContent = sr.ReadToEnd();
                config = JsonConvert.DeserializeObject<MidiFileConfig>(fileContent, JsonSerializerSettings);
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e.ToString());
        }
        return config;
    }

    public MidiFileConfig GetMidiConfigFromTrack(IEnumerable<TrackInfo> trackInfos)
    {
        return new MidiFileConfig()
        {
            Tracks = trackInfos.Select(i => new DbTrack
            {
                Index = i.Index,
                Name = i.TrackName,
                Instrument = i.InstrumentIdFromTrackName((ushort)Plugin.Config.DefaultInstrumentId) ?? 0,
                Transpose = i.TransposeFromTrackName,
            }).ToList(),
            AdaptNotes = Plugin.Config.AdaptNotesOOR,
            ToneMode = Plugin.Config.GuitarToneMode,
            Speed = 1,
        };
    }

    public void SetDefaultPerformerFolder(string path)
    {
        Plugin.Config.defaultPerformerFolder = path;
        LoadDefaultPerformer();
    }

    public void ResetDefaultPerformer()
    {
        DalamudApi.PluginLog.Debug("Reseting Default Performer...");
        defaultPerformer = new DefaultPerformer();

        var folder = EnsureValidFolder(ref Plugin.Config.defaultPerformerFolder);
        var defaultPerformerFilePath = Path.Combine(folder, DefaultPerformerFileName);

        if (!File.Exists(defaultPerformerFilePath))
        {
            defaultPerformerFilePath = FallbackToDefaultFolder();
        }

        var defaultPerformerFileInfo = new FileInfo(defaultPerformerFilePath);
        var serializedContents = JsonConvert.SerializeObject(defaultPerformer, Formatting.Indented);
        File.WriteAllText(defaultPerformerFileInfo.FullName, serializedContents);
        DalamudApi.PluginLog.Debug($"{defaultPerformerFileInfo.FullName} Saved");
        ImGuiUtil.AddNotification(NotificationType.Info, $"Default performer reseted");
    }

    public DefaultPerformer LoadDefaultPerformer()
    {
        DalamudApi.PluginLog.Debug("Loading Default Performer...");

        var folder = EnsureValidFolder(ref Plugin.Config.defaultPerformerFolder);
        var path = Path.Combine(folder, DefaultPerformerFileName);

        if (!File.Exists(path))
        {
            DalamudApi.PluginLog.Warning($"Default Performer not found at {path}, creating...");
            if (!SaveDefaultPerformer())
            {
                path = FallbackToDefaultFolder();
            }
        }

        try
        {
            var json = File.ReadAllText(path);
            defaultPerformer = JsonConvert.DeserializeObject<DefaultPerformer>(json, JsonSerializerSettings);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Failed to load Default Performer: {e}");
        }

        return defaultPerformer;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    public MidiFileConfig LoadDefaultPerformer(MidiFileConfig midiFileConfig, ref long[] Cids)
    {
        DalamudApi.PluginLog.Debug("Loading Default Performer from MidiFileConfig...");
        UsingDefaultPerformer = true;
        var trackMapping = defaultPerformer?.TrackMappingDict ?? new();
        Cids = new long[100];

        var partyMembers = DalamudApi.PartyList.ToList();

        foreach (var member in partyMembers)
        {
            if (member?.ContentId > 0 && trackMapping.TryGetValue(member.ContentId, out var trackIndices))
            {
                foreach (var trackIdx in trackIndices)
                    Cids[trackIdx] = member.ContentId;
            }
        }

        for (int i = 0; i < midiFileConfig.Tracks.Count; i++)
        {
            try
            {
                var track = midiFileConfig.Tracks[i];
                if (MidiFileConfig.GetFirstCidInParty(track, Plugin.Config.EnsembleMemberConfigs) <= 0 && Cids[i] > 0)
                {
                    if (!track.AssignedCids.Contains(Cids[i]))
                        track.AssignedCids.Insert(0, Cids[i]);
                }
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Warning($"Track {i}: {e.Message}");
            }
        }

        return midiFileConfig;
    }

    public MidiFileConfig BuildMidiConfigFromRules(MidiFileConfig midiFileConfig, ref long[] Cids)
    {
        UsingDefaultPerformer = false;
        Cids = new long[100];

        var config = Plugin.Config.TrackAssignment;
        var members = Plugin.Config.EnsembleMemberConfigs;
        var partyMembers = DalamudApi.PartyList.ToList();

        var trackSlots = Enumerable.Repeat(-1, midiFileConfig.Tracks.Count).ToArray();

        for (int ti = 0; ti < midiFileConfig.Tracks.Count; ti++)
        {
            var trackName = midiFileConfig.Tracks[ti].Name ?? string.Empty;

            for (int mi = 0; mi < members.Count && mi < config.MaxPerformers; mi++)
            {
                var member = members[mi];
                if (!member.TrackAssignmentEnabled || member.TrackRules == null) continue;

                foreach (var rule in member.TrackRules)
                {
                    if (!rule.Enabled || string.IsNullOrEmpty(rule.Pattern)) continue;

                    Regex regex;
                    try { regex = new Regex(rule.Pattern, rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None); }
                    catch { continue; }

                    if (regex.IsMatch(trackName))
                    {
                        trackSlots[ti] = mi;
                        goto nextTrack;
                    }
                }
            }

            nextTrack:;
        }

        // Phase 2: global capture rules — dynamic slot allocation by capture group
        var captureRules = config.CaptureRules;
        if (captureRules?.Count > 0)
        {
            var slotByKey = new Dictionary<(int ruleIdx, string key), int>();
            int nextCaptureSlot = 0;

            for (int ti = 0; ti < midiFileConfig.Tracks.Count; ti++)
            {
                if (trackSlots[ti] >= 0) continue;

                var trackName = midiFileConfig.Tracks[ti].Name ?? string.Empty;

                for (int ri = 0; ri < captureRules.Count; ri++)
                {
                    var rule = captureRules[ri];
                    if (!rule.Enabled || string.IsNullOrEmpty(rule.Pattern)) continue;

                    Regex regex;
                    try { regex = new Regex(rule.Pattern, rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None); }
                    catch { continue; }

                    var m = regex.Match(trackName);
                    if (!m.Success) continue;

                    int slotIdx;
                    if (rule.Mode == TrackGroupMode.GroupByCapture)
                    {
                        string captureKey = m.Groups.Count > 1 && m.Groups[1].Success
                            ? m.Groups[1].Value
                            : m.Value;

                        var dictKey = (ri, captureKey.ToLowerInvariant());
                        if (!slotByKey.TryGetValue(dictKey, out slotIdx))
                        {
                            if (nextCaptureSlot >= config.MaxPerformers) break;
                            slotIdx = nextCaptureSlot++;
                            slotByKey[dictKey] = slotIdx;
                        }
                    }
                    else
                    {
                        if (nextCaptureSlot >= config.MaxPerformers) break;
                        slotIdx = nextCaptureSlot++;
                    }

                    trackSlots[ti] = slotIdx;
                    break;
                }
            }
        }

        if (config.AssignUnmatchedTracksSequentially)
        {
            int seqSlot = 0;
            for (int ti = 0; ti < midiFileConfig.Tracks.Count; ti++)
            {
                if (trackSlots[ti] >= 0) continue;
                if (seqSlot >= config.MaxPerformers || seqSlot >= members.Count) break;
                trackSlots[ti] = seqSlot++;
            }
        }

        for (int ti = 0; ti < midiFileConfig.Tracks.Count; ti++)
        {
            int slotIdx = trackSlots[ti];
            if (slotIdx < 0 || slotIdx >= members.Count) continue;

            var memberConfig = members[slotIdx];
            long cid = 0;

            if (partyMembers.Any(p => p.ContentId == memberConfig.Cid))
                cid = memberConfig.Cid;
            else
                cid = memberConfig.LinkedEnsembleMembers
                    .Select(lm => lm.Cid)
                    .FirstOrDefault(c => partyMembers.Any(p => p.ContentId == c));

            if (cid <= 0) continue;

            var track = midiFileConfig.Tracks[ti];
            track.Enabled = true;
            if (!track.AssignedCids.Contains(cid))
                track.AssignedCids.Insert(0, cid);
            Cids[ti] = cid;
        }

        return midiFileConfig;
    }

    private string EnsureValidFolder(ref string folder)
    {
        if (Directory.Exists(folder)) return folder;

        DalamudApi.PluginLog.Warning($"Default Performer folder does not exist. Creating at: {folder}");
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Invalid folder path: {folder}, using fallback. {e.Message}");
            ImGuiUtil.AddNotification(NotificationType.Error, $"Invalid Default Performer folder. Using default instead.");
            folder = DalamudApi.PluginInterface.ConfigDirectory.FullName;
        }

        return folder;
    }

    private string FallbackToDefaultFolder()
    {
        var fallbackFolder = DalamudApi.PluginInterface.ConfigDirectory.FullName;
        var fallbackPath = Path.Combine(fallbackFolder, DefaultPerformerFileName);

        ImGuiUtil.AddNotification(NotificationType.Error, "Failed to save Default Performer, using fallback folder.");
        SaveDefaultPerformer();

        return fallbackPath;
    }

    public bool SaveDefaultPerformer()
    {
        if (defaultPerformer == null)
        {
            defaultPerformer = new DefaultPerformer();
        }

        try
        {
            var trackMappingFileInfo = GetDefaultPerformerFileInfo();
            var directory = trackMappingFileInfo.Directory;
            if (directory != null && !directory.Exists)
            {
                DalamudApi.PluginLog.Warning($"Directory {directory.FullName} does not exist. Creating...");
                Directory.CreateDirectory(directory.FullName);
            }

            if (trackMappingFileInfo != null)
            {
                var serializedContents = JsonConvert.SerializeObject(defaultPerformer, Formatting.Indented);
                File.WriteAllText(trackMappingFileInfo.FullName, serializedContents);
                DalamudApi.PluginLog.Warning($"{trackMappingFileInfo.FullName} Saved");
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Failed to save Default Performer: {e}");
            return false;
        }

        return true;
    }

    FileInfo GetDefaultPerformerFileInfo()
    {
        if (string.IsNullOrEmpty(Plugin.Config.defaultPerformerFolder))
        {
            DalamudApi.PluginLog.Warning("Default Performer folder is not set. Using fallback folder.");
            Plugin.Config.defaultPerformerFolder = DalamudApi.PluginInterface.ConfigDirectory.FullName;
        }

        return new FileInfo(Path.Combine(Plugin.Config.defaultPerformerFolder, DefaultPerformerFileName));
    }

    public void ExportToDefaultPerformer()
    {
        if (Plugin.CurrentBardPlayback?.MidiFileConfig == null)
        {
            ImGuiUtil.AddNotification(NotificationType.Error, "Please choose a song first!");
            return;
        }

        var midiFileConfig = Plugin.CurrentBardPlayback?.MidiFileConfig;
        Dictionary<long, List<int>> trackDict = new Dictionary<long, List<int>>();
        List<long> existingCidInConfig = new List<long>();
        foreach (var cur in midiFileConfig.Tracks)
        {
            foreach (var curCid in cur.AssignedCids)
            {
                if (!trackDict.TryGetValue(curCid, out List<int> value))
                {
                    value = new List<int>();
                    trackDict.Add(curCid, value);
                }

                value.Add(cur.Index);

                if (!existingCidInConfig.Contains(curCid))
                {
                    existingCidInConfig.Add(curCid);
                }
            }
        }

        foreach (var pair in trackDict)
        {
            if (!defaultPerformer.TrackMappingDict.ContainsKey(pair.Key))
            {
                defaultPerformer.TrackMappingDict.Add(pair.Key, pair.Value);
            }
            else
            {
                defaultPerformer.TrackMappingDict[pair.Key] = pair.Value;
            }
        }

        // scan for those in the party but not in config anymore, remove them from Default Performer
        var partyList = DalamudApi.PartyList.ToArray();
        List<long> toRemove = new List<long>();
        foreach (var cur in partyList)
        {
            if (!existingCidInConfig.Contains(cur.ContentId))
            {
                toRemove.Add(cur.ContentId);
            }
        }

        foreach (var cur in toRemove)
        {
            defaultPerformer.TrackMappingDict.Remove(cur);
        }

        bool succeed = SaveDefaultPerformer();
        if (succeed)
        {
            UsingDefaultPerformer = true;
            ImGuiUtil.AddNotification(NotificationType.Success, "Default Performer Exported.");
            GetMidiConfigFileInfo(Plugin.CurrentBardPlayback.FilePath).Delete();
            if (!Plugin.Config.playOnMultipleDevices)
            {
                Plugin.IpcProvider.UpdateDefaultPerformer();
            }
        }
        else
        {
            ImGuiUtil.AddNotification(NotificationType.Error, "Fail to Export Default Performer!");
        }
    }
}
