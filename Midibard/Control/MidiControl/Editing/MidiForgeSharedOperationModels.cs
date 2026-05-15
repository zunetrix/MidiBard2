namespace MidiBard.Control.MidiControl.Editing;

public enum MidiForgeRangeFitStrategy
{
    FitNotesIndividually,
    LowerHighNotesFirst,
    BestOctaveFit,
}

public enum MidiForgeChordSplitStrategy
{
    SameStartTick,
    SameStartTickAndLength,
}

public enum MidiForgeChordGroupMode
{
    GroupMerged,
    Individual,
    Group,
}

public enum MidiForgeChordPickStrategy
{
    HighestChords,
    OddChords,
}
