using System;
using System.Collections.Generic;

namespace MidiBard.Playlist;

/// <summary>
/// Represents a song/music file - independent entity.
/// </summary>
public class Song
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public int ReleaseYear { get; set; }
    public TimeSpan Duration { get; set; }
    public int PlayCount { get; set; }
    public int Rate { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime? LastPlayedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
