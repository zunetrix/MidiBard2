using Melanchall.DryWetMidi.Common;

using MidiBard.Control.MidiControl;

namespace MidiBard.Tests.Control.MidiControl;

public class GuitarToneProgramResolverTests
{
    [Theory]
    [InlineData(29, 0, 24)]
    [InlineData(27, 1, 25)]
    [InlineData(28, 2, 26)]
    [InlineData(30, 3, 27)]
    [InlineData(31, 4, 28)]
    public void TryResolveToneFromProgram_AcceptsKnownGuitarTonePrograms(
        byte program,
        int expectedTone,
        uint expectedInstrumentId)
    {
        GuitarToneProgramResolver.TryResolveToneFromProgram((SevenBitNumber)program, out var tone).ShouldBeTrue();
        tone.ShouldBe(expectedTone);

        GuitarToneProgramResolver.TryResolveInstrumentFromProgram((SevenBitNumber)program, out var instrumentId).ShouldBeTrue();
        instrumentId.ShouldBe(expectedInstrumentId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(56)]
    [InlineData(66)]
    public void TryResolveToneFromProgram_RejectsNonTonePrograms(byte program)
    {
        GuitarToneProgramResolver.TryResolveToneFromProgram((SevenBitNumber)program, out _).ShouldBeFalse();
        GuitarToneProgramResolver.TryResolveInstrumentFromProgram((SevenBitNumber)program, out _).ShouldBeFalse();
    }
}
