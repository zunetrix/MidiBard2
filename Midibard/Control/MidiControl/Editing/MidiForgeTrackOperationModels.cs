namespace MidiBard.Control.MidiControl.Editing;

public sealed record MidiForgeApplyTrackNameTransposeOptions(
    bool CreateNewTracks = false);

public sealed record MidiForgeApplyTrackNameTransposeResult(
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int CleanedTrackNames,
    int ChangedNotes,
    int SkippedTracks);

public enum MidiForgeTrackNameFillMode
{
    Ffxiv,
    Midi,
}

public sealed record MidiForgeTrackNameResult(
    int SourceTracks,
    int RenamedTracks);

public enum MidiForgeMapInstrumentsMode
{
    EmptyNamesOnly,
    EmptyOrGenericNamesOnly,
    ReplaceSelectedNames,
}

public sealed record MidiForgeMapInstrumentsOptions(
    MidiForgeMapInstrumentsMode Mode = MidiForgeMapInstrumentsMode.EmptyOrGenericNamesOnly,
    bool IncludeDrumTracks = true,
    MidiForgeTrackNameFillMode NameSource = MidiForgeTrackNameFillMode.Ffxiv);

public sealed record MidiForgeMapInstrumentsResult(
    int SourceTracks,
    int RenamedTracks,
    int SkippedTracks);

public sealed record MidiForgeSetTrackProgramOptions(
    int ProgramNumber,
    bool ReplaceAllProgramChanges = true,
    bool RenameTracks = true,
    MidiForgeTrackNameFillMode RenameMode = MidiForgeTrackNameFillMode.Ffxiv);

public sealed record MidiForgeSetTrackProgramResult(
    int SourceTracks,
    int ChangedTracks,
    int AddedProgramChanges,
    int UpdatedProgramChanges,
    int RenamedTracks);
