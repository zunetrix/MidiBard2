using System;
using System.Collections.Generic;

namespace MidiBard.Playlist;

public class Song
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public TimeSpan SongDuration { get; set; }
    public bool IsSongPlayed { get; set; }
    public int PlayCount { get; set; }
    public double Rate { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime? LastPlayedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
