namespace MidiBard.Control.MidiControl.Editing;

public enum MidiForgeRangeFitStrategy
{
    FitNotesIndividually,
    LowerHighNotesFirst,
    BestOctaveFit,
    PhraseAwareOctaveFit,
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

public enum MidiForgeChordTimingToleranceMode
{
    Exact,
    OneOver128Note,
    OneOver64Note,
    CustomTicks,
}

public enum MidiForgeSimultaneousLimitMode
{
    SameStartChordsOnly,
    ActiveOverlaps,
}

public enum MidiForgeNoteKeepPolicy
{
    Highest,
    Lowest,
    Middle,
}

public enum MidiForgeStrumDirection
{
    LowToHigh,
    HighToLow,
    Alternate,
}
