namespace MidiBard.Tests.UI.Windows.MidiEditor;

public class MidiEditorPianoRollHelpersTests
{
    [Theory]
    [InlineData(0, "1", 1920)]
    [InlineData(1, "1/2", 960)]
    [InlineData(2, "1/4", 480)]
    [InlineData(3, "1/8", 240)]
    [InlineData(4, "1/16", 120)]
    [InlineData(5, "1/32", 60)]
    [InlineData(6, "1/64", 30)]
    [InlineData(7, "1/128", 15)]
    public void PencilNoteSizing_UsesDropdownDivisionForInitialDuration(
        int divisionIndex,
        string label,
        long expectedTicks)
    {
        MidiEditorPencilNoteSizing.DivisionLabels[divisionIndex].ShouldBe(label);
        MidiEditorPencilNoteSizing.GetDurationTicks(480, divisionIndex).ShouldBe(expectedTicks);
    }

    [Fact]
    public void PencilNoteSizing_FallsBackToDefaultPpqnForNonPositiveValues()
    {
        MidiEditorPencilNoteSizing.GetDurationTicks(0, divisionIndex: 3).ShouldBe(240);
        MidiEditorPencilNoteSizing.GetDurationTicks(-1, divisionIndex: 3).ShouldBe(240);
    }
}
