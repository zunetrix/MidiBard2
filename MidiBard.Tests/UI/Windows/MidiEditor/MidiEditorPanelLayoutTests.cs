namespace MidiBard.Tests.UI.Windows.MidiEditor;

public class MidiEditorPanelLayoutTests
{
    [Fact]
    public void Calculate_TrackPanelCannotHidePianoRoll()
    {
        var layout = MidiEditorPanelLayout.Calculate(
            availableWidth: 600f,
            showTrackPanel: true,
            showEventPanel: false,
            requestedTrackWidth: 900f,
            requestedEventWidth: 420f,
            scale: 1f);

        layout.TrackWidth.ShouldBe(415f);
        layout.EventWidth.ShouldBe(0f);
        layout.PianoRollWidth.ShouldBe(180f);
    }

    [Fact]
    public void Calculate_EventPanelCannotHidePianoRoll()
    {
        var layout = MidiEditorPanelLayout.Calculate(
            availableWidth: 700f,
            showTrackPanel: false,
            showEventPanel: true,
            requestedTrackWidth: 250f,
            requestedEventWidth: 900f,
            scale: 1f);

        layout.TrackWidth.ShouldBe(0f);
        layout.EventWidth.ShouldBe(515f);
        layout.PianoRollWidth.ShouldBe(180f);
    }

    [Fact]
    public void Calculate_BothPanelsVisibleInNarrowEditorKeepsPianoRollVisible()
    {
        var layout = MidiEditorPanelLayout.Calculate(
            availableWidth: 520f,
            showTrackPanel: true,
            showEventPanel: true,
            requestedTrackWidth: 300f,
            requestedEventWidth: 220f,
            scale: 1f);

        layout.TrackWidth.ShouldBeInRange(190f, 191f);
        layout.EventWidth.ShouldBeInRange(139f, 140f);
        layout.PianoRollWidth.ShouldBe(180f, 0.01f);
    }

    [Fact]
    public void Calculate_HiddenPanelsGiveAllWidthToPianoRoll()
    {
        var layout = MidiEditorPanelLayout.Calculate(
            availableWidth: 640f,
            showTrackPanel: false,
            showEventPanel: false,
            requestedTrackWidth: 900f,
            requestedEventWidth: 900f,
            scale: 1f);

        layout.TrackWidth.ShouldBe(0f);
        layout.EventWidth.ShouldBe(0f);
        layout.PianoRollWidth.ShouldBe(640f);
    }

    [Fact]
    public void Calculate_RestoredPanelsUseStoredWidthsWhenThereIsRoom()
    {
        var layout = MidiEditorPanelLayout.Calculate(
            availableWidth: 1200f,
            showTrackPanel: true,
            showEventPanel: true,
            requestedTrackWidth: 360f,
            requestedEventWidth: 480f,
            scale: 1f);

        layout.TrackWidth.ShouldBe(360f);
        layout.EventWidth.ShouldBe(480f);
        layout.PianoRollWidth.ShouldBe(350f);
    }

    [Fact]
    public void Calculate_DefaultTrackPanelLeavesRoomForInstrumentNames()
    {
        var layout = MidiEditorPanelLayout.Calculate(
            availableWidth: 960f,
            showTrackPanel: true,
            showEventPanel: false,
            requestedTrackWidth: MidiEditorPanelLayout.DefaultTrackWidth(1f),
            requestedEventWidth: MidiEditorPanelLayout.DefaultEventWidth(1f),
            scale: 1f);

        layout.TrackWidth.ShouldBe(360f);
        layout.PianoRollWidth.ShouldBe(595f);
    }

    [Fact]
    public void ResizeMaximumsReservePianoRollWidth()
    {
        MidiEditorPanelLayout.MaxTrackResizeWidth(
                availableWidth: 900f,
                showEventPanel: true,
                requestedEventWidth: 300f,
                scale: 1f)
            .ShouldBe(410f);

        MidiEditorPanelLayout.MaxEventResizeWidth(
                availableWidth: 900f,
                showTrackPanel: true,
                requestedTrackWidth: 360f,
                scale: 1f)
            .ShouldBe(350f);
    }
}
