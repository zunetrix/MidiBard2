using System;

using LiteDB;

namespace MidiBard.Playlist;

/// <summary>
/// Join table between Playlist and Song.
/// Contains only the relationship/order info - all other data is in Song.
/// </summary>
public class PlaylistSong
{
    public int Id { get; set; }

    // DbRef to Playlist - allows automatic loading of the Playlist object
    [BsonRef("playlists")]
    public Playlist? Playlist { get; set; }

    // DbRef to Song - allows automatic loading of the Song object
    [BsonRef("songs")]
    public Song? Song { get; set; }

    public int Order { get; set; }
    public bool IsPlayed { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
