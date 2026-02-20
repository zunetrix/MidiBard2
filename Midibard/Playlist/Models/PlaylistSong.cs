using System;

namespace MidiBard.Playlist;

/// <summary>
/// Join table between Playlist and Song.
/// Contains only the relationship/order info - all other data is in Song.
/// </summary>
public class PlaylistSong
{
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public int SongId { get; set; }
    public int Order { get; set; }
    public bool IsPlayed { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
