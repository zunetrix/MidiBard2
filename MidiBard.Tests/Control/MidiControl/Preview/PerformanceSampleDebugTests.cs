using System;
using System.Linq;

using FFXIVClientStructs.FFXIV.Client.Sound;

using MidiBard.Control.MidiControl.Preview;

namespace MidiBard.Tests.Control.MidiControl.Preview;

public class PerformanceSampleDebugTests
{
    [Theory]
    [InlineData("sound/instruments/047harp.scd", true)]
    [InlineData("sound\\instruments\\001grandpiano.scd", true)]
    [InlineData("sound/foot/foot/fs_grass_f_f_shoes.scd", false)]
    [InlineData("sound/battle/mon/3451.scd", false)]
    [InlineData("sound/instruments/not_a_performance_sample.scd", false)]
    [InlineData("", false)]
    public void IsPerformanceInstrumentPath_FiltersToKnownInstrumentSamples(string path, bool expected)
    {
        PerformanceSampleCatalog.IsPerformanceInstrumentPath(path).ShouldBe(expected);
    }

    [Fact]
    public void BuildSourceRows_GroupsByInstrumentKeepsNewestValidCapture()
    {
        var older = Entry(2, "sound/instruments/001grandpiano.scd", DateTimeOffset.UtcNow.AddMinutes(-5), soundNumber: 1, midiNote: 24);
        var newer = Entry(2, "sound\\instruments\\001grandpiano.scd", DateTimeOffset.UtcNow, soundNumber: 7, midiNote: 60);
        var invalidZeroInstrument = Entry(0, "sound/instruments/047harp.scd", DateTimeOffset.UtcNow);
        var invalidPath = Entry(1, "sound/foot/foot/fs_grass_f_f_shoes.scd", DateTimeOffset.UtcNow);

        var sourceRows = PerformanceSampleCatalog.BuildSourceRows(new[]
        {
            older,
            newer,
            invalidZeroInstrument,
            invalidPath,
        });

        sourceRows.ShouldContain("[2] = \"sound\\\\instruments\\\\001grandpiano.scd\"");
        sourceRows.ShouldContain("captured soundNumber=7, midiNote=60");
        sourceRows.ShouldNotContain("soundNumber=1");
        sourceRows.ShouldNotContain("[0]");
        sourceRows.ShouldNotContain("[1]");
        sourceRows.ShouldNotContain("fs_grass");
    }

    [Fact]
    public void ProbeStore_CapturesOnlyValidEntriesAndKeepsMaxEntries()
    {
        var store = new PerformanceSampleProbeStore(maxEntries: 2);

        store.Capture(Entry(0, "sound/instruments/047harp.scd", DateTimeOffset.UtcNow));
        store.Capture(Entry(1, "sound/foot/foot/fs_grass_f_f_shoes.scd", DateTimeOffset.UtcNow));
        store.Capture(Entry(1, "sound/instruments/047harp.scd", DateTimeOffset.UtcNow.AddSeconds(1)));
        store.Capture(Entry(2, "sound/instruments/001grandpiano.scd", DateTimeOffset.UtcNow.AddSeconds(2)));
        store.Capture(Entry(3, "sound/instruments/026steelguitar.scd", DateTimeOffset.UtcNow.AddSeconds(3)));

        store.Entries.Select(entry => entry.InstrumentId).ShouldBe(new uint[] { 2, 3 });

        store.Clear();

        store.Entries.ShouldBeEmpty();
    }

    private static PerformanceSampleProbeEntry Entry(
        uint instrumentId,
        string path,
        DateTimeOffset timestamp,
        uint soundNumber = 0,
        int midiNote = 24)
        => new(
            timestamp,
            instrumentId,
            path,
            1f,
            0,
            1f,
            0,
            soundNumber,
            false,
            SoundVolumeCategory.BypassVolumeRules,
            false,
            midiNote,
            false,
            false,
            false,
            false);
}
