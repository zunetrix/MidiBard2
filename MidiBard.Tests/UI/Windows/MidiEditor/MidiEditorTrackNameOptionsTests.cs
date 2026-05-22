using MidiBard.Control.MidiControl.Editing;

namespace MidiBard.Tests.UI.Windows.MidiEditor;

public class MidiEditorTrackNameOptionsTests
{
    [Fact]
    public void Build_UsesCanonicalInstrumentMapNamesAndProgramGuitarOption()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        var lute = settings.InstrumentMaps.Single(map => map.TrackName == "Lute");
        lute.TrackName = "CustomLute";
        lute.TrackNameAliases.Add("Plucked Lead");

        var options = MidiEditorTrackNameOptions.Build(
            new ConfigurationEditorMidiMapProvider(settings),
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                ["Lute"] = 101,
                ["ElectricGuitarClean"] = 303,
            },
            909);

        options.Select(option => option.DisplayName).ShouldContain("Lute");
        options.Select(option => option.DisplayName).ShouldNotContain("CustomLute");
        options.Select(option => option.DisplayName).ShouldNotContain("Plucked Lead");
        options.Select(option => option.DisplayName).ShouldNotContain("Clean");
        options.Select(option => option.DisplayName).ShouldContain(MidiEditorTrackNameOptions.ProgramElectricGuitarTrackName);
        options.Single(option => option.DisplayName == "Lute").IconId.ShouldBe<uint>(101);
        options.Single(option => option.DisplayName == "Lute").PickerInstrumentId.ShouldBe((uint?)3);
        options.Single(option => option.DisplayName == MidiEditorTrackNameOptions.ProgramElectricGuitarTrackName)
            .IconId.ShouldBe<uint>(909);
        options.Single(option => option.DisplayName == MidiEditorTrackNameOptions.ProgramElectricGuitarTrackName)
            .PickerInstrumentId.ShouldBeNull();
    }

    [Fact]
    public void GetQuickPickerOptions_UsesPlayableInstrumentOrderAndExcludesSpecialNames()
    {
        var options = MidiEditorTrackNameOptions.Build(
            DefaultEditorMidiMapProvider.Instance,
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                ["Harp"] = 101,
                ["Piano"] = 202,
                ["ElectricGuitarOverdriven"] = 303,
                ["ElectricGuitarClean"] = 404,
            },
            909);

        var quickOptions = MidiEditorTrackNameOptions.GetQuickPickerOptions(options).ToList();
        var names = quickOptions.Select(option => option.DisplayName).ToList();

        names.ShouldContain("Harp");
        names.ShouldContain("Piano");
        names.ShouldContain("ElectricGuitarOverdriven");
        names.ShouldContain("ElectricGuitarClean");
        names.ShouldNotContain(MidiEditorTrackNameOptions.ProgramElectricGuitarTrackName);
        names.IndexOf("Harp").ShouldBeLessThan(names.IndexOf("Piano"));
        names.IndexOf("ElectricGuitarOverdriven").ShouldBeLessThan(names.IndexOf("ElectricGuitarClean"));
        quickOptions.Single(option => option.DisplayName == "ElectricGuitarClean")
            .PickerInstrumentId.ShouldBe((uint?)25);
    }
}
