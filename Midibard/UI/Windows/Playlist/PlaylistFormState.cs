using System;
using System.Collections.Generic;

using MidiBard.Playlist;

namespace MidiBard;

/// <summary>
/// Encapsulates form-related UI state for the Playlist window.
/// Provides methods compatible with ISongEditFormState interface pattern.
/// Uses fields (not properties) for ImGui ref parameter compatibility.
/// </summary>
public class PlaylistFormState
{
    // Edit song fields
    public string EditFilePath { get; set; } = string.Empty;
    public string EditName = string.Empty;
    public string EditArtist = string.Empty;
    public int EditReleaseYear = 0;
    public int EditRating = 0;
    public string EditDuration { get; set; } = string.Empty;
    public int EditPlayCount = 0;
    public string EditLastPlayedAt { get; set; } = string.Empty;
    public string EditCreatedAt { get; set; } = string.Empty;
    public string EditUpdatedAt { get; set; } = string.Empty;
    public string EditAddedAt { get; set; } = string.Empty;
    public bool EditIsPlayed = false;
    public string EditTag = string.Empty;

    // Tag editing
    public int SelectedTagIndex = -1;
    public List<Tag> AvailableTags = new();
    public bool TagsLoaded = false;

    // New playlist input
    public string NewPlaylistName = string.Empty;

    public void LoadEditPlaylistSongState(PlaylistSong playlistSong)
    {
        EditFilePath = playlistSong.Song.FilePath ?? "";
        EditName = playlistSong.Song.Name ?? "";
        EditArtist = playlistSong.Song.Artist ?? "";
        EditReleaseYear = playlistSong.Song.ReleaseYear;
        EditRating = playlistSong.Song.Rating;
        EditDuration = playlistSong.Song.Duration.ToString(@"mm\:ss");
        EditPlayCount = playlistSong.Song.PlayCount;
        EditLastPlayedAt = playlistSong.Song.LastPlayedAt?.ToString("g") ?? "-";
        EditCreatedAt = playlistSong.Song.CreatedAt.ToString("g");
        EditUpdatedAt = playlistSong.Song.UpdatedAt.ToString("g");
        EditAddedAt = playlistSong?.AddedAt.ToString("g") ?? "-";
        EditIsPlayed = playlistSong?.IsPlayed ?? false;
    }

    public void ClearEditFields()
    {
        EditFilePath = string.Empty;
        EditName = string.Empty;
        EditArtist = string.Empty;
        EditReleaseYear = 0;
        EditRating = 0;
        EditDuration = string.Empty;
        EditPlayCount = 0;
        EditLastPlayedAt = string.Empty;
        EditCreatedAt = string.Empty;
        EditUpdatedAt = string.Empty;
        EditAddedAt = string.Empty;
        EditIsPlayed = false;
        EditTag = string.Empty;
    }

    // ISongEditFormState implementation
    /// <summary>
    /// Load edit state from a Song entity (global context, used by SongsWindow).
    /// </summary>
    public void LoadFromSong(Song song)
    {
        if (song == null) return;

        EditFilePath = song.FilePath ?? "";
        EditName = song.Name ?? "";
        EditArtist = song.Artist ?? "";
        EditReleaseYear = song.ReleaseYear;
        EditRating = song.Rating;
        EditDuration = song.Duration.ToString(@"mm\:ss");
        EditPlayCount = song.PlayCount;
        EditLastPlayedAt = song.LastPlayedAt?.ToString("g") ?? "-";
        EditCreatedAt = song.CreatedAt.ToString("g");
        EditUpdatedAt = song.UpdatedAt.ToString("g");
        EditAddedAt = "-";  // No playlist context in global load
        EditIsPlayed = false;  // No playlist context in global load
    }

    /// <summary>
    /// Load edit state from a PlaylistSong entity (playlist context, used by PlaylistWindow).
    /// </summary>
    public void LoadFromPlaylistSong(PlaylistSong playlistSong, int playlistId)
    {
        LoadEditPlaylistSongState(playlistSong);
    }

    /// <summary>
    /// Get the Song entity with changes applied.
    /// </summary>
    public Song GetSongChanges()
    {
        return new Song
        {
            FilePath = EditFilePath,
            Name = EditName,
            Artist = EditArtist,
            ReleaseYear = EditReleaseYear,
            Rating = EditRating,
            PlayCount = EditPlayCount,
            Duration = TimeSpan.Parse(EditDuration),  // Assumes format is valid
            LastPlayedAt = EditLastPlayedAt == "-" ? null : DateTime.Parse(EditLastPlayedAt),
            CreatedAt = DateTime.Parse(EditCreatedAt),
            UpdatedAt = DateTime.Parse(EditUpdatedAt)
        };
    }

    /// <summary>
    /// Get the PlaylistSong entity with changes applied.
    /// Returns null if in global context (SongsWindow).
    /// </summary>
    public PlaylistSong? GetPlaylistSongChanges()
    {
        // Only PlaylistWindow uses this context
        return new PlaylistSong
        {
            IsPlayed = EditIsPlayed,
            AddedAt = EditAddedAt == "-" ? DateTime.UtcNow : DateTime.Parse(EditAddedAt)
        };
    }
}
