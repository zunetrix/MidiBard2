using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard.Tests.Control.MidiControl.Editing;

public class MidiForgeOperationsTests
{
    [Fact]
    public void AdaptTracksToPlayableRange_CreateNewTrack_KeepsOriginalAndWrapsOutOfRangeNotes()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(36, 0, 120), Note(60, 120, 120), Note(96, 240, 120)));

        var result = MidiForgeOperations.AdaptTracksToPlayableRange(
            file,
            new[] { 0 },
            new MidiForgeAdaptToRangeOptions(CreateNewTracks: true, SmartTranspose: false));

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(1);
        result.ReplacedTracks.ShouldBe(0);
        result.ChangedNotes.ShouldBe(2);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 36, 60, 96 });
        file.Tracks[1].Name.ShouldBe("Piano (Adapted 2 notes)");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 48, 60, 84 });
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void AdaptTracksToPlayableRange_SmartTranspose_AppliesBestOctaveBeforeWrapping()
    {
        var file = CreateEditableFile(CreateTrack("Low", Note(36, 0, 120), Note(40, 120, 120), Note(60, 240, 120)));

        var result = MidiForgeOperations.AdaptTracksToPlayableRange(
            file,
            new[] { 0 },
            new MidiForgeAdaptToRangeOptions(CreateNewTracks: true, SmartTranspose: true));

        result.OctaveShiftedTracks.ShouldBe(1);
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 48, 52, 72 });
    }

    [Fact]
    public void AdaptTracksToPlayableRange_ReplaceOriginal_ReplacesTrackInPlace()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(96, 0, 120)));

        var result = MidiForgeOperations.AdaptTracksToPlayableRange(
            file,
            new[] { 0 },
            new MidiForgeAdaptToRangeOptions(CreateNewTracks: false, SmartTranspose: false));

        result.CreatedTracks.ShouldBe(0);
        result.ReplacedTracks.ShouldBe(1);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano (Adapted 1 notes)");
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)84);
    }

    [Fact]
    public void AdaptTracksToPlayableRange_SkipsConductorAndEmptyTracks()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Empty", Timed(new SequenceTrackNameEvent("Empty"), 0)));

        var result = MidiForgeOperations.AdaptTracksToPlayableRange(
            file,
            new[] { 0, 1 },
            new MidiForgeAdaptToRangeOptions());

        result.SourceTracks.ShouldBe(0);
        result.CreatedTracks.ShouldBe(0);
        file.Tracks.Count.ShouldBe(1);
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void SplitTracksChords_GroupMerged_CreatesNoChordAndChordPartTracks()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 0),
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 240, 120)));

        var result = MidiForgeOperations.SplitTracksChords(
            file,
            new[] { 0 },
            new MidiForgeSplitChordsOptions(
                Strategy: MidiForgeChordSplitStrategy.SameStartTick,
                GroupMode: MidiForgeChordGroupMode.GroupMerged,
                MinimumSimultaneousNotes: 2,
                InsertPartsAtEnd: true));

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(4);
        result.ChordGroups.ShouldBe(3);
        file.Tracks.Count.ShouldBe(5);

        file.Tracks.Skip(1).Select(track => track.Name).ShouldBe(new[]
        {
            "Piano no chords",
            "Piano chords parts (1)",
            "Piano chords parts (2)",
            "Piano chords parts (3)",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 72 });
        file.Tracks[2].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 67 });
        file.Tracks[3].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 64 });
        file.Tracks[4].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60 });
        file.Tracks.Skip(1).ShouldAllBe(track => track.Chunk.Events.OfType<ProgramChangeEvent>().Any());
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void SplitTracksChords_IndividualMode_SeparatesChordSizesAndParts()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 240, 120),
            Note(71, 240, 120),
            Note(74, 240, 120)));

        var result = MidiForgeOperations.SplitTracksChords(
            file,
            new[] { 0 },
            new MidiForgeSplitChordsOptions(GroupMode: MidiForgeChordGroupMode.Individual));

        result.CreatedTracks.ShouldBe(5);
        file.Tracks.Skip(1).Select(track => track.Name).ShouldBe(new[]
        {
            "Piano chords of 2 (1)",
            "Piano chords of 2 (2)",
            "Piano chords of 3 (1)",
            "Piano chords of 3 (2)",
            "Piano chords of 3 (3)",
        });
    }

    [Fact]
    public void SplitTracksChords_SameStartTickAndLength_DoesNotSplitDifferentDurationNotesAsChord()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 240)));

        MidiForgeOperations.SplitTracksChords(
            file,
            new[] { 0 },
            new MidiForgeSplitChordsOptions(
                Strategy: MidiForgeChordSplitStrategy.SameStartTickAndLength,
                InsertPartsAtEnd: false));

        file.Tracks.Count.ShouldBe(2);
        file.Tracks[1].Name.ShouldBe("Piano no chords");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).OrderBy(note => note)
            .ShouldBe(new[] { 60, 64 });
    }

    [Fact]
    public void AutoEditTracks_MaxOne_PicksNoChordAndTopChordLine()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 240, 120)));

        var result = MidiForgeOperations.AutoEditTracks(
            file,
            new[] { 0 },
            new MidiForgeAutoEditOptions(
                MaxSimultaneousNotes: 1,
                AdaptOutOfRangeNotes: false));

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(1);
        result.ReplacedTracks.ShouldBe(0);
        result.PickedParts.ShouldBe(2);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 64, 67, 72 });
        file.Tracks[1].Name.ShouldBe("Piano (Auto Edited Max 1)");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 67, 72 });
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void AutoEditTracks_MaxTwo_PicksSecondChordLine()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 240, 120)));

        var result = MidiForgeOperations.AutoEditTracks(
            file,
            new[] { 0 },
            new MidiForgeAutoEditOptions(
                MaxSimultaneousNotes: 2,
                AdaptOutOfRangeNotes: false));

        result.PickedParts.ShouldBe(3);
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 64, 67, 72 });
    }

    [Fact]
    public void AutoEditTracks_OddStrategyMaxTwo_PicksThirdChordLineForThreeNoteChord()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 240, 120)));

        MidiForgeOperations.AutoEditTracks(
            file,
            new[] { 0 },
            new MidiForgeAutoEditOptions(
                MaxSimultaneousNotes: 2,
                PickStrategy: MidiForgeChordPickStrategy.OddChords,
                AdaptOutOfRangeNotes: false));

        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 67, 72 });
    }

    [Fact]
    public void AutoEditTracks_AdaptOutOfRange_WrapsPickedNotesOnly()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(100, 0, 120)));

        var result = MidiForgeOperations.AutoEditTracks(
            file,
            new[] { 0 },
            new MidiForgeAutoEditOptions(
                MaxSimultaneousNotes: 1,
                AdaptOutOfRangeNotes: true));

        result.ChangedNotes.ShouldBe(1);
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 100 });
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber
            .ShouldBe((SevenBitNumber)(byte)MidiForgeOperations.AdaptMidiNoteToPlayableRange(100));
    }

    [Fact]
    public void AutoEditTracks_ReplaceOriginal_ReplacesTrackInPlace()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 240, 120)));

        var result = MidiForgeOperations.AutoEditTracks(
            file,
            new[] { 0 },
            new MidiForgeAutoEditOptions(
                MaxSimultaneousNotes: 1,
                AdaptOutOfRangeNotes: false,
                CreateNewTracks: false));

        result.CreatedTracks.ShouldBe(0);
        result.ReplacedTracks.ShouldBe(1);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano (Auto Edited Max 1)");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 67, 72 });
    }

    [Fact]
    public void SplitDrumkitTracks_DefaultMap_CreatesGameDrumTracksAndRest()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Note(36, 0, 120, channel: 9),
            Note(38, 120, 120, channel: 9),
            Note(49, 240, 120, channel: 9),
            Note(60, 360, 120, channel: 9),
            Note(42, 480, 120, channel: 9)));

        var result = MidiForgeOperations.SplitDrumkitTracks(
            file,
            new[] { 0 },
            new MidiForgeSplitDrumkitOptions(AutoEditAfterSplit: false));

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(5);
        result.RestTracks.ShouldBe(1);
        result.TransposedNotes.ShouldBe(3);
        file.Tracks.Count.ShouldBe(6);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "BassDrum",
            "SnareDrum",
            "Cymbal",
            "Bongo",
            "Drumkit Rest",
            "Drumkit",
        });
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 51 });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 62 });
        file.Tracks[2].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 73 });
        file.Tracks[3].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60 });
        file.Tracks[4].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 42 });
        file.Tracks.Take(5).SelectMany(track => track.Chunk.GetNotes()).ShouldAllBe(note => (byte)note.Channel == 9);
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void SplitDrumkitTracks_AutoEditAfterSplit_PicksHighestSameStartNote()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Note(35, 0, 120, channel: 9),
            Note(36, 0, 120, channel: 9),
            Note(38, 120, 120, channel: 9)));

        var result = MidiForgeOperations.SplitDrumkitTracks(
            file,
            new[] { 0 },
            new MidiForgeSplitDrumkitOptions(CreateRestTrack: false));

        result.CreatedTracks.ShouldBe(2);
        result.AutoEditedTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("BassDrum");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 51 });
        file.Tracks[1].Name.ShouldBe("SnareDrum");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 62 });
    }

    [Fact]
    public void SplitDrumkitTracks_AutoEditAfterSplit_TransposesC3HeavyBassTrack()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Note(35, 0, 120, channel: 9),
            Note(35, 120, 120, channel: 9),
            Note(35, 240, 120, channel: 9)));

        var result = MidiForgeOperations.SplitDrumkitTracks(
            file,
            new[] { 0 },
            new MidiForgeSplitDrumkitOptions(CreateRestTrack: false));

        result.CreatedTracks.ShouldBe(1);
        result.AutoEditedTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("BassDrum");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 52, 52, 52 });
    }

    [Fact]
    public void SplitDrumkitTracks_SkipsNonDrumChannelAndConductorTracks()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Piano", Note(36, 0, 120, channel: 0)));

        var result = MidiForgeOperations.SplitDrumkitTracks(
            file,
            new[] { 0, 1 },
            new MidiForgeSplitDrumkitOptions());

        result.SourceTracks.ShouldBe(0);
        result.CreatedTracks.ShouldBe(0);
        file.Tracks.Count.ShouldBe(2);
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void DisassembleDrumkitTracks_CreatesOneTrackPerUniqueDrumNote()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Note(36, 0, 120, channel: 9),
            Note(36, 240, 120, channel: 9),
            Note(38, 480, 120, channel: 9),
            Note(42, 720, 120, channel: 9)));

        var result = MidiForgeOperations.DisassembleDrumkitTracks(
            file,
            new[] { 0 },
            new MidiForgeDisassembleDrumkitOptions());

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(3);
        result.DeletedSourceTracks.ShouldBe(0);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Drumkit",
            "Kick Drum 1",
            "Snare Drum 1",
            "Closed Hi-Hat",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 36, 36 });
        file.Tracks[2].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 38 });
        file.Tracks[3].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 42 });
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void DisassembleDrumkitTracks_UnknownNotesUseFallbackName()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit", Note(12, 0, 120, channel: 9)));

        MidiForgeOperations.DisassembleDrumkitTracks(
            file,
            new[] { 0 },
            new MidiForgeDisassembleDrumkitOptions());

        file.Tracks[1].Name.ShouldBe("Drumkit Unknown");
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)12);
    }

    [Fact]
    public void DisassembleDrumkitTracks_DeleteOriginalTracks_RemovesSource()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Note(36, 0, 120, channel: 9),
            Note(38, 240, 120, channel: 9)));

        var result = MidiForgeOperations.DisassembleDrumkitTracks(
            file,
            new[] { 0 },
            new MidiForgeDisassembleDrumkitOptions(DeleteOriginalTracks: true));

        result.CreatedTracks.ShouldBe(2);
        result.DeletedSourceTracks.ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Kick Drum 1", "Snare Drum 1" });
    }

    [Fact]
    public void DisassembleDrumkitTracks_SkipsNonDrumChannelAndConductorTracks()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Piano", Note(36, 0, 120, channel: 0)));

        var result = MidiForgeOperations.DisassembleDrumkitTracks(
            file,
            new[] { 0, 1 },
            new MidiForgeDisassembleDrumkitOptions());

        result.SourceTracks.ShouldBe(0);
        result.CreatedTracks.ShouldBe(0);
        file.Tracks.Count.ShouldBe(2);
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void TransposeSingleNoteTracksToDrumNote_DeletesOriginalByDefault()
    {
        var file = CreateEditableFile(
            CreateTrack("Hand Clap", Note(39, 0, 120, channel: 9), Note(39, 240, 120, channel: 9)),
            CreateTrack("Mixed", Note(38, 0, 120, channel: 9), Note(40, 240, 120, channel: 9)));

        var result = MidiForgeOperations.TransposeSingleNoteTracksToDrumNote(
            file,
            new[] { 0, 1 },
            new MidiForgeTransposeToDrumNoteOptions(TargetNote: 62, TrackName: "SnareDrum"));

        result.SourceTracks.ShouldBe(2);
        result.CreatedTracks.ShouldBe(1);
        result.DeletedSourceTracks.ShouldBe(1);
        result.SkippedTracks.ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[0].Name.ShouldBe("SnareDrum");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 62, 62 });
        file.Tracks[0].Chunk.GetNotes().ShouldAllBe(note => (byte)note.Channel == 9);
        file.Tracks[1].Name.ShouldBe("Mixed");
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void TransposeSingleNoteTracksToDrumNote_KeepOriginal_InsertsAfterSource()
    {
        var file = CreateEditableFile(CreateTrack("Hand Clap", Note(39, 0, 120, channel: 9)));

        var result = MidiForgeOperations.TransposeSingleNoteTracksToDrumNote(
            file,
            new[] { 0 },
            new MidiForgeTransposeToDrumNoteOptions(
                TargetNote: 73,
                TrackName: "Cymbal",
                DeleteOriginalTracks: false));

        result.CreatedTracks.ShouldBe(1);
        result.DeletedSourceTracks.ShouldBe(0);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Hand Clap", "Cymbal" });
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)39);
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)73);
    }

    [Fact]
    public void TransposeSingleNoteTracksToDrumNote_UsesFallbackNameWhenTrackNameEmpty()
    {
        var file = CreateEditableFile(CreateTrack("Snare", Note(40, 0, 120, channel: 9)));

        MidiForgeOperations.TransposeSingleNoteTracksToDrumNote(
            file,
            new[] { 0 },
            new MidiForgeTransposeToDrumNoteOptions(TargetNote: 64, TrackName: string.Empty));

        file.Tracks[0].Name.ShouldBe("Snare (Transposed 24)");
    }

    [Fact]
    public void TransposeSingleNoteTracksToDrumNote_SkipsConductorEmptyAndMultiPitchTracks()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Empty", Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)),
            CreateTrack("Mixed", Note(38, 0, 120, channel: 9), Note(40, 240, 120, channel: 9)));

        var result = MidiForgeOperations.TransposeSingleNoteTracksToDrumNote(
            file,
            new[] { 0, 1, 2 },
            new MidiForgeTransposeToDrumNoteOptions(TargetNote: 62, TrackName: "SnareDrum"));

        result.SourceTracks.ShouldBe(2);
        result.CreatedTracks.ShouldBe(0);
        result.DeletedSourceTracks.ShouldBe(0);
        result.SkippedTracks.ShouldBe(2);
        file.Tracks.Count.ShouldBe(3);
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void SplitTracksByToneRange_CreatesInRangeAndOutOfRangeTracks()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(47, 0, 120),
            Note(48, 120, 120),
            Note(84, 240, 120),
            Note(85, 360, 120)));

        var result = MidiForgeOperations.SplitTracksByToneRange(
            file,
            new[] { 0 },
            new MidiForgeSplitToneRangeOptions(MinimumNote: 48, MaximumNote: 84));

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(2);
        result.InRangeTracks.ShouldBe(1);
        result.OutOfRangeTracks.ShouldBe(1);
        result.InRangeNotes.ShouldBe(2);
        result.OutOfRangeNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (In Range C3 (48) - C6 (84))",
            "Piano (Out of Range C3 (48) - C6 (84))",
        });
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 47, 48, 84, 85 });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 48, 84 });
        file.Tracks[2].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 47, 85 });
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void SplitTracksByToneRange_SkipsEmptyPartitions()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(60, 0, 120), Note(72, 120, 120)));

        var result = MidiForgeOperations.SplitTracksByToneRange(
            file,
            new[] { 0 },
            new MidiForgeSplitToneRangeOptions(MinimumNote: 48, MaximumNote: 84));

        result.CreatedTracks.ShouldBe(1);
        result.InRangeTracks.ShouldBe(1);
        result.OutOfRangeTracks.ShouldBe(0);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (In Range C3 (48) - C6 (84))",
        });
    }

    [Fact]
    public void SplitTracksByLengthRange_CreatesInRangeAndOutOfRangeTracks()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 60),
            Note(62, 120, 120),
            Note(64, 240, 240),
            Note(65, 520, 480)));

        var result = MidiForgeOperations.SplitTracksByLengthRange(
            file,
            new[] { 0 },
            new MidiForgeSplitLengthRangeOptions(MinimumLengthTicks: 100, MaximumLengthTicks: 240));

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(2);
        result.InRangeNotes.ShouldBe(2);
        result.OutOfRangeNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (In Range 100 - 240)",
            "Piano (Out of Range 100 - 240)",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 120, 240 });
        file.Tracks[2].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 60, 480 });
    }

    [Fact]
    public void SplitTracksByLengthRange_SwapsReversedBounds()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 240, 480)));

        var result = MidiForgeOperations.SplitTracksByLengthRange(
            file,
            new[] { 0 },
            new MidiForgeSplitLengthRangeOptions(MinimumLengthTicks: 480, MaximumLengthTicks: 120));

        result.CreatedTracks.ShouldBe(1);
        result.InRangeNotes.ShouldBe(2);
        result.OutOfRangeNotes.ShouldBe(0);
        file.Tracks[1].Name.ShouldBe("Piano (In Range 120 - 480)");
    }

    [Fact]
    public void SplitTracksOverlappedNotes_CreatesNoOverlapAndOverlapTracks()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(60, 0, 240),
            Note(62, 240, 120),
            Note(64, 480, 120)));

        var result = MidiForgeOperations.SplitTracksOverlappedNotes(file, new[] { 0 });

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(3);
        result.OverlapGroups.ShouldBe(1);
        result.OverlappedNotes.ShouldBe(2);
        result.NonOverlappedNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano no overlap",
            "Piano overlap (1)",
            "Piano overlap (2)",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 62, 64 });
        file.Tracks[2].Chunk.GetNotes().Single().Length.ShouldBe(120);
        file.Tracks[3].Chunk.GetNotes().Single().Length.ShouldBe(240);
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void SplitTracksOverlappedNotes_NoDuplicateStartPitchDoesNotDirtyFile()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(60, 120, 120),
            Note(64, 0, 120)));

        var result = MidiForgeOperations.SplitTracksOverlappedNotes(file, new[] { 0 });

        result.CreatedTracks.ShouldBe(0);
        result.OverlapGroups.ShouldBe(0);
        file.Tracks.Count.ShouldBe(1);
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void TrimOverlappedSustainedNotes_TrimsToNextOverlappingStart()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 480),
            Note(64, 240, 120),
            Note(67, 240, 120)));

        var result = MidiForgeOperations.TrimOverlappedSustainedNotes(file, new[] { 0 });

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(1);
        result.ChangedNotes.ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[1].Name.ShouldBe("Piano (Trimmed)");
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 240, 120, 120 });
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void TrimOverlappedSustainedNotes_IgnoresSameStartChords()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 480),
            Note(64, 0, 480)));

        var result = MidiForgeOperations.TrimOverlappedSustainedNotes(file, new[] { 0 });

        result.CreatedTracks.ShouldBe(0);
        result.ChangedNotes.ShouldBe(0);
        file.Tracks.Count.ShouldBe(1);
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void ExtendNotesDuration_ExtendsToNextAvailableNoteStart()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 480, 120),
            Note(64, 960, 120)));

        var result = MidiForgeOperations.ExtendNotesDuration(
            file,
            new[] { 0 },
            new MidiForgeExtendNotesDurationOptions(RespectEmptyMeasures: false));

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(1);
        result.ChangedNotes.ShouldBe(2);
        file.Tracks[1].Name.ShouldBe("Piano (Extended)");
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 480, 480, 120 });
    }

    [Fact]
    public void ExtendNotesDuration_RespectsMaximumDurationTicks()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 480, 120)));

        MidiForgeOperations.ExtendNotesDuration(
            file,
            new[] { 0 },
            new MidiForgeExtendNotesDurationOptions(MaximumDurationTicks: 240, RespectEmptyMeasures: false));

        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 240, 120 });
    }

    [Fact]
    public void ExtendNotesDuration_RespectEmptyMeasures_CapsAtCurrentMeasureEnd()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 3840, 120)));

        MidiForgeOperations.ExtendNotesDuration(
            file,
            new[] { 0 },
            new MidiForgeExtendNotesDurationOptions(RespectEmptyMeasures: true));

        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 1920, 120 });
    }

    [Fact]
    public void ChangeTrackNoteLengths_CreateNewTrack_ChangesOnlyMatchingLengths()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 240, 240),
            Note(64, 520, 480)));

        var result = MidiForgeOperations.ChangeTrackNoteLengths(
            file,
            new[] { 0 },
            new MidiForgeChangeNoteLengthOptions(
                MinimumLengthTicks: 100,
                MaximumLengthTicks: 240,
                NewLengthTicks: 60));

        result.SourceTracks.ShouldBe(1);
        result.CreatedTracks.ShouldBe(1);
        result.ReplacedTracks.ShouldBe(0);
        result.ChangedNotes.ShouldBe(2);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[0].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 120, 240, 480 });
        file.Tracks[1].Name.ShouldBe("Piano (Changed 2 notes)");
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 60, 60, 480 });
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void ChangeTrackNoteLengths_DeleteOriginalTracks_ReplacesTrackInPlace()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 240, 240)));

        var result = MidiForgeOperations.ChangeTrackNoteLengths(
            file,
            new[] { 0 },
            new MidiForgeChangeNoteLengthOptions(
                MinimumLengthTicks: 0,
                MaximumLengthTicks: 120,
                NewLengthTicks: 480,
                DeleteOriginalTracks: true));

        result.CreatedTracks.ShouldBe(0);
        result.ReplacedTracks.ShouldBe(1);
        result.ChangedNotes.ShouldBe(1);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano (Changed 1 notes)");
        file.Tracks[0].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 480, 240 });
    }

    [Fact]
    public void ChangeTrackNoteLengths_RepairsZeroLengthNotes()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 0),
            Note(62, 240, 120)));

        MidiForgeOperations.ChangeTrackNoteLengths(
            file,
            new[] { 0 },
            new MidiForgeChangeNoteLengthOptions(
                MinimumLengthTicks: 0,
                MaximumLengthTicks: 0,
                NewLengthTicks: 30));

        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 30, 120 });
    }

    [Fact]
    public void ChangeTrackNoteLengths_SkipsConductorAndTracksWithoutMatchingNotes()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Piano", Note(60, 0, 120)));

        var result = MidiForgeOperations.ChangeTrackNoteLengths(
            file,
            new[] { 0, 1 },
            new MidiForgeChangeNoteLengthOptions(
                MinimumLengthTicks: 240,
                MaximumLengthTicks: 480,
                NewLengthTicks: 60));

        result.SourceTracks.ShouldBe(0);
        result.CreatedTracks.ShouldBe(0);
        result.ChangedNotes.ShouldBe(0);
        file.Tracks.Count.ShouldBe(2);
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void FillEmptyTrackNames_MidiMode_FillsEmptyNamesOnly()
    {
        var file = CreateEditableFile(
            CreateTrack(
                string.Empty,
                Timed(new ProgramChangeEvent((SevenBitNumber)0), 0),
                Note(60, 0, 120)),
            CreateTrack(
                "Custom",
                Timed(new ProgramChangeEvent((SevenBitNumber)40), 0),
                Note(64, 120, 120)));

        var result = MidiForgeOperations.FillEmptyTrackNames(
            file,
            new[] { 0, 1 },
            MidiForgeTrackNameFillMode.Midi);

        result.SourceTracks.ShouldBe(2);
        result.RenamedTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Acoustic Grand Piano");
        file.Tracks[1].Name.ShouldBe("Custom");
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void FillEmptyTrackNames_DrumChannelUsesDrumkitName()
    {
        var file = CreateEditableFile(CreateTrack(string.Empty, Note(36, 0, 120, channel: 9)));

        var result = MidiForgeOperations.FillEmptyTrackNames(
            file,
            new[] { 0 },
            MidiForgeTrackNameFillMode.Midi);

        result.RenamedTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Drumkit");
    }

    [Fact]
    public void FillEmptyTrackNames_NoEmptyNamesDoesNotDirtyFile()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(60, 0, 120)));

        var result = MidiForgeOperations.FillEmptyTrackNames(
            file,
            new[] { 0 },
            MidiForgeTrackNameFillMode.Midi);

        result.RenamedTracks.ShouldBe(0);
        file.Tracks[0].Name.ShouldBe("Piano");
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void ClearTrackNames_PreservesDrumInstrumentNames()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano", Note(60, 0, 120)),
            CreateTrack("BassDrum", Note(48, 120, 120, channel: 9)),
            CreateTrack("Drumkit", Note(36, 240, 120, channel: 9)));

        var result = MidiForgeOperations.ClearTrackNames(file, new[] { 0, 1, 2 });

        result.SourceTracks.ShouldBe(3);
        result.RenamedTracks.ShouldBe(1);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { string.Empty, "BassDrum", "Drumkit" });
        file.IsDirty.ShouldBeTrue();
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(string name, params object[] objects)
    {
        var chunk = new TrackChunk(new SequenceTrackNameEvent(name));
        using var manager = chunk.ManageTimedEvents();

        foreach (var item in objects)
        {
            switch (item)
            {
                case TimedEvent timedEvent:
                    manager.Objects.Add(timedEvent);
                    break;
                case Note note:
                    manager.Objects.Add(new TimedEvent(
                        new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                        note.Time));
                    manager.Objects.Add(new TimedEvent(
                        new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                        note.EndTime));
                    break;
            }
        }

        return chunk;
    }

    private static TimedEvent Timed(MidiEvent midiEvent, long time)
        => new(midiEvent, time);

    private static Note Note(int noteNumber, long time, long length, int channel = 0)
        => new(
            (SevenBitNumber)(byte)noteNumber,
            length,
            time)
        {
            Channel = (FourBitNumber)(byte)channel,
            Velocity = (SevenBitNumber)100,
            OffVelocity = (SevenBitNumber)0,
        };
}
