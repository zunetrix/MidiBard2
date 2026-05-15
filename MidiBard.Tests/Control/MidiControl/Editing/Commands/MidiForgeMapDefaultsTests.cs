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
    public void ConfigurationMidiForgeMapsReplacesDefaultListsDuringJsonLoad()
    {
        var field = typeof(global::MidiBard.Configuration).GetField(nameof(global::MidiBard.Configuration.MidiForgeMaps));

        field.ShouldNotBeNull();
        var attribute = field!.GetCustomAttributes(typeof(Newtonsoft.Json.JsonPropertyAttribute), false)
            .OfType<Newtonsoft.Json.JsonPropertyAttribute>()
            .SingleOrDefault();

        attribute.ShouldNotBeNull();
        attribute!.ObjectCreationHandling.ShouldBe(Newtonsoft.Json.ObjectCreationHandling.Replace);
    }

    [Fact]
    public void DefaultInstrumentMapKeepsBardForgeIdsSeparateFromRuntimeInstrumentIndexes()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        MidiForgeMapDefaults.Normalize(settings);

        settings.InstrumentMaps.Single(map => map.InstrumentId == 2).TrackName.ShouldBe("Fiddle");
        settings.InstrumentMaps.Single(map => map.InstrumentId == 4).TrackName.ShouldBe("Fife");
        settings.InstrumentMaps.Single(map => map.InstrumentId == 8).TrackName.ShouldBe("Clarinet");
        settings.InstrumentMaps.Single(map => map.InstrumentId == 7).TrackName.ShouldBe("Panpipes");

        var targets = MidiForgeMapOptionCatalog.BuildInstrumentTargets(settings);
        targets.Single(target => target.InstrumentId == 2).TrackName.ShouldBe("Fiddle");
        targets.Single(target => target.InstrumentId == 8).TrackName.ShouldBe("Clarinet");
    }

    [Fact]
    public void MapOptionCatalogReportsProgramAndDrumNoteOwnershipByTarget()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        MidiForgeMapDefaults.Normalize(settings);

        var piano = settings.InstrumentMaps.Single(map => map.TrackName == "Piano");
        var harp = settings.InstrumentMaps.Single(map => map.TrackName == "Harp");
        MidiForgeMapOptionCatalog.IsProgramAssignedToAnotherTarget(settings, piano, 0).ShouldBeFalse();
        MidiForgeMapOptionCatalog.IsProgramAssignedToAnotherTarget(settings, piano, 16).ShouldBeTrue();
        MidiForgeMapOptionCatalog.IsProgramAssignedToAnotherTarget(settings, harp, 16).ShouldBeFalse();
        MidiForgeMapOptionCatalog.ShouldDisableProgramOption(settings, piano, 16).ShouldBeTrue();
        MidiForgeMapOptionCatalog.ShouldDisableProgramOption(settings, harp, 16).ShouldBeFalse();

        var bongo = settings.DrumkitSourceMaps.Single(map => map.TrackName == "Bongo");
        var cymbal = settings.DrumkitSourceMaps.Single(map => map.TrackName == "Cymbal");
        MidiForgeMapOptionCatalog.IsDrumNoteAssignedToAnotherTarget(settings, bongo, 60).ShouldBeFalse();
        MidiForgeMapOptionCatalog.IsDrumNoteAssignedToAnotherTarget(settings, bongo, 49).ShouldBeTrue();
        MidiForgeMapOptionCatalog.IsDrumNoteAssignedToAnotherTarget(settings, cymbal, 49).ShouldBeFalse();
        MidiForgeMapOptionCatalog.ShouldDisableDrumNoteOption(settings, bongo, 49).ShouldBeTrue();
        MidiForgeMapOptionCatalog.ShouldDisableDrumNoteOption(settings, cymbal, 49).ShouldBeFalse();
    }

    [Fact]
    public void NormalizeUsesStableDrumkitSourceIdsWhenTrackNamesAreEdited()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        var bassDrum = settings.DrumkitSourceMaps.Single(map => map.TrackName == "BassDrum");
        bassDrum.TrackName = "Kick";

        MidiForgeMapDefaults.Normalize(settings);

        settings.DrumkitSourceMaps.Count(map => map.InstrumentId == 23).ShouldBe(1);
        settings.DrumkitSourceMaps.Single(map => map.InstrumentId == 23).TrackName.ShouldBe("BassDrum");
    }

    [Fact]
    public void NormalizeRestoresKnownInstrumentOutputTrackNames()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        settings.InstrumentMaps.Single(map => map.TrackName == "Piano").TrackName = "MappedPiano";
        settings.InstrumentMaps.Single(map => map.TrackName == "Lute").InstrumentName = "Custom Lute";
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Cymbal").TrackName = "Crash";

        MidiForgeMapDefaults.Normalize(settings);

        settings.InstrumentMaps.Single(map => map.InstrumentId == 0).TrackName.ShouldBe("Piano");
        settings.InstrumentMaps.Single(map => map.InstrumentId == 3).InstrumentName.ShouldBe("Lute");
        settings.DrumkitSourceMaps.Single(map => map.InstrumentId == 29).TrackName.ShouldBe("Cymbal");
    }

    [Fact]
    public void MoveInstrumentTargetChangesRelativeOrderAndPreservesMappings()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        MidiForgeMapDefaults.Normalize(settings);
        var fifePrograms = settings.InstrumentMaps.Single(map => map.TrackName == "Fife").MidiPrograms.ToArray();

        MidiForgeMapOptionCatalog.MoveInstrumentTarget(settings, 4, 1).ShouldBeTrue();

        var targets = MidiForgeMapOptionCatalog.BuildInstrumentTargets(settings);
        var fluteIndex = Array.FindIndex(targets.ToArray(), target => target.TrackName == "Flute");
        var fifeIndex = Array.FindIndex(targets.ToArray(), target => target.TrackName == "Fife");

        fifeIndex.ShouldBe(fluteIndex + 1);
        settings.InstrumentMaps.Single(map => map.TrackName == "Fife").MidiPrograms.ShouldBe(fifePrograms);

        MidiForgeMapOptionCatalog.MoveInstrumentTarget(settings, 4, -1).ShouldBeTrue();
        MidiForgeMapOptionCatalog.BuildInstrumentTargets(settings).First().TrackName.ShouldBe("Fife");
    }

    [Fact]
    public void InstrumentRangeLabelsAreUiOnlyMetadataForKnownBardForgeTargets()
    {
        MidiForgeMapOptionCatalog.TryGetInstrumentRangeLabel(19, out var cleanRange).ShouldBeTrue();
        cleanRange.ShouldBe("C2-C5");

        MidiForgeMapOptionCatalog.TryGetInstrumentRangeLabel(22, out var specialRange).ShouldBeTrue();
        specialRange.ShouldContain("G#3-E5");

        MidiForgeMapOptionCatalog.TryGetInstrumentRangeLabel(23, out _).ShouldBeFalse();
    }

    [Fact]
    public void DefaultInstrumentMapIncludesTrackNameAliases()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        var provider = new ConfigurationEditorMidiMapProvider(settings);

        provider.TryResolveInstrumentTrackNameAlias("Clean", out var clean).ShouldBeTrue();
        clean.ShouldBe("ElectricGuitarClean");

        provider.TryResolveInstrumentTrackNameAlias("power chords", out var powerChords).ShouldBeTrue();
        powerChords.ShouldBe("ElectricGuitarPowerChords");
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
                    TrackNameAliases = [" Lead ", "lead", "", "Solo"],
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
        settings.InstrumentMaps.Single(map => map.InstrumentId == 999).TrackNameAliases
            .ShouldBe(new[] { "Lead", "Solo" });
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Bongo").SourceNotes
            .ShouldBe(new[] { 64, 127 });
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Cymbal").SourceNotes
            .ShouldBe(new[] { 0 });
        settings.DrumkitSourceMaps.ShouldContain(map => map.TrackName == "BassDrum");
        settings.DrumTransposePresets.ShouldContain(preset => preset.Preset == MidiForgeDrumTransposePreset.Default);
    }

    [Fact]
    public void NormalizeCollapsesDuplicateInstrumentMapsAndPreservesLatestUserData()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        settings.InstrumentMaps.Add(new MidiForgeInstrumentMapSettings
        {
            InstrumentId = 3,
            InstrumentName = "Custom Lute",
            TrackName = "Renamed Lute",
            TrackOrder = 99,
            MidiPrograms = [6, 106, 106],
            TrackNameAliases = [" Plucked Lead ", "lute"],
        });

        MidiForgeMapDefaults.Normalize(settings);

        settings.InstrumentMaps.Count(map => map.InstrumentId == 3).ShouldBe(1);
        var lute = settings.InstrumentMaps.Single(map => map.InstrumentId == 3);
        lute.InstrumentName.ShouldBe("Lute");
        lute.TrackName.ShouldBe("Lute");
        lute.TrackOrder.ShouldBe(99);
        lute.MidiPrograms.ShouldBe(new[] { 6, 106 });
        lute.TrackNameAliases.ShouldContain("Plucked Lead");

        var countAfterFirstNormalize = settings.InstrumentMaps.Count;
        MidiForgeMapDefaults.Normalize(settings);
        settings.InstrumentMaps.Count.ShouldBe(countAfterFirstNormalize);
        settings.InstrumentMaps.Count(map => map.InstrumentId == 3).ShouldBe(1);
    }

    [Fact]
    public void NormalizeCollapsesDuplicateDrumkitSourceMapsAndPreservesLatestUserData()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        settings.DrumkitSourceMaps.Add(new MidiForgeDrumInstrumentMapSettings
        {
            InstrumentId = 30,
            TrackName = "Bongo",
            SourceNotes = [60, 64],
        });
        settings.DrumkitSourceMaps.Add(new MidiForgeDrumInstrumentMapSettings
        {
            TrackName = "Bass Drum",
            SourceNotes = [35, 36, 37, 41, 43, 45, 47, 48, 50],
        });

        MidiForgeMapDefaults.Normalize(settings);

        settings.DrumkitSourceMaps.Count(map => map.InstrumentId == 30).ShouldBe(1);
        settings.DrumkitSourceMaps.Single(map => map.InstrumentId == 30).SourceNotes
            .ShouldBe(new[] { 60, 64 });
        settings.DrumkitSourceMaps.Count(map => map.InstrumentId == 23).ShouldBe(1);
        settings.DrumkitSourceMaps.Single(map => map.InstrumentId == 23).TrackName.ShouldBe("BassDrum");
        settings.DrumkitSourceMaps.Single(map => map.InstrumentId == 23).SourceNotes
            .ShouldBe(new[] { 35, 36, 37, 41, 43, 45, 47, 48, 50 });

        var countAfterFirstNormalize = settings.DrumkitSourceMaps.Count;
        MidiForgeMapDefaults.Normalize(settings);
        settings.DrumkitSourceMaps.Count.ShouldBe(countAfterFirstNormalize);
        settings.DrumkitSourceMaps.Count(map => map.InstrumentId == 30).ShouldBe(1);
        settings.DrumkitSourceMaps.Count(map => map.InstrumentId == 23).ShouldBe(1);
    }

    [Fact]
    public void NormalizeCollapsesDuplicateDrumTransposePresets()
    {
        var settings = new MidiForgeMapSettings
        {
            DrumTransposePresets =
            [
                new MidiForgeDrumTransposePresetSettings
                {
                    Preset = MidiForgeDrumTransposePreset.Default,
                    Entries =
                    [
                        new MidiForgeDrumTransposeMapEntry
                        {
                            Category = "BassDrum",
                            DrumkitInstrument = "Kick Drum 1",
                            InputNote = 36,
                            OutputNote = 10,
                        },
                    ],
                },
                new MidiForgeDrumTransposePresetSettings
                {
                    Preset = MidiForgeDrumTransposePreset.Default,
                    Entries =
                    [
                        new MidiForgeDrumTransposeMapEntry
                        {
                            Category = "BassDrum",
                            DrumkitInstrument = "Kick Drum 1",
                            InputNote = 36,
                            OutputNote = 70,
                        },
                    ],
                },
            ],
        };

        MidiForgeMapDefaults.Normalize(settings);

        settings.DrumTransposePresets.Count
            .ShouldBe(Enum.GetValues<MidiForgeDrumTransposePreset>().Length);
        settings.DrumTransposePresets.Count(preset => preset.Preset == MidiForgeDrumTransposePreset.Default)
            .ShouldBe(1);

        var defaultPreset = settings.DrumTransposePresets
            .Single(preset => preset.Preset == MidiForgeDrumTransposePreset.Default);
        defaultPreset.Entries.Single(entry => entry.InputNote == 36).OutputNote.ShouldBe(70);

        var provider = new ConfigurationEditorMidiMapProvider(settings);
        provider.GetDrumTransposeTargets(MidiForgeDrumTransposePreset.Default)
            .Single(target => target.InputNote == 36)
            .OutputNote
            .ShouldBe(70);
    }
}
