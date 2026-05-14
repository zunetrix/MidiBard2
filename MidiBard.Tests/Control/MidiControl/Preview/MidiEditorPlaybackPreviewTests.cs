using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Preview;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Control.MidiControl.Preview;

public class MidiEditorPlaybackPreviewTests
{
    private static readonly string TestMidiPath =
        Path.Combine(AppContext.BaseDirectory, "data", "test.mid");

    private static readonly string TestR1MidiPath =
        Path.Combine(AppContext.BaseDirectory, "data", "test-r1.mid");

    public MidiEditorPlaybackPreviewTests()
    {
        DalamudTestSetup.Initialize();
    }

    [Fact]
    public void Load_TestMidiFixture_BuildsPreviewFromEditableMidiFile()
    {
        var file = new EditableMidiFile(MidiFile.Read(TestMidiPath), TestMidiPath);
        var sound = new FakeSoundPlayer();
        var preview = CreatePreview(soundPlayer: sound, defaultInstrumentId: 2);

        preview.Load(file, preservePosition: false);

        file.Tracks.Count.ShouldBeGreaterThan(0);
        preview.HasEvents.ShouldBeTrue();
        preview.DurationSeconds.ShouldBeGreaterThan(0);
        preview.EventSnapshots.ShouldContain(e => e.EventType == MidiEventType.NoteOn.ToString());
        preview.EventSnapshots.ShouldContain(e => e.EventType == MidiEventType.NoteOff.ToString());
        sound.PlayCalls.ShouldBeEmpty();
    }

    [Fact]
    public void Load_UsesLiveEditableNoteDurationWithoutFlushingTrack()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Timed(new NoteOnEvent((SevenBitNumber)(byte)60, (SevenBitNumber)(byte)100), 0),
                Timed(new NoteOffEvent((SevenBitNumber)(byte)60, (SevenBitNumber)(byte)0), 120)));
        var track = file.Tracks.Single();
        track.LoadEvents(file.TempoMap);
        var note = track.Events!.Single(e => e.NoteOffSource != null);
        note.EditDuration = 360;
        note.ApplyEditValues();

        var preview = CreatePreview(defaultInstrumentId: 2);
        preview.Load(file, preservePosition: false);

        preview.EventSnapshots.ShouldContain(e =>
            e.EventType == MidiEventType.NoteOff.ToString() &&
            e.EventValue == 60 &&
            e.Time == 360);
    }

    [Fact]
    public void Load_SimultaneousSameTrackNotes_KeepsLowToHighPlaybackEvents()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Timed(NoteOn(74), 0),
                Timed(NoteOn(52), 0),
                Timed(NoteOff(52), 120),
                Timed(NoteOff(74), 240)));
        var preview = CreatePreview(defaultInstrumentId: 2);

        preview.Load(file, preservePosition: false);

        preview.EventSnapshots
            .Where(e => e.EventType == MidiEventType.NoteOn.ToString())
            .OrderBy(e => e.EventValue)
            .Select(e => e.EventValue)
            .ShouldBe(new[] { 52, 74 });
        preview.EventSnapshots.ShouldContain(e =>
            e.EventType == MidiEventType.NoteOff.ToString() &&
            e.EventValue == 52 &&
            e.Time == 120);
        preview.EventSnapshots.ShouldContain(e =>
            e.EventType == MidiEventType.NoteOff.ToString() &&
            e.EventValue == 74 &&
            e.Time == 240);
    }

    [Theory]
    [InlineData(AntiStackType.Off, 120)]
    [InlineData(AntiStackType.KeepFirstNote, 120)]
    [InlineData(AntiStackType.KeepShortestNote, 120)]
    [InlineData(AntiStackType.KeepLongestNote, 240)]
    public void Load_DuplicateSamePitchNotes_AppliesAntiStackSettingToPreviewOnly(
        AntiStackType antiStackType,
        long expectedNoteOffTime)
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Timed(NoteOn(60), 0),
                Timed(NoteOn(60), 0),
                Timed(NoteOff(60), 120),
                Timed(NoteOff(60), 240)));
        var preview = CreatePreview(
            new FakePreviewSettings { AntiStackType = antiStackType },
            defaultInstrumentId: 2);

        preview.Load(file, preservePosition: false);

        preview.EventSnapshots
            .Where(e => e.EventType == MidiEventType.NoteOn.ToString())
            .Select(e => e.EventValue)
            .ShouldBe(antiStackType == AntiStackType.Off ? new[] { 60, 60 } : new[] { 60 });
        preview.EventSnapshots
            .Where(e => e.EventType == MidiEventType.NoteOff.ToString())
            .Select(e => e.Time)
            .ShouldBe(antiStackType == AntiStackType.Off ? new long[] { 120, 240 } : new[] { expectedNoteOffTime });

        file.Tracks.Single().Chunk.GetNotes()
            .Select(note => note.EndTime)
            .OrderBy(time => time)
            .ShouldBe(new long[] { 120, 240 });
    }

    [Fact]
    public void Load_DuplicateInstrumentTracks_KeepsSeparateTrackEventsAndPlayback()
    {
        var file = CreateEditableFile(
            CreateTrack("Clarinet",
                Timed(NoteOn(60), 0),
                Timed(NoteOff(60), 120)),
            CreateTrack("Clarinet",
                Timed(NoteOn(64), 0),
                Timed(NoteOff(64), 120)));
        var sound = new FakeSoundPlayer();
        var preview = CreatePreview(soundPlayer: sound);

        preview.Load(file, preservePosition: false);
        ReplaySnapshots(preview, 0, 1);

        preview.EventSnapshots.ShouldContain(e =>
            e.TrackIndex == 0 &&
            e.EventType == MidiEventType.NoteOn.ToString() &&
            e.EventValue == 60);
        preview.EventSnapshots.ShouldContain(e =>
            e.TrackIndex == 1 &&
            e.EventType == MidiEventType.NoteOn.ToString() &&
            e.EventValue == 64);
        sound.PlayCalls.Count.ShouldBe(2);
        sound.PlayCalls.Select(call => call.Request.TrackIndex).ShouldBe(new[] { 0, 1 });
        sound.PlayCalls.Select(call => call.Request.InstrumentId).ShouldBe(new uint[] { 7, 7 });
    }

    [Theory]
    [InlineData(AntiStackType.Off)]
    [InlineData(AntiStackType.KeepFirstNote)]
    [InlineData(AntiStackType.KeepShortestNote)]
    [InlineData(AntiStackType.KeepLongestNote)]
    public void Load_DuplicateInstrumentTracksWithSamePitch_DoesNotAntiStackAcrossTracks(AntiStackType antiStackType)
    {
        var file = CreateEditableFile(
            CreateTrack("Clarinet",
                Timed(NoteOn(60), 0),
                Timed(NoteOff(60), 120)),
            CreateTrack("Clarinet",
                Timed(NoteOn(60), 0),
                Timed(NoteOff(60), 120)));
        var sound = new FakeSoundPlayer();
        var preview = CreatePreview(
            new FakePreviewSettings { AntiStackType = antiStackType },
            sound);

        preview.Load(file, preservePosition: false);
        ReplaySnapshots(preview, 0, 1);

        preview.EventSnapshots
            .Where(e => e.EventType == MidiEventType.NoteOn.ToString())
            .Select(e => e.TrackIndex)
            .OrderBy(trackIndex => trackIndex)
            .ShouldBe(new[] { 0, 1 });
        sound.PlayCalls.Count.ShouldBe(2);
        sound.PlayCalls.Select(call => call.Request.TrackIndex).ShouldBe(new[] { 0, 1 });
        sound.PlayCalls.Select(call => call.Request.InstrumentId).ShouldBe(new uint[] { 7, 7 });
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 60, 60 });
    }

    [Fact]
    public void Load_SyntheticClarinetTracks_ProducesPreviewEventsForBothClarinetTracks()
    {
        var file = CreateEditableFile(
            CreateTrack("Clarinet",
                Timed(NoteOn(53), 120),
                Timed(NoteOn(56), 120),
                Timed(NoteOn(60), 120),
                Timed(NoteOff(53), 240),
                Timed(NoteOff(56), 240),
                Timed(NoteOff(60), 240),
                Timed(NoteOn(58), 10200),
                Timed(NoteOn(63), 10200),
                Timed(NoteOn(67), 10200),
                Timed(NoteOff(58), 10320),
                Timed(NoteOff(63), 10320),
                Timed(NoteOff(67), 10320)),
            CreateTrack("Clarinet",
                Timed(NoteOn(67), 10200),
                Timed(NoteOff(67), 10320)));
        var preview = CreatePreview(defaultInstrumentId: 2);

        preview.Load(file, preservePosition: false);

        file.Tracks
            .Select((track, index) => new { track.Name, Index = index })
            .Where(track => string.Equals(track.Name.Trim(), "Clarinet", StringComparison.OrdinalIgnoreCase))
            .Select(track => track.Index)
            .ShouldBe(new[] { 0, 1 });
        preview.EventSnapshots
            .Where(e =>
                e.TrackIndex == 0 &&
                e.Time == 120 &&
                e.EventType == MidiEventType.NoteOn.ToString())
            .Select(e => e.EventValue)
            .OrderBy(value => value)
            .ShouldBe(new[] { 53, 56, 60 });
        preview.EventSnapshots
            .Where(e =>
                e.TrackIndex == 0 &&
                e.Time == 10200 &&
                e.EventType == MidiEventType.NoteOn.ToString())
            .Select(e => e.EventValue)
            .OrderBy(value => value)
            .ShouldBe(new[] { 58, 63, 67 });
        preview.EventSnapshots.ShouldContain(e =>
            e.TrackIndex == 1 &&
            e.Time == 10200 &&
            e.EventType == MidiEventType.NoteOn.ToString() &&
            e.EventValue == 67);
    }

    [Theory]
    [InlineData("ProgramElectricGuitar", true)]
    [InlineData("Program:ElectricGuitar", true)]
    [InlineData("Program: ElectricGuitar", true)]
    [InlineData("ElectricGuitarOverdriven", false)]
    public void IsProgramElectricGuitarTrackName_UsesTrackInfoInstrumentNormalization(string trackName, bool expected)
    {
        TrackInfo.IsProgramElectricGuitarTrackName(trackName).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Piano+1", "Piano")]
    [InlineData("Piano -1", "Piano")]
    [InlineData("Program: ElectricGuitar +1", "Program: ElectricGuitar")]
    [InlineData("Piano (+1)", "Piano")]
    [InlineData("Lead (Transposed -12)", "Lead")]
    [InlineData("Lead (Transposed 12)", "Lead (Transposed 12)")]
    public void RemoveTransposeFromTrackName_CleansParsedTransposeMarkers(string trackName, string expected)
    {
        TrackInfo.RemoveTransposeFromTrackName(trackName).ShouldBe(expected);
    }

    [Fact]
    public void GetResolvedInstrumentIdForTrack_UsesTrackNameResolution()
    {
        var file = CreateEditableFile(
            CreateTrack("Trumpet",
                Timed(NoteOn(60), 0),
                Timed(NoteOff(60), 120)));
        var preview = CreatePreview(new FakePreviewSettings { DefaultInstrumentId = 0 });

        preview.Load(file, preservePosition: false);

        preview.GetResolvedInstrumentIdForTrack(0, 0).ShouldBe(15u);
    }

    [Fact]
    public void GetResolvedInstrumentIdForTrack_DoesNotUseProgramFallbackForUnnamedTrack()
    {
        var file = CreateEditableFile(
            CreateTrack(string.Empty,
                Timed(new ProgramChangeEvent((SevenBitNumber)(byte)56), 0),
                Timed(NoteOn(60), 10),
                Timed(NoteOff(60), 120)));
        var preview = CreatePreview(new FakePreviewSettings { DefaultInstrumentId = 0 });

        preview.Load(file, preservePosition: false);

        preview.GetResolvedInstrumentIdForTrack(0, 0).ShouldBeNull();
    }

    [Fact]
    public void GetResolvedInstrumentIdForTrack_UsesDefaultWhenTrackNameDoesNotResolve()
    {
        var file = CreateEditableFile(
            CreateTrack(string.Empty,
                Timed(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0),
                Timed(NoteOn(60), 10),
                Timed(NoteOff(60), 120)));
        var preview = CreatePreview(new FakePreviewSettings { DefaultInstrumentId = 15 });

        preview.Load(file, preservePosition: false);

        preview.GetResolvedInstrumentIdForTrack(0, 0).ShouldBe(15u);
    }

    [Fact]
    public void GetResolvedInstrumentIdForTrack_ForceDefaultOverridesTrackNameAndProgramTone()
    {
        var file = CreateEditableFile(
            CreateTrack("Trumpet",
                Timed(new ProgramChangeEvent((SevenBitNumber)(byte)56), 0),
                Timed(NoteOn(60), 10),
                Timed(NoteOff(60), 120)));
        var preview = CreatePreview(new FakePreviewSettings
        {
            DefaultInstrumentId = 2,
            ForceDefaultInstrument = true,
        });

        preview.Load(file, preservePosition: false);

        preview.GetResolvedInstrumentIdForTrack(0, 0).ShouldBe(2u);
    }

    [Fact]
    public void GetResolvedInstrumentIdForTrack_ProgramElectricGuitarUsesCurrentGuitarProgram()
    {
        var file = CreateEditableFile(
            CreateTrack("Program: ElectricGuitar",
                Timed(new ProgramChangeEvent((SevenBitNumber)(byte)30), 0),
                Timed(NoteOn(60), 10),
                Timed(NoteOff(60), 120)));
        var preview = CreatePreview(new FakePreviewSettings
        {
            DefaultInstrumentId = 0,
            GuitarToneMode = GuitarToneMode.ProgramElectricGuitarMode,
        });

        preview.Load(file, preservePosition: false);

        preview.GetResolvedInstrumentIdForTrack(0, 0).ShouldBe(27u);
    }

    [Fact]
    public void GetResolvedInstrumentIdForTrack_MatchesPreviewPlaybackInstrument()
    {
        var sound = new FakeSoundPlayer();
        var file = CreateEditableFile(
            CreateTrack("Program: ElectricGuitar",
                Timed(new ProgramChangeEvent((SevenBitNumber)(byte)30), 0),
                Timed(NoteOn(60), 10),
                Timed(NoteOff(60), 120)));
        var preview = CreatePreview(
            new FakePreviewSettings
            {
                DefaultInstrumentId = 0,
                GuitarToneMode = GuitarToneMode.ProgramElectricGuitarMode,
            },
            sound);

        preview.Load(file, preservePosition: false);
        var iconInstrument = preview.GetResolvedInstrumentIdForTrack(0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 10);

        iconInstrument.ShouldBe(27u);
        sound.PlayCalls.Single().Request.InstrumentId.ShouldBe(iconInstrument.GetValueOrDefault());
    }

    [Fact]
    public void GetResolvedInstrumentIdForTrack_ProgramElectricGuitarModeDoesNotOverrideRegularGuitarTrack()
    {
        var file = CreateEditableFile(
            CreateTrack("ElectricGuitarOverdriven",
                Timed(new ProgramChangeEvent((SevenBitNumber)(byte)30), 0),
                Timed(NoteOn(60), 10),
                Timed(NoteOff(60), 120)));
        var preview = CreatePreview(new FakePreviewSettings
        {
            DefaultInstrumentId = 0,
            GuitarToneMode = GuitarToneMode.ProgramElectricGuitarMode,
        });

        preview.Load(file, preservePosition: false);

        preview.GetResolvedInstrumentIdForTrack(0, 0).ShouldBe(24u);
    }

    [Theory]
    [InlineData("Piano+1", 0, 24)]
    [InlineData("Piano -1", 0, 0)]
    [InlineData("Piano+1", -12, 12)]
    public void TrackNameTranspose_AppliesToPreviewPlaybackGameNote(
        string trackName,
        int transposeGlobal,
        int expectedGameNote)
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview(
            trackName,
            sound,
            settings: new FakePreviewSettings { TransposeGlobal = transposeGlobal });

        preview.ProcessEventForTesting(NoteOn(60), 0, 0);

        sound.PlayCalls.Single().Request.GameNote.ShouldBe(expectedGameNote);
    }

    [Theory]
    [InlineData("Piano", 36, 0)]
    [InlineData("Piano", 96, 36)]
    [InlineData("Piano+1", 96, 36)]
    public void AdaptNotesOor_WrapsPreviewPlaybackIntoPlayableRange(
        string trackName,
        int midiNote,
        int expectedGameNote)
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview(
            trackName,
            sound,
            settings: new FakePreviewSettings { AdaptNotesOOR = true });

        preview.ProcessEventForTesting(NoteOn(midiNote), 0, 0);

        sound.PlayCalls.Single().Request.GameNote.ShouldBe(expectedGameNote);
    }

    [Theory]
    [InlineData(36)]
    [InlineData(96)]
    public void AdaptNotesOorOff_SkipsOutOfRangePreviewNotes(int midiNote)
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview(
            "Piano",
            sound,
            settings: new FakePreviewSettings { AdaptNotesOOR = false });

        preview.ProcessEventForTesting(NoteOn(midiNote), 0, 0);

        sound.PlayCalls.ShouldBeEmpty();
    }

    [Fact]
    public void Load_TestR1PianoTrack_KeepsSimultaneousChordEvents()
    {
        File.Exists(TestR1MidiPath).ShouldBeTrue();
        var file = new EditableMidiFile(MidiFile.Read(TestR1MidiPath), TestR1MidiPath);
        new FileInfo(TestR1MidiPath).Length.ShouldBeLessThan(1024);
        var pianoTrackIndex = file.Tracks.FindIndex(track =>
            string.Equals(track.Name.Trim(), "Piano", StringComparison.OrdinalIgnoreCase));
        pianoTrackIndex.ShouldBeGreaterThan(-1);

        var firstChord = file.Tracks[pianoTrackIndex].Chunk.GetNotes()
            .GroupBy(note => note.Time)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key)
            .First();
        var chordNotes = firstChord
            .Select(note => new
            {
                Note = note,
                MidiNote = (int)(byte)note.NoteNumber,
                GameNote = TrackInfo.TranslateNoteNumber((byte)note.NoteNumber, adaptOOR: true),
            })
            .Where(note => note.GameNote is >= 0 and <= 36)
            .OrderBy(note => note.MidiNote)
            .ToList();
        chordNotes.Count.ShouldBeGreaterThan(1);
        var preview = CreatePreview(defaultInstrumentId: 2);

        preview.Load(file, preservePosition: false);

        var chordNoteOns = preview.EventSnapshots
            .Where(e =>
                e.TrackIndex == pianoTrackIndex &&
                e.Time == firstChord.Key &&
                e.EventType == MidiEventType.NoteOn.ToString())
            .Select(e => e.EventValue)
            .OrderBy(value => value)
            .ToArray();
        chordNoteOns.ShouldBe(chordNotes.Select(note => note.MidiNote).ToArray());
    }

    [Fact]
    public void Load_TestR1ProgramElectricGuitarTrack_SwitchesThroughAllGuitarTones()
    {
        var file = new EditableMidiFile(MidiFile.Read(TestR1MidiPath), TestR1MidiPath);
        var programTrackIndex = file.Tracks.FindIndex(track =>
            TrackInfo.IsProgramElectricGuitarTrackName(track.Name));
        programTrackIndex.ShouldBeGreaterThan(-1);
        file.Tracks[programTrackIndex].Name.ShouldBe("ProgramElectricGuitar");

        var sound = new FakeSoundPlayer();
        var preview = CreatePreview(
            new FakePreviewSettings { GuitarToneMode = GuitarToneMode.ProgramElectricGuitarMode },
            sound,
            defaultInstrumentId: 2);

        preview.Load(file, preservePosition: false);

        var programEvents = preview.EventSnapshots
            .Where(e => e.TrackIndex == programTrackIndex && e.EventType == MidiEventType.ProgramChange.ToString())
            .ToArray();
        var noteOns = preview.EventSnapshots
            .Where(e => e.TrackIndex == programTrackIndex && e.EventType == MidiEventType.NoteOn.ToString())
            .ToArray();
        var expectedPrograms = new[] { 29, 27, 28, 30, 31 };
        programEvents.Select(e => e.ProgramNumber!.Value).ShouldBe(expectedPrograms);
        noteOns.Length.ShouldBe(expectedPrograms.Length);
        programEvents.Zip(noteOns).ShouldAllBe(pair => pair.First.Time < pair.Second.Time);

        ReplaySnapshots(preview, programTrackIndex);

        sound.PlayCalls
            .Where(call => call.Request.TrackIndex == programTrackIndex)
            .Select(call => call.Request.InstrumentId)
            .ShouldBe(new uint[] { 24, 25, 26, 27, 28 });
    }

    [Fact]
    public void ReleasePolicy_DynamicHoldInstrumentsClampFadeToHeldDuration()
    {
        var policy = new MidiEditorPreviewReleasePolicy();

        policy.GetMusicalReleaseFadeMs(15, 0.05).ShouldBe(300u);
        policy.GetMusicalReleaseFadeMs(15, 0.675).ShouldBe(675u);
        policy.GetMusicalReleaseFadeMs(15, 2.5).ShouldBe(1000u);
    }

    [Theory]
    [InlineData(2, 2500)]
    [InlineData(10, 3000)]
    [InlineData(12, 1500)]
    [InlineData(14, 5000)]
    public void ReleasePolicy_UsesFamilySpecificNaturalOneShotCleanupDelays(uint instrumentId, uint expectedDelayMs)
    {
        var policy = new MidiEditorPreviewReleasePolicy();

        policy.GetNaturalOneShotCleanupDelayMs(instrumentId).ShouldBe(expectedDelayMs);
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(9, true)]
    [InlineData(15, true)]
    [InlineData(19, true)]
    [InlineData(24, true)]
    [InlineData(25, true)]
    [InlineData(27, true)]
    [InlineData(2, false)]
    [InlineData(10, false)]
    [InlineData(14, false)]
    [InlineData(26, false)]
    public void ReleasePolicy_ClassifiesInstrumentFamilies(uint instrumentId, bool expectedDynamic)
    {
        var policy = new MidiEditorPreviewReleasePolicy();
        var expectedMode = expectedDynamic
            ? MidiEditorPreviewReleaseMode.DynamicHold
            : MidiEditorPreviewReleaseMode.NaturalOneShot;

        policy.GetReleaseMode(instrumentId).ShouldBe(expectedMode);
        policy.ShouldStopOnMusicalRelease(instrumentId).ShouldBe(expectedDynamic);
    }

    [Fact]
    public void SyntheticTrumpetTrack_UsesDurationBasedReleaseFades()
    {
        var file = CreateEditableFile(
            CreateTrack("Trumpet",
                Timed(NoteOn(60), 0),
                Timed(NoteOff(60), 120),
                Timed(NoteOn(64), 960),
                Timed(NoteOff(64), 1440)));
        var trumpetTrackIndex = 0;
        var tempoMap = file.Source.GetTempoMap();
        var trumpetNotes = file.Tracks[trumpetTrackIndex].Chunk.GetNotes()
            .Select(note => new
            {
                Note = note,
                DurationMs = GetDurationMs(note, tempoMap),
                FadeMs = GetExpectedDynamicReleaseFadeMs(note, tempoMap),
            })
            .OrderBy(note => note.Note.Time)
            .ToArray();
        var shortNote = trumpetNotes.First(note => note.DurationMs < MidiEditorPreviewReleasePolicy.MinimumDynamicReleaseFadeMs);
        var longNote = trumpetNotes.First(note =>
            note.FadeMs > MidiEditorPreviewReleasePolicy.MinimumDynamicReleaseFadeMs &&
            note.FadeMs < MidiEditorPreviewReleasePolicy.MaximumDynamicReleaseFadeMs);
        var sound = new FakeSoundPlayer();
        var preview = CreatePreview(soundPlayer: sound);

        preview.Load(file, preservePosition: false);
        PlayAndReleaseNote(preview, trumpetTrackIndex, shortNote.Note, tempoMap);
        PlayAndReleaseNote(preview, trumpetTrackIndex, longNote.Note, tempoMap);

        shortNote.FadeMs.ShouldBe(300u);
        longNote.FadeMs.ShouldBeGreaterThan(shortNote.FadeMs);
        sound.StopCalls.Select(call => call.FadeOutDuration).ShouldBe(new[] { shortNote.FadeMs, longNote.FadeMs });
    }

    [Fact]
    public void DynamicNoteOff_UsesHeldDurationReleaseFade()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Clarinet", sound);

        preview.ProcessEventForTesting(NoteOn(60), 0, 0, 0.0);
        preview.ProcessEventForTesting(NoteOff(60), 0, 120, 0.4);

        sound.PlayCalls.Count.ShouldBe(1);
        sound.StopCalls.Count.ShouldBe(1);
        sound.StopCalls[0].FadeOutDuration.ShouldBe(400u);
        preview.GetTrackSnapshots()[0].HeldNoteCount.ShouldBe(0);
        preview.GetTrackSnapshots()[0].CurrentSound.ShouldBe(0);
    }

    [Fact]
    public void NaturalOneShotNoteOff_DoesNotStopSoundUntilCleanup()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Piano", sound);

        preview.ProcessEventForTesting(NoteOn(60), 0, 0, 0.0);
        preview.ProcessEventForTesting(NoteOff(60), 0, 120, 0.4);

        sound.PlayCalls.Count.ShouldBe(1);
        sound.StopCalls.ShouldBeEmpty();
        preview.GetTrackSnapshots()[0].HeldNoteCount.ShouldBe(0);
        preview.GetTrackSnapshots()[0].CurrentSound.ShouldBe(0);

        preview.Stop();

        sound.StopCalls.Count.ShouldBe(1);
        sound.StopCalls[0].FadeOutDuration.ShouldBe(50u);
    }

    [Fact]
    public void NaturalOneShotCleanup_FiresAfterInstrumentCleanupDelay()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Piano", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(60), 0, 0, 0.0);
        preview.ProcessEventForTesting(NoteOff(60), 0, 120, 0.4);
        scheduler.AdvanceBy(2499);

        sound.StopCalls.ShouldBeEmpty();

        scheduler.AdvanceBy(1);

        sound.StopCalls.Count.ShouldBe(1);
        sound.StopCalls[0].FadeOutDuration.ShouldBe(50u);

        preview.Stop();
        sound.StopCalls.Count.ShouldBe(1);
    }

    [Fact]
    public void NaturalOneShotCleanup_UsesLongerCymbalDelay()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Cymbal", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(60), 0, 0, 0.0);
        preview.ProcessEventForTesting(NoteOff(60), 0, 120, 0.4);
        scheduler.AdvanceBy(4999);

        sound.StopCalls.ShouldBeEmpty();

        scheduler.AdvanceBy(1);

        sound.StopCalls.Count.ShouldBe(1);
        sound.StopCalls[0].FadeOutDuration.ShouldBe(50u);
    }

    [Fact]
    public void NaturalOneShotCleanup_IsCancelledByTransportStop()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Piano", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(60), 0, 0, 0.0);
        preview.ProcessEventForTesting(NoteOff(60), 0, 120, 0.4);
        preview.Stop();
        scheduler.AdvanceBy(2500);

        sound.StopCalls.Count.ShouldBe(1);
        sound.StopCalls[0].FadeOutDuration.ShouldBe(50u);
    }

    [Fact]
    public void Compensation_DelaysNotePlaybackUntilScheduledTime()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var compensation = new FakeCompensationProvider { DefaultDelayMs = 80 };
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler, compensationProvider: compensation);

        preview.SendEventForTesting(NoteOn(60), 0, 0, 0.0);
        scheduler.AdvanceBy(79);

        sound.PlayCalls.ShouldBeEmpty();

        scheduler.AdvanceBy(1);

        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 60 });
    }

    [Fact]
    public void Compensation_StopCancelsPendingDelayedEvents()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var compensation = new FakeCompensationProvider { DefaultDelayMs = 80 };
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler, compensationProvider: compensation);

        preview.SendEventForTesting(NoteOn(60), 0, 0, 0.0);
        preview.Stop();
        scheduler.AdvanceBy(80);

        sound.PlayCalls.ShouldBeEmpty();
    }

    [Fact]
    public void Compensation_LoadCancelsPendingDelayedEvents()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var compensation = new FakeCompensationProvider { DefaultDelayMs = 80 };
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler, compensationProvider: compensation);

        preview.SendEventForTesting(NoteOn(60), 0, 0, 0.0);
        preview.Load(CreateEditableFile(CreateTrack("Clarinet", Timed(NoteOn(64), 0), Timed(NoteOff(64), 120))), preservePosition: false);
        scheduler.AdvanceBy(80);

        sound.PlayCalls.ShouldBeEmpty();
    }

    [Fact]
    public void Compensation_ProcessesGuitarProgramChangesBeforeDelayedNotes()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var compensation = new FakeCompensationProvider { DefaultDelayMs = 50 };
        var preview = CreateLoadedPreview(
            "ElectricGuitarOverdriven",
            sound,
            GuitarToneMode.Standard,
            scheduler: scheduler,
            compensationProvider: compensation);

        preview.SendEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0, 0);
        preview.SendEventForTesting(NoteOn(60), 0, 0, 0.0);

        sound.PlayCalls.ShouldBeEmpty();

        scheduler.AdvanceBy(50);

        sound.PlayCalls.Single().Request.InstrumentId.ShouldBe(25u);
    }

    [Fact]
    public void Compensation_DelayedNoteKeepsInstrumentResolvedAtOriginalEventTime()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var compensation = new FakeCompensationProvider { DefaultDelayMs = 50 };
        var preview = CreateLoadedPreview(
            "ProgramElectricGuitar",
            sound,
            GuitarToneMode.ProgramElectricGuitarMode,
            scheduler: scheduler,
            compensationProvider: compensation);

        preview.SendEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)29), 0, 0);
        preview.SendEventForTesting(NoteOn(60), 0, 0, 0.0);
        preview.SendEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)30), 0, 1);
        scheduler.AdvanceBy(50);

        sound.PlayCalls.Single().Request.InstrumentId.ShouldBe(24u);
    }

    [Fact]
    public void Compensation_UsesDelayedDispatchTimesForDynamicReleaseFade()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var compensation = new FakeCompensationProvider { DefaultDelayMs = 80 };
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler, compensationProvider: compensation);

        preview.SendEventForTesting(NoteOn(60), 0, 0, 0.0);
        scheduler.AdvanceBy(80);
        preview.SendEventForTesting(NoteOff(60), 0, 120, 0.4);
        scheduler.AdvanceBy(80);

        sound.StopCalls.Count.ShouldBe(1);
        sound.StopCalls[0].FadeOutDuration.ShouldBe(400u);
    }

    [Fact]
    public void Compensation_CorrectsSameTickChordDelaySoLowNoteStillStartsFirst()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var compensation = new FakeCompensationProvider();
        compensation.SetDelay(7, TrackInfo.TranslateNoteNumber(55, adaptOOR: true), 80);
        compensation.SetDelay(7, TrackInfo.TranslateNoteNumber(60, adaptOOR: true), 10);
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler, compensationProvider: compensation);

        preview.SendEventForTesting(NoteOn(55), 0, 0, 0.0);
        preview.SendEventForTesting(NoteOn(60), 0, 0, 0.0);
        scheduler.AdvanceBy(79);

        sound.PlayCalls.ShouldBeEmpty();

        scheduler.AdvanceBy(1);

        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55 });

        scheduler.AdvanceBy(35);

        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55, 60 });
    }

    [Fact]
    public void Compensation_CorrectedThreeNoteSameTickChordRollsLowToHigh()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var compensation = new FakeCompensationProvider();
        compensation.SetDelay(7, TrackInfo.TranslateNoteNumber(53, adaptOOR: true), 80);
        compensation.SetDelay(7, TrackInfo.TranslateNoteNumber(56, adaptOOR: true), 10);
        compensation.SetDelay(7, TrackInfo.TranslateNoteNumber(60, adaptOOR: true), 5);
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler, compensationProvider: compensation);

        preview.SendEventForTesting(NoteOn(53), 0, 120, 0.0);
        preview.SendEventForTesting(NoteOn(56), 0, 120, 0.0);
        preview.SendEventForTesting(NoteOn(60), 0, 120, 0.0);
        scheduler.AdvanceBy(80);

        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 53 });

        scheduler.AdvanceBy(35);

        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 53, 56 });

        scheduler.AdvanceBy(35);

        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 53, 56, 60 });
        preview.GetTrackSnapshots()[0].CurrentMidiNote.ShouldBe(60);
    }

    [Fact]
    public async Task TimerScheduler_SameDelayCallbacksExecuteInScheduleOrder()
    {
        using var scheduler = new TimerMidiEditorPreviewScheduler();
        var order = new List<int>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        scheduler.Schedule(TimeSpan.FromMilliseconds(50), () => AddSchedulerOrder(order, 1));
        scheduler.Schedule(TimeSpan.FromMilliseconds(50), () => AddSchedulerOrder(order, 2));
        scheduler.Schedule(TimeSpan.FromMilliseconds(50), () =>
        {
            AddSchedulerOrder(order, 3);
            completed.TrySetResult();
        });

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        order.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task TimerScheduler_EarlierDueCallbackRunsBeforeLaterDueCallback()
    {
        using var scheduler = new TimerMidiEditorPreviewScheduler();
        var order = new List<int>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        scheduler.Schedule(TimeSpan.FromMilliseconds(80), () =>
        {
            AddSchedulerOrder(order, 2);
            completed.TrySetResult();
        });
        scheduler.Schedule(TimeSpan.FromMilliseconds(20), () => AddSchedulerOrder(order, 1));

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        order.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task TimerScheduler_DisposedCallbackDoesNotRun()
    {
        using var scheduler = new TimerMidiEditorPreviewScheduler();
        var order = new List<int>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var cancelled = scheduler.Schedule(TimeSpan.FromMilliseconds(20), () => AddSchedulerOrder(order, 1));
        cancelled.Dispose();
        scheduler.Schedule(TimeSpan.FromMilliseconds(40), () =>
        {
            AddSchedulerOrder(order, 2);
            completed.TrySetResult();
        });

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        order.ShouldBe(new[] { 2 });
    }

    [Fact]
    public void Stop_UsesCleanupFadeAndClearsHeldState()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Piano", sound);

        preview.ProcessEventForTesting(NoteOn(60), 0, 0);
        preview.Stop();

        sound.StopCalls.ShouldContain(call => call.FadeOutDuration == 50u);
        preview.GetTrackSnapshots()[0].HeldNoteCount.ShouldBe(0);
        preview.GetTrackSnapshots()[0].CurrentSound.ShouldBe(0);
    }

    [Fact]
    public void Seek_AppliesPriorGuitarProgramChangeBeforeLaterNote()
    {
        var sound = new FakeSoundPlayer();
        var file = CreateEditableFile(
            CreateTrack("ElectricGuitarOverdriven",
                Timed(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0),
                Timed(NoteOn(60), 480),
                Timed(NoteOff(60), 600)));
        var preview = CreatePreview(
            new FakePreviewSettings
            {
                DefaultInstrumentId = 0,
                GuitarToneMode = GuitarToneMode.Standard,
            },
            sound);

        preview.Load(file, preservePosition: false);
        preview.Seek(0.25);
        preview.ProcessEventForTesting(NoteOn(60), 0, 480);

        sound.PlayCalls.Single().Request.InstrumentId.ShouldBe(25u);
    }

    [Fact]
    public void HiddenTrack_DoesNotPlayUntilVisibilityRefresh()
    {
        var visible = false;
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Piano", sound, trackVisibilityProvider: _ => visible);

        preview.ProcessEventForTesting(NoteOn(60), 0, 0);
        sound.PlayCalls.ShouldBeEmpty();
        preview.GetTrackSnapshots()[0].HeldNoteCount.ShouldBe(1);

        visible = true;
        preview.RefreshVisibilityForTesting();

        sound.PlayCalls.Count.ShouldBe(1);
        sound.PlayCalls[0].Request.MidiNote.ShouldBe(60);
    }

    [Fact]
    public void HiddenTrack_LaterNotePrunesOlderHeldNoteBeforeVisibilityResume()
    {
        var visible = false;
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Clarinet", sound, trackVisibilityProvider: _ => visible);

        preview.ProcessEventForTesting(NoteOn(55), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 10);

        sound.PlayCalls.ShouldBeEmpty();
        preview.GetTrackSnapshots()[0].HeldNoteCount.ShouldBe(1);

        visible = true;
        preview.RefreshVisibilityForTesting();

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(1);
        snapshot.CurrentMidiNote.ShouldBe(60);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 60 });
    }

    [Fact]
    public void VisibilityHiddenAfterActiveNote_StopsSoundButKeepsHeldStateForResume()
    {
        var visible = true;
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Clarinet", sound, trackVisibilityProvider: _ => visible);

        preview.ProcessEventForTesting(NoteOn(60), 0, 0);
        visible = false;
        preview.RefreshVisibilityForTesting();

        var hiddenSnapshot = preview.GetTrackSnapshots()[0];
        hiddenSnapshot.HeldNoteCount.ShouldBe(1);
        hiddenSnapshot.CurrentSound.ShouldBe(0);
        sound.StopCalls.Single().FadeOutDuration.ShouldBe(1000u);

        visible = true;
        preview.RefreshVisibilityForTesting();

        var visibleSnapshot = preview.GetTrackSnapshots()[0];
        visibleSnapshot.HeldNoteCount.ShouldBe(1);
        visibleSnapshot.CurrentMidiNote.ShouldBe(60);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 60, 60 });
    }

    [Fact]
    public void SimultaneousNotes_OnSameTrack_PlayLowThenHighAndDropLowerHeldNote()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(55), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 0);

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(2);
        snapshot.CurrentMidiNote.ShouldBe(55);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55 });
        sound.StopCalls.ShouldBeEmpty();

        scheduler.AdvanceBy(35);

        snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(1);
        snapshot.CurrentMidiNote.ShouldBe(60);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55, 60 });
        sound.StopCalls.Count.ShouldBe(1);
        sound.StopCalls[0].FadeOutDuration.ShouldBe(300u);
    }

    [Fact]
    public void ThreeSimultaneousNotes_OnSameTrack_RollLowToHighBeforeSustainingHighestNote()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(53), 0, 0);
        preview.ProcessEventForTesting(NoteOn(56), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 0);

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(3);
        snapshot.CurrentMidiNote.ShouldBe(53);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 53 });

        scheduler.AdvanceBy(35);

        snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(3);
        snapshot.CurrentMidiNote.ShouldBe(56);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 53, 56 });
        sound.StopCalls.Select(call => call.FadeOutDuration).ShouldBe(new uint[] { 300 });

        scheduler.AdvanceBy(35);

        snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(1);
        snapshot.CurrentMidiNote.ShouldBe(60);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 53, 56, 60 });
        sound.StopCalls.Select(call => call.FadeOutDuration).ShouldBe(new uint[] { 300, 300 });
    }

    [Fact]
    public void LowerSimultaneousNote_OnSameTrack_DoesNotInterruptHigherMidiNote()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Piano", sound);

        preview.ProcessEventForTesting(NoteOn(60), 0, 0);
        preview.ProcessEventForTesting(NoteOn(55), 0, 0);

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(1);
        snapshot.CurrentMidiNote.ShouldBe(60);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 60 });
        sound.StopCalls.ShouldBeEmpty();
    }

    [Fact]
    public void SameOnsetLowerNote_DoesNotResumeAfterHighestNoteOff()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(55), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 0);
        scheduler.AdvanceBy(35);
        preview.ProcessEventForTesting(NoteOff(60), 0, 120);
        preview.ProcessEventForTesting(NoteOff(55), 0, 120);

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(0);
        snapshot.CurrentMidiNote.ShouldBeNull();
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55, 60 });
        sound.StopCalls.Select(call => call.FadeOutDuration).ShouldBe(new uint[] { 300, 300 });
    }

    [Fact]
    public void SameOnsetRoll_DoesNotFireAfterStop()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Piano", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(55), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 0);
        preview.Stop();
        scheduler.AdvanceBy(35);

        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55 });
        preview.GetTrackSnapshots()[0].HeldNoteCount.ShouldBe(0);
    }

    [Fact]
    public void SameOnsetRoll_DoesNotFireAfterDispose()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Piano", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(55), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 0);
        preview.Dispose();
        scheduler.AdvanceBy(35);

        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55 });
    }

    [Fact]
    public void SameOnsetRoll_HighestNoteOffBeforeRollKeepsLowerHeldNote()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(55), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 0);
        preview.ProcessEventForTesting(NoteOff(60), 0, 120);
        scheduler.AdvanceBy(35);

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(1);
        snapshot.CurrentMidiNote.ShouldBe(55);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55 });
    }

    [Fact]
    public void SameOnsetRoll_LowerNoteOffBeforeRollStartsHighestImmediately()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Clarinet", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(55), 0, 0, 0.0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 0, 0.0);
        preview.ProcessEventForTesting(NoteOff(55), 0, 10, 0.01);
        scheduler.AdvanceBy(35);

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(1);
        snapshot.CurrentMidiNote.ShouldBe(60);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55, 60 });
        sound.StopCalls.Single().FadeOutDuration.ShouldBe(300u);
    }

    [Fact]
    public void LaterNote_OnSameTrack_CancelsPendingSameOnsetRoll()
    {
        var sound = new FakeSoundPlayer();
        var scheduler = new ManualPreviewScheduler();
        var preview = CreateLoadedPreview("Piano", sound, scheduler: scheduler);

        preview.ProcessEventForTesting(NoteOn(55), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 0);
        preview.ProcessEventForTesting(NoteOn(64), 0, 10);
        scheduler.AdvanceBy(35);

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.CurrentMidiNote.ShouldBe(64);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55, 64 });
    }

    [Fact]
    public void LaterNote_OnSameTrack_InterruptsEarlierHeldNote()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Clarinet", sound);

        preview.ProcessEventForTesting(NoteOn(55), 0, 0, 0.0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 10, 0.4);

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(1);
        snapshot.CurrentMidiNote.ShouldBe(60);
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55, 60 });
        sound.StopCalls.Count.ShouldBe(1);
        sound.StopCalls[0].FadeOutDuration.ShouldBe(400u);
    }

    [Fact]
    public void LaterNoteOff_DoesNotResumeEarlierHeldNote()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Clarinet", sound);

        preview.ProcessEventForTesting(NoteOn(55), 0, 0, 0.0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 10, 0.4);
        preview.ProcessEventForTesting(NoteOff(60), 0, 20, 0.9);

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(0);
        snapshot.CurrentMidiNote.ShouldBeNull();
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 55, 60 });
        sound.StopCalls.Count.ShouldBe(2);
        sound.StopCalls.Select(call => call.FadeOutDuration).ShouldBe(new uint[] { 400, 500 });
    }

    [Fact]
    public void LaterLowerNote_OnSameTrack_InterruptsEarlierHigherWithoutResuming()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Trumpet", sound);

        preview.ProcessEventForTesting(NoteOn(78), 0, 256, 0.5);
        preview.ProcessEventForTesting(NoteOn(59), 0, 384, 0.75);
        preview.ProcessEventForTesting(NoteOff(59), 0, 447, 0.873);
        preview.ProcessEventForTesting(NoteOff(78), 0, 640, 1.25);

        var snapshot = preview.GetTrackSnapshots()[0];
        snapshot.HeldNoteCount.ShouldBe(0);
        snapshot.CurrentMidiNote.ShouldBeNull();
        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 78, 59 });
        sound.StopCalls.Select(call => call.FadeOutDuration).ShouldBe(new uint[] { 300, 300 });
    }

    [Fact]
    public void SmashMenuTrumpetOpeningPattern_DoesNotRestartInterruptedSustainedNote()
    {
        var file = CreateEditableFile(
            CreateTrack("Trumpet",
                Timed(NoteOn(78), 256),
                Timed(NoteOn(59), 384),
                Timed(NoteOff(59), 447),
                Timed(NoteOff(78), 640)));
        var sound = new FakeSoundPlayer();
        var preview = CreatePreview(soundPlayer: sound);

        preview.Load(file, preservePosition: false);
        ReplaySnapshots(preview, 0);

        sound.PlayCalls.Select(call => call.Request.MidiNote).ShouldBe(new[] { 78, 59 });
    }

    [Fact]
    public void ProgramChange_DoesNotOverrideNamedNonGuitarTrack()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("Piano", sound, GuitarToneMode.Standard);

        preview.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)10), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 1);

        sound.PlayCalls.Single().Request.InstrumentId.ShouldBe(2u);
    }

    [Fact]
    public void ProgramChange_DoesNotResolveUnnamedTrackWithoutDefault()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview(string.Empty, sound, defaultInstrumentId: 0);

        preview.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)56), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 1);

        sound.PlayCalls.ShouldBeEmpty();
    }

    [Fact]
    public void ProgramChange_DefaultInstrumentSuppliesBaseForUnnamedTrack()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview(string.Empty, sound, defaultInstrumentId: 15);

        preview.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 1);

        sound.PlayCalls.Single().Request.InstrumentId.ShouldBe(15u);
    }

    [Fact]
    public void GuitarToneMode_StandardTracksGuitarTonePerChannel()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("ElectricGuitarOverdriven", sound, GuitarToneMode.Standard);

        preview.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)24) { Channel = (FourBitNumber)0 }, 0, 0);
        preview.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)30) { Channel = (FourBitNumber)1 }, 0, 0);
        preview.ProcessEventForTesting(NoteOn(60, channel: 0), 0, 1);
        preview.ProcessEventForTesting(NoteOn(64, channel: 1), 0, 2);

        sound.PlayCalls.Select(call => call.Request.InstrumentId).ShouldBe(new uint[] { 25, 27 });
    }

    [Fact]
    public void ProgramChange_NonGuitarProgramDoesNotResetGuitarTone()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview("ElectricGuitarOverdriven", sound, GuitarToneMode.Standard);

        preview.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 1);
        preview.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)56), 0, 2);
        preview.ProcessEventForTesting(NoteOn(64), 0, 3);

        sound.PlayCalls.Select(call => call.Request.InstrumentId).ShouldBe(new uint[] { 25, 25 });
    }

    [Fact]
    public void ForceDefaultInstrument_OverridesTrackNameAndProgramTone()
    {
        var sound = new FakeSoundPlayer();
        var preview = CreateLoadedPreview(
            "Piano",
            sound,
            settings: new FakePreviewSettings
            {
                DefaultInstrumentId = 15,
                ForceDefaultInstrument = true,
            });

        preview.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60), 0, 1);

        sound.PlayCalls.Single().Request.InstrumentId.ShouldBe(15u);
    }

    [Theory]
    [InlineData(GuitarToneMode.Off, 0, 24)]
    [InlineData(GuitarToneMode.Standard, 0, 25)]
    [InlineData(GuitarToneMode.Simple, 1, 25)]
    [InlineData(GuitarToneMode.OverrideByTrack, 0, 27)]
    public void GuitarToneMode_ControlsGuitarProgramChanges(GuitarToneMode mode, int noteChannel, uint expectedInstrument)
    {
        var sound = new FakeSoundPlayer();
        var settings = new FakePreviewSettings { GuitarToneMode = mode };
        settings.TrackStatus[0].Tone = 3;
        var preview = CreateLoadedPreview("ElectricGuitarOverdriven", sound, settings: settings);

        preview.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0, 0);
        preview.ProcessEventForTesting(NoteOn(60, noteChannel), 0, 1);

        sound.PlayCalls.Single().Request.InstrumentId.ShouldBe(expectedInstrument);
    }

    [Fact]
    public void ProgramElectricGuitarMode_AppliesOnlyToProgramElectricGuitarTracks()
    {
        var programTrackSound = new FakeSoundPlayer();
        var regularTrackSound = new FakeSoundPlayer();

        var programTrack = CreateLoadedPreview("ProgramElectricGuitar", programTrackSound, GuitarToneMode.ProgramElectricGuitarMode);
        var regularTrack = CreateLoadedPreview("ElectricGuitarOverdriven", regularTrackSound, GuitarToneMode.ProgramElectricGuitarMode);

        programTrack.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0, 0);
        programTrack.ProcessEventForTesting(NoteOn(60), 0, 1);
        regularTrack.ProcessEventForTesting(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0, 0);
        regularTrack.ProcessEventForTesting(NoteOn(60), 0, 1);

        programTrackSound.PlayCalls.Single().Request.InstrumentId.ShouldBe(25u);
        regularTrackSound.PlayCalls.Single().Request.InstrumentId.ShouldBe(24u);
    }

    private static void AddSchedulerOrder(List<int> order, int value)
    {
        lock (order)
            order.Add(value);
    }

    private static void PlayAndReleaseNote(
        MidiEditorPlaybackPreview preview,
        int trackIndex,
        Note note,
        TempoMap tempoMap)
    {
        var channel = (byte)note.Channel;
        var noteNumber = (byte)note.NoteNumber;
        preview.ProcessEventForTesting(NoteOn(noteNumber, channel), trackIndex, note.Time, GetTimeSeconds(note.Time, tempoMap));
        preview.ProcessEventForTesting(NoteOff(noteNumber, channel), trackIndex, note.EndTime, GetTimeSeconds(note.EndTime, tempoMap));
    }

    private static uint GetDurationMs(Note note, TempoMap tempoMap)
        => (uint)Math.Round((GetTimeSeconds(note.EndTime, tempoMap) - GetTimeSeconds(note.Time, tempoMap)) * 1000.0);

    private static uint GetExpectedDynamicReleaseFadeMs(Note note, TempoMap tempoMap)
        => Math.Clamp(
            GetDurationMs(note, tempoMap),
            MidiEditorPreviewReleasePolicy.MinimumDynamicReleaseFadeMs,
            MidiEditorPreviewReleasePolicy.MaximumDynamicReleaseFadeMs);

    private static double GetTimeSeconds(long time, TempoMap tempoMap)
        => TimeConverter.ConvertTo<MetricTimeSpan>(time, tempoMap).TotalMicroseconds / 1_000_000.0;

    private static MidiEditorPlaybackPreview CreateLoadedPreview(
        string trackName,
        FakeSoundPlayer soundPlayer,
        GuitarToneMode guitarToneMode = GuitarToneMode.Off,
        uint defaultInstrumentId = 2,
        Func<int, bool>? trackVisibilityProvider = null,
        FakePreviewSettings? settings = null,
        IMidiEditorPreviewScheduler? scheduler = null,
        IMidiEditorPreviewCompensationProvider? compensationProvider = null)
    {
        settings ??= new FakePreviewSettings
        {
            GuitarToneMode = guitarToneMode,
            DefaultInstrumentId = defaultInstrumentId,
        };

        var preview = CreatePreview(settings, soundPlayer, defaultInstrumentId, trackVisibilityProvider, scheduler, compensationProvider);
        preview.Load(CreateEditableFile(
            CreateTrack(trackName,
                Timed(new NoteOnEvent((SevenBitNumber)(byte)60, (SevenBitNumber)(byte)100), 0),
                Timed(new NoteOffEvent((SevenBitNumber)(byte)60, (SevenBitNumber)(byte)0), 120))), preservePosition: false);
        return preview;
    }

    private static MidiEditorPlaybackPreview CreatePreview(
        FakePreviewSettings? settings = null,
        FakeSoundPlayer? soundPlayer = null,
        uint defaultInstrumentId = 2,
        Func<int, bool>? trackVisibilityProvider = null,
        IMidiEditorPreviewScheduler? scheduler = null,
        IMidiEditorPreviewCompensationProvider? compensationProvider = null)
    {
        settings ??= new FakePreviewSettings { DefaultInstrumentId = defaultInstrumentId };
        return new MidiEditorPlaybackPreview(
            settings,
            new FakeInstrumentCatalog(),
            soundPlayer ?? new FakeSoundPlayer(),
            trackVisibilityProvider,
            scheduler,
            compensationProvider);
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(string trackName, params TimedEvent[] events)
    {
        var chunk = string.IsNullOrEmpty(trackName)
            ? new TrackChunk()
            : new TrackChunk(new SequenceTrackNameEvent(trackName));

        using var manager = chunk.ManageTimedEvents();
        foreach (var timedEvent in events)
            manager.Objects.Add(timedEvent);

        return chunk;
    }

    private static TimedEvent Timed(ChannelEvent midiEvent, long time, int channel = 0)
    {
        midiEvent.Channel = (FourBitNumber)(byte)channel;
        return new TimedEvent(midiEvent, time);
    }

    private static NoteOnEvent NoteOn(int noteNumber, int channel = 0)
        => new((SevenBitNumber)(byte)noteNumber, (SevenBitNumber)(byte)100)
        {
            Channel = (FourBitNumber)(byte)channel,
        };

    private static NoteOffEvent NoteOff(int noteNumber, int channel = 0)
        => new((SevenBitNumber)(byte)noteNumber, (SevenBitNumber)(byte)0)
        {
            Channel = (FourBitNumber)(byte)channel,
        };

    private static void ReplaySnapshots(MidiEditorPlaybackPreview preview, params int[] trackIndexes)
    {
        var allowedTracks = trackIndexes.ToHashSet();
        foreach (var snapshot in preview.EventSnapshots
            .Where(snapshot => allowedTracks.Contains(snapshot.TrackIndex))
            .OrderBy(snapshot => snapshot.Time)
            .ThenBy(snapshot => snapshot.TrackIndex)
            .ThenBy(snapshot => snapshot.EventValue))
        {
            preview.ProcessEventForTesting(CreateEvent(snapshot), snapshot.TrackIndex, snapshot.Time);
        }
    }

    private static MidiEvent CreateEvent(MidiEditorPlaybackPreview.EventSnapshot snapshot)
    {
        if (snapshot.EventType == MidiEventType.ProgramChange.ToString())
        {
            return new ProgramChangeEvent((SevenBitNumber)(byte)snapshot.ProgramNumber!.Value)
            {
                Channel = (FourBitNumber)(byte)snapshot.Channel,
            };
        }

        if (snapshot.EventType == MidiEventType.NoteOn.ToString())
            return NoteOn(snapshot.EventValue, snapshot.Channel);

        if (snapshot.EventType == MidiEventType.NoteOff.ToString())
            return NoteOff(snapshot.EventValue, snapshot.Channel);

        throw new InvalidOperationException($"Unsupported preview event snapshot: {snapshot.EventType}");
    }

    private sealed class FakePreviewSettings : IMidiEditorPreviewSettings
    {
        public float PlaySpeed { get; set; } = 1f;
        public int TransposeGlobal { get; set; }
        public bool AdaptNotesOOR { get; set; } = true;
        public uint DefaultInstrumentId { get; set; } = 2;
        public bool ForceDefaultInstrument { get; set; }
        public GuitarToneMode GuitarToneMode { get; set; } = GuitarToneMode.Off;
        public AntiStackType AntiStackType { get; set; } = AntiStackType.Off;
        public IReadOnlyList<TrackStatus> TrackStatus { get; } = Enumerable.Range(0, 100).Select(_ => new TrackStatus()).ToArray();
    }

    private sealed class FakeInstrumentCatalog : IMidiEditorPreviewInstrumentCatalog
    {
        private readonly Dictionary<byte, uint> programInstruments = new()
        {
            [24] = 25,
            [25] = 25,
            [26] = 25,
            [27] = 25,
            [28] = 26,
            [29] = 24,
            [30] = 27,
            [31] = 28,
            [56] = 15,
        };

        public uint? ResolveTrackInstrument(string trackName, uint defaultInstrumentId, bool forceDefaultInstrument)
        {
            if (forceDefaultInstrument && defaultInstrumentId > 0)
                return defaultInstrumentId;

            var defaultInstrument = defaultInstrumentId > 0 ? (ushort?)defaultInstrumentId : null;
            return TrackInfo.GetInstrumentIdByName(trackName, defaultInstrument);
        }

        public bool TryResolveProgramInstrument(SevenBitNumber program, out uint instrumentId)
            => programInstruments.TryGetValue((byte)program, out instrumentId);

        public bool IsGuitar(uint instrumentId)
            => instrumentId is >= 24 and <= 28;
    }

    private sealed class FakeCompensationProvider : IMidiEditorPreviewCompensationProvider
    {
        private readonly Dictionary<(uint instrumentId, int gameNote), int> delays = new();

        public int DefaultDelayMs { get; set; }

        public void SetDelay(uint instrumentId, int gameNote, int delayMs)
            => delays[(instrumentId, gameNote)] = delayMs;

        public int GetCompensationMs(uint instrumentId, int gameNote)
            => delays.TryGetValue((instrumentId, gameNote), out var delayMs)
                ? delayMs
                : DefaultDelayMs;
    }

    private sealed class ManualPreviewScheduler : IMidiEditorPreviewScheduler
    {
        private readonly List<ScheduledAction> actions = new();
        private long nowMs;
        private long nextSequence;

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var action = new ScheduledAction(
                nowMs + Math.Max(0, (long)Math.Round(delay.TotalMilliseconds)),
                nextSequence++,
                callback);
            actions.Add(action);
            return action;
        }

        public void AdvanceBy(int milliseconds)
        {
            var targetMs = nowMs + milliseconds;
            while (true)
            {
                var action = actions
                    .Where(item => !item.Cancelled && item.DueMs <= targetMs)
                    .OrderBy(item => item.DueMs)
                    .ThenBy(item => item.Sequence)
                    .FirstOrDefault();
                if (action == null)
                    break;

                nowMs = action.DueMs;
                action.Cancelled = true;
                action.Callback();
            }

            nowMs = targetMs;
        }

        private sealed class ScheduledAction(long dueMs, long sequence, Action callback) : IDisposable
        {
            public long DueMs { get; } = dueMs;
            public long Sequence { get; } = sequence;
            public Action Callback { get; } = callback;
            public bool Cancelled { get; set; }

            public void Dispose()
                => Cancelled = true;
        }
    }

    private sealed class FakeSoundPlayer : IMidiEditorPreviewSoundPlayer
    {
        private nint nextSound = 1;

        public List<PlayCall> PlayCalls { get; } = new();
        public List<StopCall> StopCalls { get; } = new();

        public nint Play(PreviewSoundRequest request, out string? statusMessage)
        {
            statusMessage = null;
            var sound = nextSound++;
            PlayCalls.Add(new PlayCall(sound, request));
            return sound;
        }

        public void Stop(nint sound, uint fadeOutDuration)
        {
            if (sound == 0)
                return;

            StopCalls.Add(new StopCall(sound, fadeOutDuration));
        }
    }

    private sealed record PlayCall(nint Sound, PreviewSoundRequest Request);
    private sealed record StopCall(nint Sound, uint FadeOutDuration);
}
