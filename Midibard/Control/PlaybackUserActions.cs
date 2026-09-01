using System;
using System.IO;
using System.Threading.Tasks;

using MidiBard.Extensions.Dalamud.Party;

namespace MidiBard.Control;

internal sealed class PlaybackUserActions
{
    private const int ChatLoadTimeoutMs = 10000;
    private const int ChatLoadPollMs = 50;

    private readonly Plugin _plugin;

    public PlaybackUserActions(Plugin plugin)
    {
        _plugin = plugin;
    }

    public async Task<bool> LoadPlaylistSong(int songIndex)
    {
        if (AgentManager.AgentMetronome.EnsembleModeRunning)
            return false;

        var playlist = _plugin.PlaylistManager.CurrentPlaylist;
        if (playlist == null || songIndex < 0 || songIndex >= playlist.Songs.Count)
            return false;

        if (UseChatPlaylistSync(
                _plugin.Config.playOnMultipleDevices,
                DalamudApi.PartyList.Length))
        {
            var expectedFileName = playlist.Songs[songIndex].Song == null
                ? null
                : Path.GetFileName(playlist.Songs[songIndex].Song.FilePath);

            _plugin.ChatWatcher.SendSwitchTo(songIndex);

            if (expectedFileName == null)
                return false;

            var deadline = DateTime.UtcNow.AddMilliseconds(ChatLoadTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (_plugin.PlaylistManager.CurrentSongIndex == songIndex &&
                    _plugin.CurrentBardPlayback.IsLoaded &&
                    string.Equals(
                        Path.GetFileName(_plugin.CurrentBardPlayback.FilePath),
                        expectedFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                await Task.Delay(ChatLoadPollMs);
            }

            return false;
        }

        _plugin.MidiPlayerControl.StopLrc();
        return await _plugin.PlaylistManager.LoadPlayback(songIndex);
    }

    public void PlayPause()
    {
        _plugin.MidiPlayerControl.PlayPause();
    }

    public void StopPlayback()
    {
        if (_plugin.FilePlayback.IsWaiting)
            _plugin.FilePlayback.CancelWaiting();
        else
            _plugin.MidiPlayerControl.Stop();

        _plugin.EnsembleManager.BroadcastUnequipInstruments();
    }

    public void BeginEnsembleReadyCheck()
    {
        if (_plugin.Config.UpdateInstrumentBeforeReadyCheck)
        {
            _plugin.EnsembleManager.BroadcastEquipInstruments();
            _plugin.EnsembleManager.BeginEnsembleReadyCheck(
                _plugin.Config.PreReadyCheckDelayMs);
            return;
        }

        _plugin.EnsembleManager.BeginEnsembleReadyCheck();
    }

    internal static bool UseChatPlaylistSync(
        bool playOnMultipleDevices,
        int partyMemberCount)
        => playOnMultipleDevices && partyMemberCount > 1;
}
