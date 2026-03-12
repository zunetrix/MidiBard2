namespace MidiBard.Ipc;

internal partial class IpcProvider
{
    public void LoadPlaylist(int playlistId)
    {
        var message = IpcMessage.Create(IpcMessageType.LoadPlaylist, playlistId).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.LoadPlaylist)]
    private void HandleLoadPlaylist(IpcMessage message)
    {
        _ = Plugin.PlaylistManager.LoadPlaylistByIdAsync(message.DataStruct<int>());
    }

    public void RemoveTrackIndex(int songIndex)
    {
        var message = IpcMessage.Create(IpcMessageType.RemoveTrackIndex, songIndex).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.RemoveTrackIndex)]
    private void HandleRemoveTrackIndex(IpcMessage message)
    {
        Plugin.PlaylistManager.RemoveSongLocal(message.DataStruct<int>());
    }

    public void MoveSongToIndex(int songIndex, int targetIndex)
    {
        var message = IpcMessage.Create(IpcMessageType.MoveSongToIndex, (songIndex, targetIndex)).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.MoveSongToIndex)]
    private void HandleMoveSongToIndex(IpcMessage message)
    {
        var (from, to) = message.DataStruct<(int, int)>();
        Plugin.PlaylistManager.MoveSongToIndexLocal(from, to);
    }

    public void ChangeSongPlayedStatus(int songIndex, bool newStatus)
    {
        var message = IpcMessage.Create(IpcMessageType.ChangeSongPlayedStatus, (songIndex, newStatus)).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.ChangeSongPlayedStatus)]
    private void HandleChangeSongPlayedStatus(IpcMessage message)
    {
        var (index, status) = message.DataStruct<(int, bool)>();
        Plugin.PlaylistManager.ChangeSongPlayedStatusLocal(index, status);
    }

    public void ResetAllSongsPlayedStatus()
    {
        var message = IpcMessage.Create(IpcMessageType.ResetAllSongsPlayedStatus).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.ResetAllSongsPlayedStatus)]
    private void HandleResetAllSongsPlayedStatus(IpcMessage message)
    {
        Plugin.PlaylistManager.ResetAllSongsPlayedStatusLocal();
    }

    public void LoadTempPlaylist(string[] filePaths)
    {
        var message = IpcMessage.Create(IpcMessageType.LoadTempPlaylist, filePaths).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.LoadTempPlaylist)]
    private void HandleLoadTempPlaylist(IpcMessage message)
    {
        _ = Plugin.PlaylistManager.LoadTempPlaylistAsync(message.StringData);
    }
}
