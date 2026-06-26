namespace MidiBard;

public class MidiEditorViewSettings
{
    public bool ShowTrackPanel { get; set; } = true;
    public bool ShowEventPanel { get; set; } = false;
    public bool ShowLeftPanel { get; set; } = false;
    public bool ShowNoteLabel { get; set; } = true;
    public bool ShowNoteBorder { get; set; } = true;
    public bool ShowSeconds { get; set; } = true;
    public bool ShowC3C6Range { get; set; } = true;
    public bool UseTrackNameTranspose { get; set; } = false;
    public bool UseAutoAdapt { get; set; } = false;
    public bool ShowProgramChangeMarkers { get; set; } = false;
    public bool ShowTempoMarkers { get; set; } = false;
    public bool ShowTimeSignatureMarkers { get; set; } = false;
    public bool ShowNotePreview { get; set; } = true;
    public bool InvertVerticalDrag { get; set; }
}
