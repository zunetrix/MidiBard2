using System;
using System.Collections.Generic;

using LiteDB;

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
    public int Rating { get; set; }
    public DateTime? LastPlayedAt { get; set; }
    public DateTime FileLastModifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string Comments { get; set; } = string.Empty;
    public bool IsValid { get; set; }

    /// <summary>
    /// Optional application-managed sync ID. When set, this value is embedded in the
    /// file name as "[SyncId]" (e.g. "my song [42].mid") so the record can be
    /// re-identified after a rename. Distinct from the DB primary key (Id) - always
    /// assigned sequentially by the application and never reused automatically,
    /// unless the user explicitly runs "Stamp IDs" with Fill Gaps enabled.
    /// Null means this song is not participating in file-ID sync.
    /// </summary>
    public int? SyncId { get; set; } = null;

    // DbRef to Tags collection - stores only the tag IDs
    [BsonRef("tags")]
    public List<Tag> Tags { get; set; } = new();


    /// <summary>
    /// Set the rating for this song (0-5).
    /// </summary>
    public void SetRating(int newRating)
    {
        if (newRating < 0 || newRating > 5)
            return;

        Rating = newRating;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Validate the file path - checks if file exists and updates HasValidFilePath.
    /// </summary>
    public void ValidateFile()
    {
        IsValid = !string.IsNullOrWhiteSpace(FilePath) &&
            System.IO.File.Exists(FilePath);
        UpdatedAt = DateTime.UtcNow;
    }
}
