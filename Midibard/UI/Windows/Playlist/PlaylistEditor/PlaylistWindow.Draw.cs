using System;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public partial class PlaylistWindow
{
    public override void Draw()
    {
        DrawMenuBar();
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
                if (ImGui.BeginMenuBar())
                {
                    // DrawFileMenu();
                    // DrawActionsMenu();

                    if (ImGui.MenuItem("Tags"))
                    {
                        Plugin.Ui.TagsWindow.Toggle();
                    }

                    // DrawCommandsMenu();

                    if (ImGui.MenuItem("Help"))
                    {
                        //TODO
                    }

                    var versionText = $"v{Plugin.Version}";
                    var textSize = ImGui.CalcTextSize(versionText);
                    var padding = ImGui.GetStyle().FramePadding.X + 5;
                    var regionMaxX = ImGui.GetWindowContentRegionMax().X;
                    // align to right
                    ImGui.SameLine(regionMaxX - textSize.X - (padding * 2));
                    ImGui.Text(versionText);

                    ImGui.EndMenuBar();
                }

            }
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
