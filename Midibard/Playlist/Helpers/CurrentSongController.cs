namespace MidiBard.Playlist.Helpers;

/// <summary>
/// Manages current song identity-based tracking using reference instead of index.
/// Survives playlist mutations (reorder, remove) since it tracks by object identity.
/// </summary>
internal class CurrentSongController
{
    /// <summary>
    /// Current song being played by reference (survives reordering/mutations).
    /// </summary>
    public PlaylistSong? CurrentPlayingSong { get; set; }

    /// <summary>
    /// Get the current song index based on identity reference.
    /// Returns -1 if no song is currently playing.
    /// O(n) operation - calculates position from identity.
    /// </summary>
    public int GetCurrentSongIndex(Playlist? currentPlaylist)
    {
        if (CurrentPlayingSong == null || currentPlaylist == null)
            return -1;

        return currentPlaylist.Songs.IndexOf(CurrentPlayingSong);
    }

    /// <summary>
    /// Set current song by index, converting to identity-based reference.
    /// </summary>
    public void SetCurrentSongByIndex(int index, Playlist? currentPlaylist)
    {
        if (index < 0 || currentPlaylist == null || index >= currentPlaylist.Songs.Count)
        {
            CurrentPlayingSong = null;
            return;
        }

        CurrentPlayingSong = currentPlaylist.Songs[index];
    }

    /// <summary>
    /// Reset current song to null (no song playing).
    /// </summary>
    public void Clear()
    {
        CurrentPlayingSong = null;
    }

    /// <summary>
    /// Check if current song is in the playlist (by identity comparison).
    /// </summary>
    public bool IsCurrentSongInPlaylist(Playlist? currentPlaylist)
    {
        if (CurrentPlayingSong == null || currentPlaylist == null)
            return false;

        return currentPlaylist.Songs.IndexOf(CurrentPlayingSong) >= 0;
    }
}
