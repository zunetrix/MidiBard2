using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control;
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

    // Batch selection — tracks
    private readonly HashSet<int> _selectedTrackIndices = new();
    private bool _globalTracksChecked = false;

    // Batch selection — events (indices into the selected track's Events list)
    private readonly HashSet<int> _selectedEventIndices = new();
    private bool _globalEventsChecked = false;

    // Transpose popup state
    private int _transposeSemitones = 0;

    // Merge popup state
    private bool _mergeIncludePC = true;
    private bool _mergeIncludePB = true;
    private int _mergeTargetRelIdx = 0;

    // Quantize popup state
    private int _quantizeStepIndex = 2; // default: 1/16 note
    private bool _quantizeToNewTrack = false;

    private EditableEvent? _editingEvent;
    private EditableTrack? _editingTrack;
    private string _editTrackName = string.Empty;
    private bool _editTrackFocusNext = false; // focus the inline edit input on next frame

    // show / hide elements
    private bool _showTrackPanel = true;
    private bool _showEventPanel = true;

    // Piano roll preview (panel 3)
    private EditableMidiFile? _previewFile = null;
    private int _previewFileVersion = -1;
    private TrackDisplayState[]? _previewTracks = null;
    private TempoMap? _previewTempoMap = null;
    private float _previewLeftPanelWidth = 200f;
    private readonly PianoRollState _previewState = new()
    {
        AutoFollowPlayback = false,
        TimePixelsPerSecond = 25f,
        NoteMinHeight = 10f,
        CameraTopNote = 90f,
        ShowC3C6Range = true,
        ShowNoteBorder = true,
        ShowNoteLabel = false,
        ShowLeftPanel = true,
        ShowSeconds = true,
    };

    // Track name autocomplete (instruments as suggestions)
    private readonly ImGuiInputAutocompleteInstrument<Instrument> _trackNameAutocomplete = new();

    // Instrument options for track name autocomplete (excludes the "None" entry at index 0)
    private static IReadOnlyList<Instrument>? _instrumentOptions;
    private static IReadOnlyList<Instrument> InstrumentOptions =>
        _instrumentOptions ??= InstrumentHelper.Instruments.Skip(1).ToArray();

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
        _file?.Tracks.ForEach(t => t.Dispose());
        _file = null;
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
        DrawMergePopup();
        DrawQuantizePopup();

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
            _file?.Tracks.ForEach(t => t.Dispose());

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var midi = MidiFile.Read(stream);
            _file = new EditableMidiFile(midi, path);
            _file.ConsolidateTempoToConductorTrack();
            _file.IsDirty = false; // auto-consolidation doesn't count as user change
            _selectedTrackIndex = -1;
            _eventSearch = string.Empty;
            _selectedTrackIndices.Clear();
            _selectedEventIndices.Clear();
            _globalTracksChecked = _globalEventsChecked = false;
            WindowName = $"MIDI Editor - {Path.GetFileName(path)}###MidiEditorWindow";
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[MidiEditorWindow] Failed to open MIDI file");
        }
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

    private void DeleteSelectedTracks()
    {
        if (_file == null) return;

        // Delete from highest index downward to keep indices valid
        foreach (var idx in _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderByDescending(i => i))
        {
            if (_selectedTrackIndex == idx) _selectedTrackIndex = -1;
            _file.RemoveTrack(idx);
        }

        _selectedTrackIndices.Clear();
        _globalTracksChecked = false;
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

        foreach (var ev in toDelete)
            track.RemoveEvent(ev);

        _file!.IsDirty = true;
        _selectedEventIndices.Clear();
        _globalEventsChecked = false;
    }
}
