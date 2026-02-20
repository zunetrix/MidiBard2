using System;
using System.Collections.Generic;

namespace MidiBard.Playlist;

public class Playlist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public List<PlaylistSong> Songs { get; set; } = new();
    public int CurrentSongIndex { get; set; } = -1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Calculated total duration of all songs in the playlist.
    /// Computed automatically when Songs collection changes.
    /// </summary>
    public TimeSpan PlaylistDuration => TimeSpan.Zero; // TODO: Calculate from Song table
}
