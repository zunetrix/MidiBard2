using System;
using System.Collections.Generic;
using System.Linq;

namespace MidiBard.Playlist;

public class Playlist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Embedded list of PlaylistSong - order is determined by array position
    public List<PlaylistSong> Songs { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Calculated total duration of all songs in the playlist.
    /// </summary>
    public TimeSpan Duration => CalculateDuration();

    /// <summary>
    /// Indicates if the playlist is in a valid state.
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Songs.Count >= 0;

    /// <summary>
    /// Add a song to the playlist.
    /// </summary>
    public void AddSong(PlaylistSong playlistSong)
    {
        if (playlistSong == null)
            return;

        Songs.Add(playlistSong);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Remove a song at the specified index.
    /// </summary>
    /// <returns>True if removal was successful, false otherwise.</returns>
    public bool RemoveSongAt(int index)
    {
        if (!IsValidIndex(index))
            return false;

        Songs.RemoveAt(index);
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Move a song from one position to another.
    /// </summary>
    /// <returns>True if move was successful, false otherwise.</returns>
    public bool MoveSongToIndex(int fromIndex, int toIndex)
    {
        if (!IsValidIndex(fromIndex) || !IsValidIndex(toIndex))
            return false;

        var song = Songs[fromIndex];
        Songs.RemoveAt(fromIndex);
        Songs.Insert(toIndex, song);
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Mark a song as played at the specified index.
    /// </summary>
    /// <returns>True if mark was successful, false otherwise.</returns>
    public bool MarkSongAsPlayed(int index)
    {
        if (!IsValidIndex(index))
            return false;

        Songs[index].IsPlayed = true;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Reset the played status for all songs in the playlist.
    /// </summary>
    public void ResetAllSongsPlayedStatus()
    {
        foreach (var song in Songs)
            song.IsPlayed = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Get all songs that are ready for sync (have valid Song reference).
    /// </summary>
    public IEnumerable<PlaylistSong> GetSongsForSync()
    {
        return Songs.Where(ps => ps.Song != null).ToList();
    }

    private bool IsValidIndex(int index) =>
        index >= 0 && index < Songs.Count;

    private TimeSpan CalculateDuration()
    {
        try
        {
            return TimeSpan.FromMilliseconds(
                Songs.Where(s => s.Song != null)
                    .Sum(s => s.Song!.Duration.TotalMilliseconds)
            );
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }
}
