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
        var tracks = midiFileConfig.Tracks;

        DalamudApi.PluginLog.Debug(
            $"[TA] BuildMidiConfigFromRules: {tracks.Count} tracks, {members.Count} members, " +
            $"{config.CaptureRules?.Count ?? 0} capture rules, " +
            $"maxPerformers={config.MaxPerformers}, sequential={config.AssignUnmatchedTracksSequentially}");

        var trackSlots = new int[tracks.Count];
        Array.Fill(trackSlots, -1);

        ApplyMemberRules(tracks, members, config, trackSlots);
        ApplyCaptureAndSequentialRules(tracks, members, config, trackSlots);
        ApplyCids(tracks, members, config, trackSlots, Cids);

        DalamudApi.PluginLog.Debug("[TA] BuildMidiConfigFromRules complete.");
        return midiFileConfig;
    }

    // Phase 1: assign tracks to specific members via per-member regex rules.
    private static void ApplyMemberRules(
        List<DbTrack> tracks,
        List<EnsembleMemberConfig> members,
        TrackAssignmentConfig config,
        int[] trackSlots)
    {
        DalamudApi.PluginLog.Debug("[TA] === Phase 1: per-member rules ===");
        for (int ti = 0; ti < tracks.Count; ti++)
            trackSlots[ti] = FindMemberRuleSlot(tracks[ti].Name ?? string.Empty, ti, members, config.MaxPerformers);
    }

    // Returns the index of the first member whose rules match trackName, or -1 if none match.
    private static int FindMemberRuleSlot(string trackName, int ti, List<EnsembleMemberConfig> members, int maxPerformers)
    {
        for (int mi = 0; mi < members.Count && mi < maxPerformers; mi++)
        {
            var member = members[mi];
            if (!member.TrackAssignmentEnabled || member.TrackRules == null) continue;

            foreach (var rule in member.TrackRules)
            {
                if (!rule.Enabled || string.IsNullOrEmpty(rule.Pattern)) continue;

                Regex regex;
                try { regex = new Regex(rule.Pattern, rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None); }
                catch (Exception ex)
                {
                    DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' - member [{mi}] '{member.Name}' rule '{rule.Pattern}' invalid regex: {ex.Message}");
                    continue;
                }

                if (!regex.IsMatch(trackName)) continue;

                var label = string.IsNullOrWhiteSpace(rule.Label) ? rule.Pattern : rule.Label;
                DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' -> member [{mi}] '{member.Name}' via rule '{label}'");
                return mi;
            }
        }

        return -1;
    }

    // Phase 2+3: capture rules and sequential fill share one slot counter so slots
    // are allocated in track order, preventing collisions between the two strategies.
    private static void ApplyCaptureAndSequentialRules(
        List<DbTrack> tracks,
        List<EnsembleMemberConfig> members,
        TrackAssignmentConfig config,
        int[] trackSlots)
    {
        DalamudApi.PluginLog.Debug("[TA] === Phase 2+3: capture rules + sequential fill ===");

        var slotByKey = new Dictionary<(int ruleIdx, string key), int>();
        int nextSlot = 0;

        for (int ti = 0; ti < tracks.Count; ti++)
        {
            if (trackSlots[ti] >= 0) continue;

            var trackName = tracks[ti].Name ?? string.Empty;

            if (TryMatchCaptureRule(trackName, ti, config.CaptureRules, config.MaxPerformers, slotByKey, ref nextSlot, out int captureSlot))
            {
                // captureSlot is -1 when a rule matched but MaxPerformers was already reached.
                // Either way, don't fall through to sequential - the track is claimed by the rule.
                if (captureSlot >= 0) trackSlots[ti] = captureSlot;
            }
            else if (config.AssignUnmatchedTracksSequentially)
            {
                if (nextSlot >= config.MaxPerformers || nextSlot >= members.Count)
                {
                    DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' - sequential skipped: limit reached");
                    continue;
                }

                DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' -> sequential slot {nextSlot}");
                trackSlots[ti] = nextSlot++;
            }
            else
            {
                DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' - no capture match, sequential disabled");
            }
        }
    }

    // Returns true if any capture rule matched (even if skipped due to MaxPerformers).
    // slotIdx is the allocated slot, or -1 if matched but MaxPerformers was reached.
    private static bool TryMatchCaptureRule(
        string trackName, int ti,
        List<TrackAssignmentRule>? captureRules,
        int maxPerformers,
        Dictionary<(int, string), int> slotByKey,
        ref int nextSlot,
        out int slotIdx)
    {
        slotIdx = -1;
        if (captureRules == null || captureRules.Count == 0) return false;

        for (int ri = 0; ri < captureRules.Count; ri++)
        {
            var rule = captureRules[ri];
            if (!rule.Enabled || string.IsNullOrEmpty(rule.Pattern)) continue;

            Regex regex;
            try { regex = new Regex(rule.Pattern, rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None); }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' - capture rule [{ri}] '{rule.Pattern}' invalid regex: {ex.Message}");
                continue;
            }

            var m = regex.Match(trackName);
            if (!m.Success) continue;

            var ruleLabel = string.IsNullOrWhiteSpace(rule.Label) ? rule.Pattern : rule.Label;
            slotIdx = AllocateCaptureSlot(trackName, ti, ri, ruleLabel, rule, m, maxPerformers, slotByKey, ref nextSlot);
            return true;
        }

        return false;
    }

    // Allocates or reuses a slot for a matched capture rule. Returns -1 if MaxPerformers reached.
    private static int AllocateCaptureSlot(
        string trackName, int ti, int ri, string ruleLabel,
        TrackAssignmentRule rule, Match m,
        int maxPerformers,
        Dictionary<(int, string), int> slotByKey,
        ref int nextSlot)
    {
        if (rule.Mode == TrackGroupMode.GroupByCapture)
        {
            string captureKey = m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value;
            var dictKey = (ri, captureKey.ToLowerInvariant());

            if (slotByKey.TryGetValue(dictKey, out int existingSlot))
            {
                DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' -> capture rule [{ri}] '{ruleLabel}' key='{captureKey}' -> reusing slot {existingSlot}");
                return existingSlot;
            }

            if (nextSlot >= maxPerformers)
            {
                DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' - capture rule [{ri}] '{ruleLabel}' key='{captureKey}' skipped: MaxPerformers reached");
                return -1;
            }

            int newSlot = nextSlot++;
            slotByKey[dictKey] = newSlot;
            DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' -> capture rule [{ri}] '{ruleLabel}' key='{captureKey}' -> new slot {newSlot}");
            return newSlot;
        }
        else // OneTrackPerPlayer
        {
            if (nextSlot >= maxPerformers)
            {
                DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' - capture rule [{ri}] '{ruleLabel}' (OneEach) skipped: MaxPerformers reached");
                return -1;
            }

            int newSlot = nextSlot++;
            DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' -> capture rule [{ri}] '{ruleLabel}' (OneEach) -> slot {newSlot}");
            return newSlot;
        }
    }

    // CID assignment: maps each track slot to a party member's content ID.
    // When CompactAbsentMembers is enabled, absent members are skipped and slots
    // are remapped sequentially against only the party-present members.
    private static void ApplyCids(
        List<DbTrack> tracks,
        List<EnsembleMemberConfig> members,
        TrackAssignmentConfig config,
        int[] trackSlots,
        long[] cids)
    {
        DalamudApi.PluginLog.Debug("[TA] === CID resolution ===");

        // Build effective member list: all members, or only those present in party.
        var effectiveMembers = config.CompactAbsentMembers
            ? members.Where(m => ResolveMemberCid(m) > 0).ToList()
            : members;

        if (config.CompactAbsentMembers)
            DalamudApi.PluginLog.Debug($"[TA]   CompactAbsentMembers: {members.Count} configured -> {effectiveMembers.Count} in party");

        for (int ti = 0; ti < tracks.Count; ti++)
        {
            int slotIdx = trackSlots[ti];
            var trackName = tracks[ti].Name ?? string.Empty;

            if (slotIdx < 0)
            {
                DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' - no slot assigned, skipped");
                continue;
            }

            if (slotIdx >= effectiveMembers.Count)
            {
                DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' - slot {slotIdx} out of range (effectiveMembers={effectiveMembers.Count}), skipped");
                continue;
            }

            var memberConfig = effectiveMembers[slotIdx];
            long cid = ResolveMemberCid(memberConfig);

            if (cid <= 0)
            {
                DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' -> slot {slotIdx} '{memberConfig.Name}' - not in party, skipped");
                continue;
            }

            DalamudApi.PluginLog.Debug($"[TA]   Track {ti:00} '{trackName}' -> slot {slotIdx} '{memberConfig.Name}'");
            var track = tracks[ti];
            track.Enabled = true;
            if (!track.AssignedCids.Contains(cid)) track.AssignedCids.Insert(0, cid);
            cids[ti] = cid;
        }
    }

    // Returns the active CID for a member: primary if in party, else first linked member in party, else 0.
    private static long ResolveMemberCid(EnsembleMemberConfig member)
    {
        if (DalamudApi.PartyList.Any(p => p.ContentId == member.Cid))
            return member.Cid;

        return member.LinkedEnsembleMembers
            .Select(lm => lm.Cid)
            .FirstOrDefault(cid => DalamudApi.PartyList.Any(p => p.ContentId == cid));
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
