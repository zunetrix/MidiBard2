using MidiBard.Control.MidiControl.Editing;

namespace MidiBard.Tests.Control.MidiControl.Editing;

public class MidiForgeNotePrimitivesTests
{
    // --- ParseNoteText happy path ---

    [Fact]
    public void ParseNoteText_WhiteC3_Returns48()
        => MidiForgeNotePrimitives.ParseNoteText("C3").ShouldBe(48);

    [Theory]
    [InlineData("C#3", 49)]
    [InlineData("Db3", 49)]
    [InlineData("D#4", 63)]
    [InlineData("Eb4", 63)]
    [InlineData("F#2", 42)]
    [InlineData("Gb2", 42)]
    [InlineData("G#5", 80)]
    [InlineData("Ab5", 80)]
    [InlineData("A#0", 22)]
    [InlineData("Bb0", 22)]
    [InlineData("B7", 107)]
    [InlineData("Cb8", 107)] // Cb8 is enharmonic to B7
    [InlineData("B#4", 72)]  // B#4 is enharmonic to C5
    public void ParseNoteText_Enharmonics_ReturnsExpected(string input, int expected)
        => MidiForgeNotePrimitives.ParseNoteText(input).ShouldBe(expected);

    [Fact]
    public void ParseNoteText_LowercaseLetters_ParseCorrectly()
        => MidiForgeNotePrimitives.ParseNoteText("c#3").ShouldBe(49);

    [Fact]
    public void ParseNoteText_MixedCase_ParseCorrectly()
        => MidiForgeNotePrimitives.ParseNoteText("c#4").ShouldBe(61);

    [Fact]
    public void ParseNoteText_LeadingTrailingWhitespace_ParseCorrectly()
        => MidiForgeNotePrimitives.ParseNoteText("  Db5  ").ShouldBe(73);

    // --- TryParseNoteText ---

    [Fact]
    public void TryParseNoteText_ValidNoteText_ReturnsTrue()
    {
        var result = MidiForgeNotePrimitives.TryParseNoteText("F#3", out var note);
        result.ShouldBeTrue();
        note.ShouldBe(54);
    }

    [Fact]
    public void TryParseNoteText_IntegerString_ReturnsFalse()
    {
        var result = MidiForgeNotePrimitives.TryParseNoteText("60", out var note);
        result.ShouldBeFalse();
        note.ShouldBe(-1);
    }

    [Fact]
    public void TryParseNoteText_NegativeInteger_ReturnsFalse()
    {
        var result = MidiForgeNotePrimitives.TryParseNoteText("-5", out var note);
        result.ShouldBeFalse();
        note.ShouldBe(-1);
    }

    [Fact]
    public void TryParseNoteText_Garbage_ReturnsFalse()
    {
        var result = MidiForgeNotePrimitives.TryParseNoteText("xyz", out var note);
        result.ShouldBeFalse();
        note.ShouldBe(-1);
    }

    [Fact]
    public void TryParseNoteText_InvalidAccidental_ReturnsFalse()
    {
        var result = MidiForgeNotePrimitives.TryParseNoteText("C##3", out var note);
        result.ShouldBeFalse();
        note.ShouldBe(-1);
    }

    [Fact]
    public void TryParseNoteText_NoOctave_ReturnsFalse()
    {
        var result = MidiForgeNotePrimitives.TryParseNoteText("C", out var note);
        result.ShouldBeFalse();
        note.ShouldBe(-1);
    }

    [Fact]
    public void TryParseNoteText_NegativeOctave_ReturnsFalse()
    {
        var result = MidiForgeNotePrimitives.TryParseNoteText("C-5", out var note);
        result.ShouldBeFalse();
        note.ShouldBe(-1);
    }

    [Fact]
    public void TryParseNoteText_OctaveOutOfRange_BelowZero_ReturnsFalse()
    {
        var result = MidiForgeNotePrimitives.TryParseNoteText("C-1", out var note);
        result.ShouldBeFalse();
        note.ShouldBe(-1);
    }

    [Fact]
    public void TryParseNoteText_OctaveOutOfRange_Above9_ReturnsFalse()
    {
        var result = MidiForgeNotePrimitives.TryParseNoteText("C10", out var note);
        result.ShouldBeFalse();
        note.ShouldBe(-1);
    }

    [Fact]
    public void TryParseNoteText_NoteMidiNumberAbove127_ReturnsFalse()
    {
        // Db10 would be 131, well above 127
        var result = MidiForgeNotePrimitives.TryParseNoteText("Db10", out var note);
        result.ShouldBeFalse();
        note.ShouldBe(-1);
    }

    [Fact]
    public void TryParseNoteText_B9Above127_ReturnsFalse()
    {
        // B9 = (9+1)*12+11 = 131 > 127
        var result = MidiForgeNotePrimitives.TryParseNoteText("B9", out var note);
        result.ShouldBeFalse();
        note.ShouldBe(-1);
    }

    // --- ResolveNoteBoundary (int-first, then note-text) ---

    [Fact]
    public void ResolveNoteBoundary_PureInteger_ReturnsThatInteger()
    {
        MidiForgeNotePrimitives.ResolveNoteBoundary("48").ShouldBe(48);
        MidiForgeNotePrimitives.ResolveNoteBoundary("84").ShouldBe(84);
        MidiForgeNotePrimitives.ResolveNoteBoundary("0").ShouldBe(0);
        MidiForgeNotePrimitives.ResolveNoteBoundary("127").ShouldBe(127);
    }

    [Fact]
    public void ResolveNoteBoundary_NoteText_ReturnsMidiNumber()
    {
        MidiForgeNotePrimitives.ResolveNoteBoundary("C3").ShouldBe(48);
        MidiForgeNotePrimitives.ResolveNoteBoundary("C6").ShouldBe(84);
        MidiForgeNotePrimitives.ResolveNoteBoundary("Db5").ShouldBe(73);
        MidiForgeNotePrimitives.ResolveNoteBoundary("G#2").ShouldBe(44);
    }

    [Fact]
    public void ResolveNoteBoundary_WhitespaceAroundInt_ReturnsInt()
    {
        MidiForgeNotePrimitives.ResolveNoteBoundary("  60  ").ShouldBe(60);
    }

    [Fact]
    public void ResolveNoteBoundary_WhitespaceAroundNoteText_ReturnsMidiNumber()
    {
        MidiForgeNotePrimitives.ResolveNoteBoundary("  C#4  ").ShouldBe(61);
    }

    [Fact]
    public void ResolveNoteBoundary_NegativeInteger_ReturnsThatNegativeInteger()
    {
        MidiForgeNotePrimitives.ResolveNoteBoundary("-10").ShouldBe(-10);
        MidiForgeNotePrimitives.ResolveNoteBoundary("  -5  ").ShouldBe(-5);
    }

    [Fact]
    public void ResolveNoteBoundary_Garbage_ReturnsDefaultValue()
    {
        MidiForgeNotePrimitives.ResolveNoteBoundary("abc", fallback: 42).ShouldBe(42);
    }

    [Fact]
    public void ResolveNoteBoundary_EmptyString_ReturnsDefaultValue()
    {
        MidiForgeNotePrimitives.ResolveNoteBoundary("", fallback: 42).ShouldBe(42);
        MidiForgeNotePrimitives.ResolveNoteBoundary(null!, fallback: 42).ShouldBe(42);
    }

    [Fact]
    public void ResolveNoteBoundary_FallbackIsUsedWhenNothingParses()
    {
        MidiForgeNotePrimitives.ResolveNoteBoundary("C#3x", fallback: 99).ShouldBe(99);
    }

    // --- Edge cases for valid boundaries ---

    [Fact]
    public void ParseNoteText_Octave0_CNotes_ProduceCorrectValues()
    {
        MidiForgeNotePrimitives.ParseNoteText("C0").ShouldBe(12);
        MidiForgeNotePrimitives.ParseNoteText("B0").ShouldBe(23);
    }

    [Fact]
    public void ParseNoteText_Octave9_G9_Is127()
    {
        // G9 = (9+1)*12+7 = 127, the highest valid MIDI note
        MidiForgeNotePrimitives.ParseNoteText("G9").ShouldBe(127);
    }

    [Fact]
    public void ParseNoteText_C0_LessThanMidi0_IsClampedOrRejectedByTryParse()
    {
        // C-1 would be midi 0 but octave -1 is invalid
        var result = MidiForgeNotePrimitives.TryParseNoteText("C-1", out var note);
        result.ShouldBeFalse();
    }

    [Fact]
    public void ParseNoteText_AllNaturals_Octave4_Correct()
    {
        var expected = new Dictionary<string, int>
        {
            { "C4", 60 },
            { "D4", 62 },
            { "E4", 64 },
            { "F4", 65 },
            { "G4", 67 },
            { "A4", 69 },
            { "B4", 71 },
        };
        foreach (var (text, midi) in expected)
        {
            MidiForgeNotePrimitives.ParseNoteText(text).ShouldBe(midi,
                $"expected {midi} for {text}, got {MidiForgeNotePrimitives.ParseNoteText(text)}");
        }
    }

    [Fact]
    public void ParseNoteText_AllSharps_Octave4_Correct()
    {
        var expected = new Dictionary<string, int>
        {
            { "C#4", 61 },
            { "D#4", 63 },
            { "F#4", 66 },
            { "G#4", 68 },
            { "A#4", 70 },
        };
        foreach (var (text, midi) in expected)
        {
            MidiForgeNotePrimitives.ParseNoteText(text).ShouldBe(midi,
                $"expected {midi} for {text}, got {MidiForgeNotePrimitives.ParseNoteText(text)}");
        }
    }

    [Fact]
    public void ParseNoteText_AllFlats_Octave4_Correct()
    {
        var expected = new Dictionary<string, int>
        {
            { "Db4", 61 },
            { "Eb4", 63 },
            { "Gb4", 66 },
            { "Ab4", 68 },
            { "Bb4", 70 },
        };
        foreach (var (text, midi) in expected)
        {
            MidiForgeNotePrimitives.ParseNoteText(text).ShouldBe(midi,
                $"expected {midi} for {text}, got {MidiForgeNotePrimitives.ParseNoteText(text)}");
        }
    }

    // --- GetMidiNoteName ---

    [Fact]
    public void GetMidiNoteName_C3_ReturnsC3()
        => MidiForgeNotePrimitives.GetMidiNoteName(48).ShouldBe("C3");

    [Fact]
    public void GetMidiNoteName_C6_ReturnsC6()
        => MidiForgeNotePrimitives.GetMidiNoteName(84).ShouldBe("C6");

    [Fact]
    public void GetMidiNoteName_MiddleC_ReturnsC4()
        => MidiForgeNotePrimitives.GetMidiNoteName(60).ShouldBe("C4");

    [Fact]
    public void GetMidiNoteName_G9_ReturnsG9()
        => MidiForgeNotePrimitives.GetMidiNoteName(127).ShouldBe("G9");

    [Fact]
    public void GetMidiNoteName_C0_ReturnsC0()
        => MidiForgeNotePrimitives.GetMidiNoteName(12).ShouldBe("C0");

    [Fact]
    public void GetMidiNoteName_UseSharps()
    {
        MidiForgeNotePrimitives.GetMidiNoteName(61).ShouldBe("C#4");
        MidiForgeNotePrimitives.GetMidiNoteName(66).ShouldBe("F#4");
        MidiForgeNotePrimitives.GetMidiNoteName(68).ShouldBe("G#4");
    }

    [Fact]
    public void GetMidiNoteName_ClampsOutOfRange()
    {
        MidiForgeNotePrimitives.GetMidiNoteName(-1).ShouldBe(MidiForgeNotePrimitives.GetMidiNoteName(0));
        MidiForgeNotePrimitives.GetMidiNoteName(200).ShouldBe(MidiForgeNotePrimitives.GetMidiNoteName(127));
    }
}
