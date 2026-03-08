namespace MidiBard.Playlist.Helpers;

/// <summary>
/// Shared extension methods for Playlist used across all helpers.
/// </summary>
internal static class PlaylistExtensions
{
    /// <summary>
    /// Returns true if <paramref name="songIndex"/> is a valid index into the playlist's Songs list.
    /// </summary>
    public static bool IsValidSongIndex(this Playlist? playlist, int songIndex)
    {
        if (playlist == null || playlist.Songs == null || playlist.Songs.Count == 0)
            return false;
        return songIndex >= 0 && songIndex < playlist.Songs.Count;
    }
}
