using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Time;
using MidiBard.Resources;

namespace MidiBard;

public partial class MainWindow
{
    private bool _playlistScrollToCurrentSong;

    private void DrawCurrentPlaying()
    {
        if (Plugin.CurrentBardPlayback.IsLoaded)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Plugin.Config.themeColor * new Vector4(1, 1, 1, 1.3f)))
            {
                ImGui.Text(Plugin.CurrentBardPlayback.DisplayName);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            if (ImGui.IsItemClicked())
            {
                _playlistScrollToCurrentSong = true;
            }
            ImGuiUtil.ToolTip("Click to scroll to current playing song in playlist");
        }
        else
        {
            var totalDuration = Plugin.PlaylistManager.CurrentPlaylist?.Duration ?? TimeSpan.Zero;
            var durationString = totalDuration == TimeSpan.Zero
                ? ""
                : $"Duration: {totalDuration.GetDurationString()}";

            var totalSongs = Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0;
            var tracksText = string.Format(Language.main_status_tracks_in_playlist, totalSongs);
            ImGui.Text(tracksText);

            ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() - ImGui.CalcTextSize(durationString).X + ImGui.GetCursorPosX());
            ImGui.Text(durationString);
        }
    }
}
