using System;
using System.Collections.Generic;

namespace MidiBard;

public enum PlayMode
{
    Single,
    SingleRepeat,
    ListOrdered,
    ListRepeat,
    Random
}

public enum GuitarToneMode
{
    Off,
    Standard,
    Simple,
    OverrideByTrack,
    ProgramElectricGuitarMode,
    //OverrideByChannel,
}

public class TrackStatus
{
    public bool Enabled = false;
    public int Tone = 0;
    public int Transpose = 0;
}

//public struct ChannelStatus
//{
//    public ChannelStatus(bool enabled = true, int tone = 0, int transpose = 0)
//    {
//        Enabled = enabled;
//        Tone = tone;
//        Transpose = transpose;
//    }

//    public bool Enabled = true;
//    public int Tone = 0;
//    public int Transpose = 0;
//}

public class EnsembleMember
{
    public long Cid;
    public string Name;
}

public class EnsembleMemberConfig
{
    public long Cid;
    public string Name;
    public string TrackAssignmentRegex;
    public List<EnsembleMember> LinkedEnsembleMembers { get; set; } = new();
    public bool TrackAssignmentEnabled = false;
    public List<TrackAssignmentRule> TrackRules { get; set; } = new();
}

public enum ChatType
{
    Current = 0,
    Say = 1,
    Party = 2,
    Echo = 3,
    Yell = 4
}

public enum AntiStackType
{
    Off = 0,
    KeepFirstNote = 1,
    KeepShortestNote = 2,
    KeepLongestNote = 3,
}

public enum FilterPlayedSongOptions
{
    ShowAll = 0,
    ShowPlayed = 1,
    ShowUnPlayed = 2,
}

public enum CompensationModes
{
    None = 0,
    ByInstrument = 1,
    ByInstrumentNote = 2,
}

public enum ExtractionField
{
    SongName = 0,
    Artist = 1,
    ReleaseYear = 2,
    Rating = 3,
    Comments = 4,
    Tags = 5,
}

/// <summary>
/// Defines how to extract a value from text using a regex pattern.
/// OutputFormat supports regex replacement syntax (e.g. "$1").
/// For Field == Tags, Separator splits the result into multiple tag names.
/// </summary>
public class ExtractionRule
{
    public ExtractionField Field = ExtractionField.SongName;
    public bool Enabled = false;
    public string Label = string.Empty;
    public string RegexPattern = string.Empty;
    public string OutputFormat = "$1";
    public bool IgnoreCase = true;
    public string? Separator = null;
    /// <summary>Regex applied to the captured value for sanitization.</summary>
    public string? SanitizePattern = null;
    /// <summary>Replacement used when SanitizePattern matches. Empty/null removes the match.</summary>
    public string? SanitizeReplacement = null;
}

public class TrackAssignmentRule
{
    public bool Enabled = true;
    public string Label = string.Empty;
    public string Pattern = string.Empty;
    public bool IgnoreCase = true;
}

public class TrackAssignmentConfig
{
    public bool Enabled = false;
    public int MaxPerformers = 8;
    public bool AssignUnmatchedTracksSequentially = true;
}
