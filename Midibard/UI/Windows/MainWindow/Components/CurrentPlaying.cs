using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using MidiBard.Extensions.Time;
using MidiBard.Resources;

namespace MidiBard;

public partial class MainWindow
{
    static bool playlistScrollToCurrentSong = false;
    private void DrawCurrentPlaying()
    {
        if (Plugin.CurrentBardPlayback != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Plugin.Config.themeColor * new Vector4(1, 1, 1, 1.3f));
            ImGui.TextUnformatted(Plugin.CurrentBardPlayback.DisplayName);
            ImGui.PopStyleColor();

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            if (ImGui.IsItemClicked())
            {
                playlistScrollToCurrentSong = true;
            }
            ImGuiUtil.ToolTip("Click to scroll to current playing song in playlist");
        }
        else
        {
            var totalDuration = Plugin.PlaylistManager.CurrentContainer.TotalDuration;
            var durationString = totalDuration == TimeSpan.Zero
                ? ""
                : $"Duration: {totalDuration.GetDurationString()}";

            var totalSongs = Plugin.PlaylistManager.FilePathList.Count;
            var tracksText = string.Format(Language.text_tracks_in_playlist, totalSongs);
            ImGui.TextUnformatted($"{tracksText}");

            ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() - ImGui.CalcTextSize(durationString).X + ImGui.GetCursorPosX());
            ImGui.TextUnformatted($"{durationString}");
        }
    }
}
