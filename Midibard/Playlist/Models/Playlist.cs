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
