using System.Collections.Generic;
using System.Linq;

namespace MidiBard.Managers;

internal class MidiFileConfig
{
    //public string FileName;
    //public string FilePath { get; set; }
    //public int Transpose { get; set; }
    public List<DbTrack> Tracks = new List<DbTrack>();
    //public DbChannel[] Channels = Enumerable.Repeat(new DbChannel(), 16).ToArray();
    //public List<int> TrackToDuplicate = new List<int>();
    public GuitarToneMode ToneMode = GuitarToneMode.Off;
    public bool AdaptNotes = true;
    public float Speed = 1;
    /// <summary>Per-song instrument delay compensation values (ms). Key = sanitized instrument name. When set, overrides the global InstrumentCompensationOverrides and switches mode to ByInstrument.</summary>
    public Dictionary<string, int>? InstrumentCompensation = null;

    internal static bool IsCidOnTrack(ulong cid, DbTrack track, List<EnsembleMemberConfig> ensembleMemberConfigs)
    {
        // main cid
        if (track.AssignedCids.Contains(cid))
            return true;

        // linked
        return ensembleMemberConfigs
            .Where(cfg => track.AssignedCids.Contains(cfg.Cid))
            .Any(cfg => cfg.LinkedEnsembleMembers.Any(link => link.Cid == cid));

        // return false;
    }

    internal static ulong GetFirstCidInParty(DbTrack track, List<EnsembleMemberConfig> ensembleMemberConfigs)
    {
        // main CIDs
        var mainCid = track.AssignedCids
            .FirstOrDefault(cid => DalamudApi.PartyList.Any(p => p.ContentId == cid));

        if (mainCid != 0)
        {
            // api.DalamudApi.PluginLog.Warning($"GetFirstCidInParty main ({mainCid}): {track.Name}");
            return mainCid;
        }

        // linked CIDs
        var linkedCid = ensembleMemberConfigs
            .Where(cfg => track.AssignedCids.Contains(cfg.Cid))
            .SelectMany(cfg => cfg.LinkedEnsembleMembers)
            .Select(link => link.Cid)
            .FirstOrDefault(cid => DalamudApi.PartyList.Any(p => p.ContentId == cid));

        if (linkedCid != 0)
        {
            // api.DalamudApi.PluginLog.Warning($"GetFirstCidInParty linked ({linkedCid}): {track.Name}");
            return linkedCid;
        }

        return 0;
    }
}
