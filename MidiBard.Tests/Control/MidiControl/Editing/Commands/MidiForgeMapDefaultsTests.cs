using MidiBard.Control.MidiControl.Editing;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class MidiForgeMapDefaultsTests
{
    [Fact]
    public void CreateDefaultSettings_SeedsBardForgeSourceMapsAndFullTransposePresets()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        MidiForgeMapDefaults.Normalize(settings);

        settings.DrumkitSourceMaps.Single(map => map.TrackName == "BassDrum").SourceNotes
            .ShouldBe(new[] { 35, 36, 41, 43, 45, 47, 48, 50 });
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "SnareDrum").SourceNotes
            .ShouldBe(new[] { 38, 40 });
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Cymbal").SourceNotes
            .ShouldBe(new[] { 49, 52, 55, 57 });
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Bongo").SourceNotes
            .ShouldBe(new[] { 60, 61 });
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Timpani").SourceNotes
            .ShouldBeEmpty();

        foreach (var preset in Enum.GetValues<MidiForgeDrumTransposePreset>())
        {
            var targets = MidiForgeMapDefaults.GetEffectiveDrumTransposeTargets(settings, preset);
            targets.Count.ShouldBe(61);
            targets.Single(target => target.InputNote == 37).OutputNote.ShouldBe(37);
            targets.Single(target => target.InputNote == 42).OutputNote.ShouldBe(42);
            targets.Single(target => target.InputNote == 46).OutputNote.ShouldBe(46);
        }

        MidiForgeMapDefaults.GetEffectiveDrumTransposeTargets(settings, MidiForgeDrumTransposePreset.Default)
            .Single(target => target.InputNote == 36)
            .OutputNote
            .ShouldBe(51);
        MidiForgeMapDefaults.GetEffectiveDrumTransposeTargets(settings, MidiForgeDrumTransposePreset.BardForge2)
            .Single(target => target.InputNote == 36)
            .OutputNote
            .ShouldBe(55);
        MidiForgeMapDefaults.GetEffectiveDrumTransposeTargets(settings, MidiForgeDrumTransposePreset.MogAmp)
            .Single(target => target.InputNote == 36)
            .OutputNote
            .ShouldBe(57);
    }

    [Fact]
    public void DefaultInstrumentMapIncludesBardForgeAliases()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        var provider = new ConfigurationEditorMidiMapProvider(settings);

        provider.TryResolveInstrumentTrackName((Melanchall.DryWetMidi.Common.SevenBitNumber)52, out var panpipes)
            .ShouldBeTrue();
        panpipes.ShouldBe("Panpipes");

        provider.TryResolveInstrumentTrackName((Melanchall.DryWetMidi.Common.SevenBitNumber)24, out var guitar)
            .ShouldBeTrue();
        guitar.ShouldBe("ElectricGuitarClean");
    }

    [Fact]
    public void EffectiveTransposeTargetsIncludeUserAddedSourceNotesAsIdentityMappings()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Bongo").SourceNotes.Add(64);

        var targets = MidiForgeMapDefaults.GetEffectiveDrumTransposeTargets(
            settings,
            MidiForgeDrumTransposePreset.Default);

        var lowConga = targets.Single(target => target.InputNote == 64);
        lowConga.Category.ShouldBe("Bongo");
        lowConga.DrumkitInstrument.ShouldBe("Low Conga");
        lowConga.OutputNote.ShouldBe(64);
    }

    [Fact]
    public void NormalizeClampsAndDeduplicatesMaps()
    {
        var settings = new MidiForgeMapSettings
        {
            InstrumentMaps =
            [
                new MidiForgeInstrumentMapSettings
                {
                    InstrumentId = 999,
                    InstrumentName = "Custom",
                    TrackName = "Custom",
                    MidiPrograms = [-1, 52, 52, 200],
                },
            ],
            DrumkitSourceMaps =
            [
                new MidiForgeDrumInstrumentMapSettings { TrackName = "Bongo", SourceNotes = [64, 64, 200] },
                new MidiForgeDrumInstrumentMapSettings { TrackName = "Cymbal", SourceNotes = [64, -10] },
            ],
        };

        MidiForgeMapDefaults.Normalize(settings);

        settings.InstrumentMaps.Single(map => map.InstrumentId == 999).MidiPrograms
            .ShouldBe(new[] { 0, 52, 127 });
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Bongo").SourceNotes
            .ShouldBe(new[] { 64, 127 });
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Cymbal").SourceNotes
            .ShouldBe(new[] { 0 });
        settings.DrumkitSourceMaps.ShouldContain(map => map.TrackName == "BassDrum");
        settings.DrumTransposePresets.ShouldContain(preset => preset.Preset == MidiForgeDrumTransposePreset.Default);
    }
}
