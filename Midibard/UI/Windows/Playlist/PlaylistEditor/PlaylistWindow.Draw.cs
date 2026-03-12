using System;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Time;

namespace MidiBard;

public partial class PlaylistWindow
{
    public override void Draw()
    {
        DrawMenuBar();
        if (_pendingPopup != null) { ImGui.OpenPopup(_pendingPopup); _pendingPopup = null; }
        DrawColumnsPopup();
        // Show import progress if importing
        if (_importHelper.IsImporting)
        {
            DrawImportProgress();
        }

        // Display message if there's one
        _messageDisplay.Draw();

        if (_isLoading)
        {
            ImGuiUtil.DrawColoredBanner("Loading...", Style.Colors.Violet);
            return;
        }

        if (_showPlaylistEditorLeftPanel)
        {
            // Calculate resizable panel width
            var totalAvail = ImGui.GetContentRegionAvail().X;
            var minPanelPx = 120f * ImGuiHelpers.GlobalScale;
            var maxPanelPx = MathF.Max(minPanelPx, totalAvail - minPanelPx);
            _leftPanelWidth = MathF.Max(minPanelPx, MathF.Min(_leftPanelWidth, maxPanelPx));

            // Left panel - Playlist list
            ImGui.BeginChild("##PlaylistTabs", ImGuiHelpers.ScaledVector2(_leftPanelWidth, -1), true);
            DrawLeftPanel();
            ImGui.EndChild();

            // Splitter for resizing
            ImGui.SameLine();
            DrawSplitter(ref _leftPanelWidth, minPanelPx, maxPanelPx);

            ImGui.SameLine();
        }

        // Right panel - Playlist details
        ImGui.BeginChild("PlaylistDetails", ImGuiHelpers.ScaledVector2(-1, -1), true);
        if (_selectedPlaylist != null)
        {
            DrawRightPanel();
        }
        else
        {
            ImGui.Text("Select a playlist to view details");
        }

        ImGui.EndChild();
    }

    private void DrawMenuBar()
    {
        using (ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor))
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1))
            {
                using var menuBar = ImRaii.MenuBar();
                if (!menuBar) return;

                DrawFileMenu();

                if (ImGui.MenuItem("Tags"))
                    Plugin.Ui.TagsWindow.Toggle();

                if (ImGui.MenuItem("Columns"))
                    OpenPopup("PlaylistColumnsPopup");
            }
        }
    }

    private void DrawSongCounter()
    {
        var btnLabel = $"Songs: {PlaylistSongs.Count} Duration: {_playlistTotalDuration.GetDurationString()}";
        var btnWidth = ImGui.CalcTextSize(btnLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - btnWidth - 10 * ImGuiHelpers.GlobalScale);
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonBlueNormal)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonBlueNormal))
        {
            ImGui.Button($"{btnLabel}##PlaylistInfo");
        }
    }

    private void DrawFileMenu()
    {
        using var menu = ImRaii.Menu("File");
        if (!menu) return;

        ImGuiUtil.TextIcon(FontAwesomeIcon.FileImport);
        ImGui.SameLine();
        if (ImGui.Selectable("Import Rules"))
            Plugin.Ui.ExtractionRulesWindow.Toggle();
        ImGuiUtil.ToolTip("Define rules to extract info from file name into song collection");

        ImGuiUtil.TextIcon(FontAwesomeIcon.FileExport);
        ImGui.SameLine();
        if (ImGui.Selectable("Export"))
        {
            if (_selectedPlaylist != null)
                Plugin.Ui.ExportWindow.OpenForPlaylist(_selectedPlaylist.Name, PlaylistSongs);
        }

        ImGui.Separator();

        ImGuiUtil.TextIcon(FontAwesomeIcon.FileImport);
        ImGui.SameLine();
        if (ImGui.Selectable("Import JSON Playlist"))
        {
            // TODO: implement import from old playlist format
        }
    }

    private void DrawImportProgress()
    {
        using (ImRaii.PushColor(ImGuiCol.PlotHistogram, Style.Colors.GrassGreen))
        {
            ImGui.ProgressBar(_importHelper.GetProgressValue(), ImGuiHelpers.ScaledVector2(-1, 20), _importHelper.GetProgressText());
        }

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonDangerNormal)
        .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonDangerHovered)
        .Push(ImGuiCol.ButtonActive, Style.Components.ButtonDangerActive))
        {
            if (ImGui.Button("Cancel"))
                CancelImport();
        }
    }
}
