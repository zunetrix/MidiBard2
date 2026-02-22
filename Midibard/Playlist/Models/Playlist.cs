using System;
using System.Collections.Generic;
using System.Linq;

using LiteDB;

namespace MidiBard.Playlist;

public class Playlist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // DbRef to PlaylistSong collection - stores only the PlaylistSong IDs
    [BsonRef("playlist_songs")]
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
