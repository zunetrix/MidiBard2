using System;
using System.IO;

namespace MidiBard.Playlist;

/// <summary>
/// Extension methods for PlaylistSong to provide computed properties
/// that were previously in SongEntry.
/// </summary>
public static class PlaylistSongExtensions
{
    /// <summary>
    /// Get the file name without extension from the song file path.
    /// </summary>
    public static string GetFileName(this PlaylistSong playlistSong)
    {
        if (playlistSong?.Song?.FilePath == null)
            return string.Empty;
        return Path.GetFileNameWithoutExtension(playlistSong.Song.FilePath);
    }

    /// <summary>
    /// Get the directory path from the song file path.
    /// </summary>
    public static string GetFileDirectory(this PlaylistSong playlistSong)
    {
        if (playlistSong?.Song?.FilePath == null)
            return string.Empty;
        return Path.GetDirectoryName(playlistSong.Song.FilePath) ?? string.Empty;
    }

    /// <summary>
    /// Get the file path of the associated LRC (lyric) file.
    /// </summary>
    public static string GetLrcPath(this PlaylistSong playlistSong)
    {
        if (playlistSong?.Song?.FilePath == null)
            return string.Empty;
        return Path.ChangeExtension(playlistSong.Song.FilePath, "lrc");
    }

    /// <summary>
    /// Get the formatted duration string (HH:MM:SS or MM:SS).
    /// </summary>
    public static string GetSongLengthFormated(this PlaylistSong playlistSong)
    {
        var duration = playlistSong?.Song?.Duration ?? TimeSpan.Zero;
        return $"{(duration.Hours != 0 ? duration.Hours + ":" : "")}{duration.Minutes:00}:{duration.Seconds:00}";
    }

    /// <summary>
    /// Get the duration of the song.
    /// </summary>
    public static TimeSpan GetSongLength(this PlaylistSong playlistSong)
    {
        return playlistSong?.Song?.Duration ?? TimeSpan.Zero;
    }

    /// <summary>
    /// Get the file path of the song.
    /// </summary>
    public static string GetFilePath(this PlaylistSong playlistSong)
    {
        return playlistSong?.Song?.FilePath ?? string.Empty;
    }
}
