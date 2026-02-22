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

    public bool IsPlayed { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
