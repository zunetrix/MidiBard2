using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Event;
using MidiBard.Control.MidiControl.Editing.Commands.File;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;
using MidiBard.Control.MidiControl.Preview;
using MidiBard.Extensions.DryWetMidi;
using MidiBard.Util;
using MidiBard.Util.ImGuiExt;

namespace MidiBard;

public partial class MidiEditorWindow : Window, IDisposable
{
    private readonly Plugin _plugin;

    // State
    private EditableMidiFile? _file;
    private int _selectedTrackIndex = -1;
    private string _eventSearch = string.Empty;
    private MidiEventFilter _eventFilter = MidiEventFilter.Notes | MidiEventFilter.ProgramChange | MidiEventFilter.PitchBend | MidiEventFilter.Tempo;
    private string? _pendingPopup;
    private readonly MidiForgeHistory _history = new();
    private readonly MidiEditorSessionState _editorCommandSession;
    private readonly EditorCommandExecutor _editorCommandExecutor = new();
    private readonly EditorQueryExecutor _editorQueryExecutor = new();
    private readonly PreviewCommandExecutor _previewCommandExecutor = new();
    private readonly PreviewQueryExecutor _previewQueryExecutor = new();

    // Batch selection - tracks
    private readonly HashSet<int> _selectedTrackIndices = new();
    private bool _globalTracksChecked = false;

    // Batch selection - events (indices into the selected track's Events list)
    private readonly HashSet<int> _selectedEventIndices = new();
    private bool _globalEventsChecked = false;

    private EditableEvent? _editingEvent;
    private EditableTrack? _editingTrack;
    private string _editTrackName = string.Empty;
    private bool _editTrackFocusNext = false; // focus the inline edit input on next frame

    // Event list visible indices cache (invalidated when track, filter, search, or file version changes)
    private readonly List<int> _visibleEventIndices = new();
    private int _visibleEventsTrackIndex = -1;
    private MidiEventFilter _visibleEventsFilter;
    private string _visibleEventsSearch = string.Empty;
    private int _visibleEventsVersion = -1;

    // show / hide elements
    private bool _showTrackPanel = true;
    private bool _showEventPanel = false;
    private float _trackPanelWidth;
    private float _eventPanelWidth;

    // Piano roll preview (panel 3)
    private readonly MidiEditorPlaybackPreview _playbackPreview;
    private EditableMidiFile? _previewFile = null;
    private int _previewFileVersion = -1;
    private TrackDisplayState[]? _previewTracks = null;
    private TempoMap? _previewTempoMap = null;
    private EditableMidiFile? _trackDiagnosticsFile = null;
    private int _trackDiagnosticsVersion = -1;
    private int _trackDiagnosticsTrackCount = -1;
    private IReadOnlyDictionary<int, MidiForgeTrackAnalysis> _trackDiagnosticsByIndex =
        new Dictionary<int, MidiForgeTrackAnalysis>();
    private IReadOnlyDictionary<int, (IReadOnlyList<string> Warnings, IReadOnlyList<string> TooltipLines)> _trackDiagnosticsStringsByIndex =
        new Dictionary<int, (IReadOnlyList<string>, IReadOnlyList<string>)>();
    private string[]? _trackDisplayNumbers;
    private float _previewLeftPanelWidth = 200f;

    // Program change marker cache (invalidated by _file.Version change)
    private EditableMidiFile? _pcMarkerCacheFile;
    private int _pcMarkerCacheVersion = -1;
    private IReadOnlyDictionary<int, IReadOnlyList<PreviewProgramChangeMarker>> _pcMarkersByTrack =
        new Dictionary<int, IReadOnlyList<PreviewProgramChangeMarker>>();

    private readonly record struct PreviewProgramChangeMarker(double TimeSeconds, int ProgramNumber, uint? IconId);

    // Reusable list for deferred icon draws in DrawProgramChangeMarkers (avoids per-frame allocation)
    private readonly List<(Vector2 pos, uint iconId)> _pcIconsToRender = new();

    // Per-frame UI caches (invalidated at the start of each Draw)
    private IEditorMidiMapProvider? _frameMidiMapProvider;
    private IReadOnlyList<MidiEditorTrackNameOption>? _frameTrackNameOptions;
    private IReadOnlyDictionary<string, uint>? _frameTrackNameIconMap;
    private readonly PianoRollState _previewState = new()
    {
        AutoFollowPlayback = false,
        TimePixelsPerSecond = 25f,
        NoteMinHeight = 10f,
        CameraTopNote = 90f,
        ShowC3C6Range = true,
        ShowNoteBorder = true,
        ShowNoteLabel = true,
        ShowLeftPanel = false,
        ShowSeconds = true,
        PanMode = true, // default: pan; hold Ctrl to select/move/resize
    };

    // Piano roll interaction state
    private enum EditorDragMode { None, Pan, Move, Resize, BoxSelect, PencilDraw }
    private enum NoteHitZone { None, Body, StartResize, EndResize }
    private readonly record struct NoteHitEntry(Vector2 RectMin, Vector2 RectMax, int EventIndex);
    private EditorDragMode _editorDragMode = EditorDragMode.None;
    private double _dragOriginSeconds;
    private float _dragOriginNoteOffset;
    private bool _resizeFromStart;
    private readonly Dictionary<int, (int tick, int val1, int val2, int dur)> _preDragSnapshot = new();
    private Vector2 _boxSelectA;
    private Vector2 _boxSelectB;
    private HashSet<int> _boxSelectInitialSelection = new();
    private readonly List<NoteHitEntry> _noteHitList = new();
    private bool _pianoRollScrollToSelected;
    private int _pianoRollScrollTarget = -1;
    private float _pianoRollWidthCache;
    // Snapshot of _file.Tracks order at the time _previewTracks was last built,
    // used to match display state by EditableTrack reference after DnD reorders.
    private EditableTrack[]? _previewTrackOrder = null;

    // Pencil tool state
    private bool _pencilModeActive = false;
    private bool _pencilAutoTrim = true;    // true = trim to fit; false = block if would overlap
    private int _pencilNoteDivisionIndex = 2; // default: 1/8 note
    private EditableEvent? _pencilDragEvent;
    private double _pencilDragOriginSec;
    private long _pencilNoteStartTick;
    private long _pencilNoteMaxDur = long.MaxValue; // max allowed duration; set at insert to prevent drag from re-introducing overlap
    private static readonly string[] PencilDivisionLabels = MidiEditorPencilNoteSizing.DivisionLabels;

    // Track name autocomplete (instruments as suggestions)
    private readonly ImGuiInputAutocompleteInstrument<MidiEditorTrackNameOption> _trackNameAutocomplete = new();

    // GM program names for the combo in the Program Change edit popup
    private static readonly string[] GmProgramComboItems = Enumerable.Range(0, 128)
        .Select(i =>
        {
            var name = DryWetMidiExtensions.GetGMProgramName((byte)i);
            return string.IsNullOrEmpty(name) ? $"{i + 1}" : $"{i + 1} - {name}";
        })
        .ToArray();

    private IReadOnlyList<MidiEditorTrackNameOption> GetTrackNameOptions()
    {
        _frameTrackNameOptions ??= MidiEditorTrackNameOptions.Build(
            CreateEditorMidiMapProvider(),
            BuildTrackNameIconMap(),
            GetProgramElectricGuitarIconId());
        return _frameTrackNameOptions;
    }

    private IReadOnlyDictionary<string, uint> BuildTrackNameIconMap()
    {
        if (_frameTrackNameIconMap != null)
            return _frameTrackNameIconMap;

        var icons = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        if (InstrumentHelper.Instruments != null)
        {
            foreach (var instrument in InstrumentHelper.Instruments)
            {
                if (string.IsNullOrWhiteSpace(instrument.FFXIVDisplayName))
                    continue;

                icons.TryAdd(instrument.FFXIVDisplayName, instrument.IconId);

                var sanitizedName = InstrumentHelper.SanitizeName(instrument.FFXIVDisplayName);
                if (!string.IsNullOrWhiteSpace(sanitizedName))
                    icons.TryAdd(sanitizedName, instrument.IconId);
            }
        }

        _frameTrackNameIconMap = icons;
        return icons;
    }

    private static uint GetProgramElectricGuitarIconId()
        => InstrumentHelper.Instruments?
            .FirstOrDefault(instrument => instrument.Row.RowId == 24)?.IconId
           ?? InstrumentHelper.Instruments?
            .FirstOrDefault(instrument => instrument.IsGuitar)?.IconId
           ?? MidiEditorTrackNameOptions.DefaultIconId;

    public MidiEditorWindow(Plugin plugin) : base("MIDI Editor###MidiEditorWindow")
    {
        _plugin = plugin;
        _editorCommandSession = new MidiEditorSessionState(_history);
        _playbackPreview = new MidiEditorPlaybackPreview(plugin, IsPreviewTrackVisible);
        Size = ImGuiHelpers.ScaledVector2(960, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(600, 400)
        };
        Flags = ImGuiWindowFlags.MenuBar;
    }

    public void Dispose()
    {
        if (_editorCommandSession.PopupStates.TryGet<ImportPopupState>(ImportPopupStateKey, out var importState))
        {
            importState.Cancellation?.Cancel();
            importState.Cancellation?.Dispose();
        }

        _playbackPreview.Dispose();
        _file?.Tracks.ForEach(t => t.Dispose());
        _file = null;
    }

    public override void OnClose()
    {
        CancelEditorCommandGesture();
        StopPlaybackPreview();
        base.OnClose();
    }

    private bool IsPreviewTrackVisible(int trackIndex)
    {
        var tracks = _previewTracks;
        return tracks != null && (uint)trackIndex < (uint)tracks.Length && tracks[trackIndex].Visible;
    }

    public override void Draw()
    {
        // Reset per-frame UI caches
        _frameMidiMapProvider = null;
        _frameTrackNameOptions = null;
        _frameTrackNameIconMap = null;

        if (_pendingPopup != null)
        {
            ImGui.OpenPopup(_pendingPopup);
            _pendingPopup = null;
        }

        // Popups rendered at window level - must match same context as OpenPopup
        DrawEventEditPopup();
        DrawEventFilterPopup();
        DrawTransposePopup();
        DrawTransposeNotesPopup();
        DrawMergePopup();
        DrawQuantizePopup();
        DrawChangeNoteLengthPopup();
        DrawSetTrackProgramPopup();
        DrawMapInstrumentsPopup();
        DrawOpenWithOptionsPopup();
        DrawImportFromUrlPopup();
        DrawMergeSongPopup();
        DrawSanitizePopup();
        DrawPrepareForPlaybackPopup();
        DrawAutoArrangeSelectedPopup();
        DrawAdaptToRangePopup();
        DrawApplyTrackNameTransposesPopup();
        DrawMergeGuitarToneTracksPopup();
        DrawAutoEditPopup();
        DrawSplitChordsPopup();
        DrawLimitSimultaneousNotesPopup();
        DrawStrumNotesPopup();
        DrawSplitNotesByToneRangePopup();
        DrawSplitNotesByLengthRangePopup();
        DrawExtendNotesDurationPopup();
        DrawSplitEqualNotesPopup();
        DrawDifferenceTracksPopup();
        DrawSplitNotesIntoTracksPopup();
        DrawGeneratePitchBendNotesPopup();
        DrawSplitDrumkitPopup();
        DrawDisassembleDrumkitPopup();
        DrawTransposeSingleNoteTracksToDrumNotePopup();
        DrawGlueNotesPopup();
        DrawRepeatLoopPopup();
        DrawInsertMeasuresPopup();
        DrawDeleteMeasuresPopup();

        DrawMenuBar();
        DrawToolbar();
        ImGui.Separator();

        if (_file == null)
        {
            ImGui.TextDisabled("No MIDI file loaded. Use the Open button above to load a file.");
            return;
        }

        var available = ImGui.GetContentRegionAvail();
        DrawEditorPanels(available);
    }

    private void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            DalamudApi.PluginLog.Warning($"[MidiEditorWindow] File not found: {path}");
            return;
        }

        try
        {
            var midi = ServiceContainer.MidiFileService.LoadMidiFile(path);
            if (midi == null)
            {
                DalamudApi.PluginLog.Error($"[MidiEditorWindow] Failed to load MIDI file: {path}");
                return;
            }

            OpenLoadedMidiFile(midi, path, isDirty: false);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[MidiEditorWindow] Failed to open MIDI file");
        }
    }

    private void OpenLoadedMidiFile(MidiFile midi, string? path, bool isDirty, string? displayName = null)
    {
        CancelEditorCommandGesture();
        _playbackPreview.Prepare(null, 0.0);
        var result = _editorCommandExecutor.Execute(
            new OpenLoadedMidiFileCommand(),
            CreateEditorCommandContext(requireFile: false),
            new OpenLoadedMidiFileOptions(midi, path, isDirty, displayName));

        if (!result.Succeeded)
        {
            DalamudApi.PluginLog.Warning($"[MidiEditorWindow] Failed to open loaded MIDI: {result.Message}");
            return;
        }

        ApplyDocumentCommandResult(resetTransientState: true);
    }

    private void ApplyDocumentCommandResult(bool resetTransientState)
    {
        _file = _editorCommandSession.File;

        if (resetTransientState)
        {
            CancelEditorCommandGesture();
            _playbackPreview.Prepare(null, 0.0);
            _selectedTrackIndex = -1;
            _eventSearch = string.Empty;
            _editingEvent = null;
            _editingTrack = null;
            _editorDragMode = EditorDragMode.None;
            _preDragSnapshot.Clear();
            _noteHitList.Clear();
        }

        ApplyEditorCommandRefreshHints();

        WindowName = _file == null
            ? "MIDI Editor###MidiEditorWindow"
            : $"MIDI Editor - {_file.DisplayName}###MidiEditorWindow";
    }

    /// <summary>Opens a MIDI file directly and brings the window to front.</summary>
    public void OpenFromFile(string filePath)
    {
        OpenFile(filePath);
        IsOpen = true;
    }

    private void SelectTrack(int index)
    {
        if (_file == null) return;
        if (index == _selectedTrackIndex)
        {
            if (index >= 0 && index < _file.Tracks.Count)
                _file.Tracks[index].UnloadEvents();
            _selectedTrackIndex = -1;
            _selectedEventIndices.Clear();
            _eventSearch = string.Empty;
            _globalEventsChecked = false;
            return;
        }

        // Flush previous track back to its chunk
        if (_selectedTrackIndex >= 0 && _selectedTrackIndex < _file.Tracks.Count)
            _file.Tracks[_selectedTrackIndex].UnloadEvents();

        _selectedTrackIndex = index;
        _eventSearch = string.Empty;
        _selectedEventIndices.Clear();
        _globalEventsChecked = false;

        if (index >= 0 && index < _file.Tracks.Count)
            _file.Tracks[index].LoadEvents(_file.TempoMap);
    }

    //  Track selection helpers

    private void SelectAllTracks()
    {
        if (_file == null) return;
        for (int i = 0; i < _file.Tracks.Count; i++)
            _selectedTrackIndices.Add(i);
    }

    private void ClearTrackSelection()
    {
        _selectedTrackIndices.Clear();
        _globalTracksChecked = false;
    }

    private void ToggleAllTracksVisibility()
    {
        for (int i = 0; i < _previewTracks.Length; i++)
            _previewTracks[i].Visible = !_previewTracks[i].Visible;

    }

    private void DeleteSelectedTracks()
    {
        if (_file == null) return;

        var tracksToDelete = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderByDescending(i => i)
            .ToList();
        if (tracksToDelete.Count == 0) return;

        var result = _editorCommandExecutor.Execute(
            new DeleteTracksCommand(),
            CreateEditorCommandContext(),
            new DeleteTracksOptions(tracksToDelete));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }

    //  Event selection helpers

    private List<EditableEvent>? CurrentEvents => _selectedTrackIndex >= 0
        && _file != null && _selectedTrackIndex < _file.Tracks.Count
        ? _file.Tracks[_selectedTrackIndex].Events
        : null;

    private void SelectAllEvents()
    {
        var events = CurrentEvents;
        if (events == null) return;
        for (int i = 0; i < events.Count; i++)
            _selectedEventIndices.Add(i);
    }

    private void ClearEventSelection()
    {
        _selectedEventIndices.Clear();
        _globalEventsChecked = false;
    }

    private void DeleteSelectedEvents()
    {
        var track = _selectedTrackIndex >= 0 && _file != null
            ? _file.Tracks[_selectedTrackIndex] : null;
        if (track?.Events == null) return;

        var toDelete = _selectedEventIndices
            .Where(i => i < track.Events.Count)
            .Select(i => EventSelectionKey.FromEvent(i, track.Events[i]))
            .ToList();
        if (toDelete.Count == 0) return;

        var result = _editorCommandExecutor.Execute(
            new DeleteEventsCommand(),
            CreateEditorCommandContext(),
            new DeleteEventsOptions(_selectedTrackIndex, toDelete));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }

    private void DeleteSelectedNotes()
    {
        var selectedNoteKeys = GetSelectedNoteKeys();
        if (selectedNoteKeys.Count == 0) return;

        var result = _editorCommandExecutor.Execute(
            new DeleteSelectedNotesCommand(),
            CreateEditorCommandContext(),
            new DeleteSelectedNotesOptions(_selectedTrackIndex, selectedNoteKeys));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }

    private void NudgeSelectedNotesByGrid(int direction)
    {
        var selectedNoteKeys = GetSelectedNoteKeys();
        if (selectedNoteKeys.Count == 0 || direction == 0) return;

        var stepTicks = GetSelectedNoteGridStepTicks();
        if (stepTicks <= 0) return;

        var result = _editorCommandExecutor.Execute(
            new NudgeSelectedNotesCommand(),
            CreateEditorCommandContext(),
            new NudgeSelectedNotesOptions(
                _selectedTrackIndex,
                selectedNoteKeys,
                direction < 0 ? -stepTicks : stepTicks));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }

    private long GetSelectedNoteGridStepTicks()
    {
        var events = CurrentEvents;
        if (events == null || _file == null)
            return 0;

        var referenceTick = _selectedEventIndices
            .Where(i => (uint)i < (uint)events.Count && events[i].NoteOffSource != null)
            .Select(i => events[i].Tick)
            .DefaultIfEmpty(0)
            .Min();

        return GetGridStepTicks(referenceTick, _file.TempoMap);
    }

    private long GetGridStepTicks(long tick, TempoMap tmap)
    {
        var pos = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(Math.Max(0, tick), tmap);
        var bar = pos.Bars;
        var beat = pos.Beats;
        var barTick = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(bar, 0), tmap);

        if (_previewState.BeatDivision == BeatSubdivision.Bars)
        {
            var nextBarTick = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(bar + 1, 0), tmap);
            return Math.Max(1, nextBarTick - barTick);
        }

        var beatTick = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(bar, beat), tmap);
        var beatMetric = TimeConverter.ConvertTo<MetricTimeSpan>(beatTick, tmap);
        var timeSig = tmap.GetTimeSignatureAtTime(beatMetric);
        var beatsPerBar = timeSig.Numerator;
        var nextBeat = beat < beatsPerBar - 1 ? beat + 1 : 0;
        var nextBarForBeat = beat < beatsPerBar - 1 ? bar : bar + 1;
        var nextBeatTick = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(nextBarForBeat, nextBeat), tmap);
        var beatDuration = nextBeatTick - beatTick;
        if (beatDuration <= 0)
            return 1;

        var subdivision = Math.Max(1, (int)_previewState.BeatDivision);
        return Math.Max(1, beatDuration / subdivision);
    }

    private void CopySelectedNotes()
    {
        var selectedNoteKeys = GetSelectedNoteKeys();
        if (selectedNoteKeys.Count == 0) return;

        var result = _editorCommandExecutor.Execute(
            new CopySelectedNotesCommand(),
            CreateEditorCommandContext(),
            new CopySelectedNotesOptions(_selectedTrackIndex, selectedNoteKeys));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }

    private void PasteCopiedNotes()
    {
        if (_file == null || !_editorCommandSession.NoteClipboard.HasNotes)
            return;

        if (_selectedTrackIndex < 0 || _selectedTrackIndex >= _file.Tracks.Count)
            return;

        var track = _file.Tracks[_selectedTrackIndex];
        if (track.IsConductorTrack)
            return;

        var result = _editorCommandExecutor.Execute(
            new PasteCopiedNotesCommand(),
            CreateEditorCommandContext(),
            new PasteCopiedNotesOptions(
                _selectedTrackIndex,
                GetPasteAnchorTick(),
                _editorCommandSession.NoteClipboard.Notes));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }

    private long GetPasteAnchorTick()
    {
        if (_file == null)
            return 0;

        var seconds = Math.Max(0, _playbackPreview.PositionSeconds);
        return TimeConverter.ConvertFrom(
            new MetricTimeSpan((long)(seconds * 1_000_000.0)),
            _file.TempoMap);
    }
}
