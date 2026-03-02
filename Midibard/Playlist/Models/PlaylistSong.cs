using System;

using LiteDB;

namespace MidiBard.Playlist;

/// <summary>
/// Embedded song in a playlist.
/// Stored as embedded document inside Playlist.Songs array.
/// Order is determined by position in the array.
/// </summary>
public class PlaylistSong
{
    public int Id { get; set; }

    // DbRef to Song - allows automatic loading of the Song object
    [BsonRef("songs")]
    public Song? Song { get; set; }

    public bool IsPlayed { get; set; } = false;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Indicates if this playlist song entry is in a valid state.
    /// </summary>
    public bool IsValid => Song != null && Song.IsValid && !string.IsNullOrWhiteSpace(Song.FilePath);

    /// <summary>
    /// Gets a display summary of the song (Name - Artist (Duration)).
    /// </summary>
    public string SummaryDisplay => Song != null
        ? $"{Song.Name} - {Song.Artist} ({Song.Duration.TotalMinutes:F1}m)"
        : "[Unknown]";

    /// <summary>
    /// Mark this song as played in the context of this playlist.
    /// </summary>
    public void MarkAsPlayed()
    {
        IsPlayed = true;
    }

    /// <summary>
    /// Reset the played status for this song in this playlist.
    /// </summary>
    public void ResetPlayedStatus()
    {
        IsPlayed = false;
    }
}
