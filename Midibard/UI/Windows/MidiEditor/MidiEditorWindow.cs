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

    // show / hide elements
    private bool _showTrackPanel = true;
    private bool _showEventPanel = false;

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
    private float _previewLeftPanelWidth = 200f;
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
    private readonly record struct NoteHitEntry(Vector2 RectMin, Vector2 RectMax, int EventIndex);
    private EditorDragMode _editorDragMode = EditorDragMode.None;
    private double _dragOriginSeconds;
    private float _dragOriginNoteOffset;
    private readonly Dictionary<int, (int tick, int val1, int val2, int dur)> _preDragSnapshot = new();
    private bool _gestureHistoryCaptured;
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
    private static readonly int[] PencilDivisions = { 1, 2, 4, 8, 16, 32, 64, 128 };
    private static readonly string[] PencilDivisionLabels = { "1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64", "1/128" };

    // Track name autocomplete (instruments as suggestions)
    private readonly ImGuiInputAutocompleteInstrument<TrackNameOption> _trackNameAutocomplete = new();

    private sealed record TrackNameOption(string DisplayName, uint IconId);

    // Instrument options for track name autocomplete (excludes the "None" entry at index 0).
    private static IReadOnlyList<TrackNameOption>? _trackNameOptions;
    private static IReadOnlyList<TrackNameOption> TrackNameOptions =>
        _trackNameOptions ??= BuildTrackNameOptions();

    private static IReadOnlyList<TrackNameOption> BuildTrackNameOptions()
    {
        var options = InstrumentHelper.Instruments
            .Skip(1)
            .Select(instrument => new TrackNameOption(instrument.FFXIVDisplayName, instrument.IconId))
            .ToList();

        var programGuitarIcon = InstrumentHelper.Instruments
            .FirstOrDefault(instrument => instrument.Row.RowId == 24)?.IconId
            ?? InstrumentHelper.Instruments.FirstOrDefault(instrument => instrument.IsGuitar)?.IconId
            ?? 60042;
        options.Add(new TrackNameOption("Program: ElectricGuitar", programGuitarIcon));

        return options;
    }

    // GM program names for the combo in the Program Change edit popup
    private static readonly string[] GmProgramComboItems = Enumerable.Range(0, 128)
        .Select(i =>
        {
            var name = DryWetMidiExtensions.GetGMProgramName((byte)i);
            return string.IsNullOrEmpty(name) ? $"{i + 1}" : $"{i + 1} - {name}";
        })
        .ToArray();

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

    private bool IsPreviewTrackVisible(int trackIndex)
    {
        var tracks = _previewTracks;
        return tracks != null && (uint)trackIndex < (uint)tracks.Length && tracks[trackIndex].Visible;
    }

    public override void Draw()
    {
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
        DrawOpenWithOptionsPopup();
        DrawImportFromUrlPopup();
        DrawMergeSongPopup();
        DrawSanitizePopup();
        DrawPrepareForPlaybackPopup();
        DrawAdaptToRangePopup();
        DrawApplyTrackNameTransposesPopup();
        DrawMergeGuitarToneTracksPopup();
        DrawAutoEditPopup();
        DrawSplitChordsPopup();
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

        DrawMenuBar();
        DrawToolbar();
        ImGui.Separator();

        if (_file == null)
        {
            ImGui.TextDisabled("No MIDI file loaded. Use the Open button above to load a file.");
            return;
        }

        var colCount = 1 + (_showTrackPanel ? 1 : 0) + (_showEventPanel ? 1 : 0);
        var available = ImGui.GetContentRegionAvail();
        if (ImGui.BeginTable("##MidiEditorPanels", colCount,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV,
            available))
        {
            if (_showTrackPanel)
                ImGui.TableSetupColumn("##Tracks", ImGuiTableColumnFlags.WidthFixed, 250f * ImGuiHelpers.GlobalScale);
            if (_showEventPanel)
                ImGui.TableSetupColumn("##Events", ImGuiTableColumnFlags.WidthFixed, 420f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("##PianoRoll", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();

            if (_showTrackPanel)
            {
                ImGui.TableNextColumn();
                DrawTrackListPanel();
            }

            if (_showEventPanel)
            {
                ImGui.TableNextColumn();
                DrawEventListPanel();
            }

            ImGui.TableNextColumn();
            DrawPianoRollPanel();

            ImGui.EndTable();
        }
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
        _playbackPreview.Load(null, preservePosition: false);
        _file?.Tracks.ForEach(t => t.Dispose());
        _file = new EditableMidiFile(midi, path, displayName);
        _file.ConsolidateTempoToConductorTrack();
        _file.SetDirtyStateForLoad(isDirty);
        _history.Clear();
        _selectedTrackIndex = -1;
        _eventSearch = string.Empty;
        _selectedTrackIndices.Clear();
        _selectedEventIndices.Clear();
        _globalTracksChecked = _globalEventsChecked = false;
        _editorDragMode = EditorDragMode.None;
        _preDragSnapshot.Clear();
        _noteHitList.Clear();
        WindowName = $"MIDI Editor - {_file.DisplayName}###MidiEditorWindow";
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
        if (index == _selectedTrackIndex) return;

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

        ExecuteDirectEdit(() =>
        {
            // Delete from highest index downward to keep indices valid
            foreach (var idx in tracksToDelete)
            {
                if (_selectedTrackIndex == idx) _selectedTrackIndex = -1;
                _file.RemoveTrack(idx);
            }

            _selectedTrackIndices.Clear();
            _globalTracksChecked = false;
            return true;
        });
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
            .OrderByDescending(i => i)
            .Select(i => track.Events[i])
            .ToList();
        if (toDelete.Count == 0) return;

        ExecuteDirectEdit(() =>
        {
            foreach (var ev in toDelete)
                track.RemoveEvent(ev);

            _selectedEventIndices.Clear();
            _globalEventsChecked = false;
            return true;
        });
    }
}
