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
        options.Single(option => option.DisplayName == MidiEditorTrackNameOptions.ProgramElectricGuitarTrackName)
            .IconId.ShouldBe<uint>(909);
    }
}
