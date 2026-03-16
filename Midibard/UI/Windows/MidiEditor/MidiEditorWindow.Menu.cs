using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private void DrawMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        //  File
        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Open..."))
                OpenMidiFileDialog();

            using (ImRaii.Disabled(_file is not { IsDirty: true }))
                if (ImGui.MenuItem("Save"))
                    _file?.Save();

            using (ImRaii.Disabled(_file == null))
                if (ImGui.MenuItem("Save As..."))
                    SaveAsDialog();

            ImGui.EndMenu();
        }

        //  Edit
        if (ImGui.BeginMenu("Edit"))
        {
            ImGui.TextDisabled("(coming soon)");
            ImGui.EndMenu();
        }

        //  Track
        using (ImRaii.Disabled(_file == null))
        {
            if (ImGui.BeginMenu("Track"))
            {
                var hasSel = _selectedTrackIndices.Count > 0;
                var selSuffix = hasSel ? $" ({_selectedTrackIndices.Count})" : string.Empty;

                if (ImGui.MenuItem($"Clone Selected Tracks{selSuffix}", default, false, hasSel))
                {
                    foreach (var idx in _selectedTrackIndices.OrderByDescending(i => i))
                        _file!.CloneTrack(idx);
                    _selectedTrackIndices.Clear();
                    _globalTracksChecked = false;
                }

                var canDelete = hasSel && _selectedTrackIndices.Any(
                    i => i < _file!.Tracks.Count && !_file.Tracks[i].IsConductorTrack);

                if (ImGui.MenuItem($"Delete Selected Tracks{selSuffix}", default, false, canDelete))
                    DeleteSelectedTracks();

                ImGui.Separator();

                if (ImGui.MenuItem("Merge Selected Tracks", default, false, false))
                { /* TODO */ }

                if (ImGui.MenuItem("Transpose Selected Tracks...", default, false, false))
                { /* TODO */ }

                if (ImGui.MenuItem("Quantize Selected Tracks...", default, false, false))
                { /* TODO */ }

                ImGui.Separator();

                if (ImGui.MenuItem("Consolidate Tempo to Conductor Track"))
                    _file?.ConsolidateTempoToConductorTrack();

                ImGui.Separator();

                var canSplit = _selectedTrackIndex >= 0
                    && _selectedTrackIndex < _file!.Tracks.Count
                    && _file.Tracks[_selectedTrackIndex].HasMultipleChannels;

                if (ImGui.MenuItem("Split Selected Track by Channel", default, false, canSplit))
                {
                    _file!.SplitTrackByChannel(_selectedTrackIndex);
                    if (_selectedTrackIndex >= _file.Tracks.Count)
                    {
                        _selectedTrackIndex = -1;
                        _selectedEventIndices.Clear();
                    }
                    _selectedTrackIndices.Clear();
                }

                ImGui.EndMenu();
            }
        }

        //  View
        if (ImGui.BeginMenu("View"))
        {
            ImGui.TextDisabled("(coming soon)");
            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }
}
