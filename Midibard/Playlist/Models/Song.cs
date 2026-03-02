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
    public bool HasValidFilePath { get; set; }

    // DbRef to Tags collection - stores only the tag IDs
    [BsonRef("tags")]
    public List<Tag> Tags { get; set; } = new();

    public DateTime? LastPlayedAt { get; set; }
    public DateTime? FileLastModifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Indicates if the song is in a valid state.
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(FilePath) &&
        Duration > TimeSpan.Zero &&
        HasValidFilePath;

    /// <summary>
    /// Gets a display summary of the song (Name - Artist (Duration)).
    /// </summary>
    public string SummaryDisplay =>
        $"{Name} - {Artist} ({Duration.TotalMinutes:F1}m)";

    /// <summary>
    /// Record a play of this song - increments play count and updates last played time.
    /// </summary>
    public void RecordPlay()
    {
        PlayCount++;
        LastPlayedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

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
        HasValidFilePath = !string.IsNullOrWhiteSpace(FilePath) &&
            System.IO.File.Exists(FilePath);
        UpdatedAt = DateTime.UtcNow;
    }
}
